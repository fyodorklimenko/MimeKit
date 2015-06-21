﻿//
// HtmlTokenizer.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013-2015 Xamarin Inc. (www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System.IO;
using System.Text;

namespace MimeKit.Text {
	/// <summary>
	/// An HTML tokenizer.
	/// </summary>
	/// <remarks>
	/// Tokenizes HTML text, emitting an <see cref="HtmlToken"/> for each token it encounters.
	/// </remarks>
	public class HtmlTokenizer
	{
		const string DocType = "doctype";
		const string CData = "[CDATA[";

		readonly HtmlEntityDecoder entity = new HtmlEntityDecoder ();
		readonly StringBuilder data = new StringBuilder ();
		readonly StringBuilder name = new StringBuilder ();
		readonly char[] cdata = new char[3];
		HtmlDocTypeToken doctype;
		HtmlAttribute attribute;
		string activeTagName;
		HtmlTagToken tag;
		int cdataIndex;
		bool isEndTag;
		char quote;

		TextReader text;

		/// <summary>
		/// Initializes a new instance of the <see cref="HtmlTokenizer"/> class.
		/// </summary>
		/// <remarks>
		/// Creates a new <see cref="HtmlTokenizer"/>.
		/// </remarks>
		/// <param name="reader">The <see cref="TextReader"/>.</param>
		public HtmlTokenizer (TextReader reader)
		{
			DecodeCharacterReferences = true;
			LinePosition = 1;
			LineNumber = 1;
			text = reader;
		}

		/// <summary>
		/// Get or set whether or not the tokenizer should decode character references.
		/// </summary>
		/// <remarks>
		/// <para>Gets or sets whether or not the tokenizer should decode character references.</para>
		/// <para>Note: Character references in attribute values will still be decoded even if this
		/// value is set to <c>false</c>.</para>
		/// </remarks>
		/// <value><c>true</c> if character references should be decoded; otherwise, <c>false</c>.</value>
		public bool DecodeCharacterReferences {
			get; set;
		}

		/// <summary>
		/// Get the current HTML namespace detected by the tokenizer.
		/// </summary>
		/// <remarks>
		/// Gets the current HTML namespace detected by the tokenizer.
		/// </remarks>
		/// <value>The html namespace.</value>
		public HtmlNamespace HtmlNamespace {
			get; private set;
		}

		/// <summary>
		/// Gets the current line number.
		/// </summary>
		/// <remarks>
		/// <para>This property is most commonly used for error reporting, but can be called
		/// at any time. The starting value for this property is <c>1</c>.</para>
		/// <para>Combined with <see cref="LinePosition"/>, a value of <c>1,1</c> indicates
		/// the start of the document.</para>
		/// </remarks>
		/// <value>The current line number.</value>
		public int LineNumber {
			get; private set;
		}

		/// <summary>
		/// Gets the current line position.
		/// </summary>
		/// <remarks>
		/// <para>This property is most commonly used for error reporting, but can be called
		/// at any time. The starting value for this property is <c>1</c>.</para>
		/// <para>Combined with <see cref="LineNumber"/>, a value of <c>1,1</c> indicates
		/// the start of the document.</para>
		/// </remarks>
		/// <value>The current line number.</value>
		public int LinePosition {
			get; private set;
		}

		/// <summary>
		/// Get the current state of the tokenizer.
		/// </summary>
		/// <remarks>
		/// Gets the current state of the tokenizer.
		/// </remarks>
		/// <value>The current state of the tokenizer.</value>
		public HtmlTokenizerState TokenizerState {
			get; private set;
		}

		/// <summary>
		/// Create a DOCTYPE token.
		/// </summary>
		/// <remarks>
		/// Creates a DOCTYPE token.
		/// </remarks>
		/// <returns>The DOCTYPE token.</returns>
		protected virtual HtmlDocTypeToken CreateDocType ()
		{
			return new HtmlDocTypeToken ();
		}

		HtmlDocTypeToken CreateDocTypeToken (string rawTagName)
		{
			var token = CreateDocType ();
			token.RawTagName = rawTagName;
			return token;
		}

		/// <summary>
		/// Create an HTML comment token.
		/// </summary>
		/// <remarks>
		/// Creates an HTML comment token.
		/// </remarks>
		/// <returns>The HTML comment token.</returns>
		/// <param name="comment">The comment.</param>
		protected virtual HtmlCommentToken CreateCommentToken (string comment)
		{
			return new HtmlCommentToken (comment);
		}

		/// <summary>
		/// Create an HTML character data token.
		/// </summary>
		/// <remarks>
		/// Creates an HTML character data token.
		/// </remarks>
		/// <returns>The HTML character data token.</returns>
		/// <param name="data">The character data.</param>
		protected virtual HtmlDataToken CreateDataToken (string data)
		{
			return new HtmlDataToken (data);
		}

		/// <summary>
		/// Create an HTML character data token.
		/// </summary>
		/// <remarks>
		/// Creates an HTML character data token.
		/// </remarks>
		/// <returns>The HTML character data token.</returns>
		/// <param name="data">The character data.</param>
		protected virtual HtmlCDataToken CreateCDataToken (string data)
		{
			return new HtmlCDataToken (data);
		}

		/// <summary>
		/// Create an HTML script data token.
		/// </summary>
		/// <remarks>
		/// Creates an HTML script data token.
		/// </remarks>
		/// <returns>The HTML script data token.</returns>
		/// <param name="data">The script data.</param>
		protected virtual HtmlScriptDataToken CreateScriptDataToken (string data)
		{
			return new HtmlScriptDataToken (data);
		}

		/// <summary>
		/// Create an HTML tag token.
		/// </summary>
		/// <remarks>
		/// Creates an HTML tag token.
		/// </remarks>
		/// <returns>The HTML tag token.</returns>
		/// <param name="name">The tag name.</param>
		/// <param name="isEndTag"><c>true</c> if the tag is an end tag; otherwise, <c>false</c>.</param>
		protected virtual HtmlTagToken CreateTagToken (string name, bool isEndTag = false)
		{
			return new HtmlTagToken (name, isEndTag);
		}

		/// <summary>
		/// Create an attribute.
		/// </summary>
		/// <remarks>
		/// Creates an attribute.
		/// </remarks>
		/// <returns>The attribute.</returns>
		/// <param name="name">THe attribute name.</param>
		protected virtual HtmlAttribute CreateAttribute (string name)
		{
			return new HtmlAttribute (name);
		}

		static bool IsAlphaNumeric (char c)
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
		}

		static bool IsAsciiLetter (char c)
		{
			return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
		}

		static char ToLower (char c)
		{
			return (c >= 'A' && c <= 'Z') ? (char) (c + 0x20) : c;
		}

		int Peek ()
		{
			return text.Peek ();
		}

		int Read ()
		{
			int c;

			if ((c = text.Read ()) == -1)
				return -1;

			if (c == '\n') {
				LinePosition = 1;
				LineNumber++;
			} else {
				LinePosition++;
			}

			return c;
		}

		// Note: value must be lowercase
		bool NameIs (string value)
		{
			if (name.Length != value.Length)
				return false;

			for (int i = 0; i < name.Length; i++) {
				if (ToLower (name[i]) != value[i])
					return false;
			}

			return true;
		}

		void EmitTagAttribute ()
		{
			attribute = CreateAttribute (name.ToString ());
			tag.Attributes.Add (attribute);
			name.Length = 0;
		}

		HtmlToken EmitCommentToken (string comment)
		{
			var token = CreateCommentToken (comment);
			data.Length = 0;
			name.Length = 0;
			return token;
		}

		HtmlToken EmitCommentToken (StringBuilder comment)
		{
			return EmitCommentToken (comment.ToString ());
		}

		HtmlToken EmitDocType ()
		{
			var token = doctype;
			data.Length = 0;
			doctype = null;
			return token;
		}

		HtmlToken EmitDataToken (bool encodeEntities)
		{
			if (data.Length == 0)
				return null;

			var token = CreateDataToken (data.ToString ());
			token.EncodeEntities = encodeEntities;
			data.Length = 0;

			return token;
		}

		HtmlToken EmitCDataToken ()
		{
			if (data.Length == 0)
				return null;

			var token = CreateCDataToken (data.ToString ());
			data.Length = 0;

			return token;
		}

		HtmlToken EmitScriptDataToken ()
		{
			if (data.Length == 0)
				return null;

			var token = CreateScriptDataToken (data.ToString ());
			data.Length = 0;

			return token;
		}

		HtmlToken EmitTagToken ()
		{
			if (!tag.IsEndTag && !tag.IsEmptyElement) {
				switch (tag.Id) {
				case HtmlTagId.Style: case HtmlTagId.Xmp: case HtmlTagId.IFrame: case HtmlTagId.NoEmbed: case HtmlTagId.NoFrames:
					TokenizerState = HtmlTokenizerState.RawText;
					activeTagName = tag.Name;
					break;
				case HtmlTagId.Title: case HtmlTagId.TextArea:
					TokenizerState = HtmlTokenizerState.RcData;
					activeTagName = tag.Name;
					break;
				case HtmlTagId.PlainText:
					TokenizerState = HtmlTokenizerState.PlainText;
					break;
				case HtmlTagId.Script:
					TokenizerState = HtmlTokenizerState.ScriptData;
					break;
				case HtmlTagId.NoScript:
					// TODO: only switch into the RawText state if scripting is enabled
					TokenizerState = HtmlTokenizerState.RawText;
					activeTagName = tag.Name;
					break;
				case HtmlTagId.Html:
					TokenizerState = HtmlTokenizerState.Data;

					for (int i = tag.Attributes.Count; i > 0; i--) {
						var attr = tag.Attributes[i - 1];

						if (attr.Id == HtmlAttributeId.XmlNS && attr.Value != null) {
							HtmlNamespace = tag.Attributes[i].Value.ToHtmlNamespace ();
							break;
						}
					}
					break;
				default:
					TokenizerState = HtmlTokenizerState.Data;
					break;
				}
			} else {
				TokenizerState = HtmlTokenizerState.Data;
			}

			var token = tag;
			data.Length = 0;
			tag = null;

			return token;
		}

		HtmlToken ReadCharacterReference (HtmlTokenizerState next)
		{
			int nc = Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				data.Append ('&');

				return EmitDataToken (true);
			}

			c = (char) nc;

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ': case '<': case '&':
				// no character is consumed, emit '&'
				TokenizerState = next;
				data.Append ('&');
				return null;
			}

			entity.Push ('&');

			while (entity.Push (c)) {
				Read ();

				if ((nc = Peek ()) == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					data.Append (entity.GetPushedInput ());
					entity.Reset ();

					return EmitDataToken (true);
				}

				c = (char) nc;
			}

			TokenizerState = next;

			data.Append (entity.GetValue ());
			entity.Reset ();

			if (c == ';') {
				// consume the ';'
				Read ();
			}

			return null;
		}

		HtmlToken ReadGenericRawTextLessThan (HtmlTokenizerState rawText, HtmlTokenizerState rawTextEndTagOpen)
		{
			int nc = Peek ();

			data.Append ('<');

			switch ((char) nc) {
			case '/':
				TokenizerState = rawTextEndTagOpen;
				data.Append ('/');
				name.Length = 0;
				Read ();
				break;
			default:
				TokenizerState = rawText;
				break;
			}

			return null;
		}

		HtmlToken ReadGenericRawTextEndTagOpen (bool decoded, HtmlTokenizerState rawText, HtmlTokenizerState rawTextEndTagName)
		{
			int nc = Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitDataToken (decoded);
			}

			c = (char) nc;

			if (IsAsciiLetter (c)) {
				TokenizerState = rawTextEndTagName;
				name.Append (c);
				data.Append (c);
				Read ();
			} else {
				TokenizerState = rawText;
			}

			return null;
		}

		HtmlToken ReadGenericRawTextEndTagName (bool decoded, HtmlTokenizerState rawText)
		{
			var current = TokenizerState;

			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitDataToken (decoded);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					if (NameIs (activeTagName)) {
						TokenizerState = HtmlTokenizerState.BeforeAttributeName;
						break;
					}

					goto default;
				case '/':
					if (NameIs (activeTagName)) {
						TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
						break;
					}
					goto default;
				case '>':
					if (NameIs (activeTagName)) {
						var token = CreateTagToken (name.ToString (), true);
						TokenizerState = HtmlTokenizerState.Data;
						data.Length = 0;
						name.Length = 0;
						return token;
					}
					goto default;
				default:
					if (!IsAsciiLetter (c)) {
						TokenizerState = rawText;
						return null;
					}

					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == current);

			tag = CreateTagToken (name.ToString (), true);
			name.Length = 0;

			return null;
		}

		HtmlToken ReadData ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					break;
				}

				c = (char) nc;

				switch (c) {
				case '&':
					if (DecodeCharacterReferences) {
						TokenizerState = HtmlTokenizerState.CharacterReferenceInData;
						return null;
					}

					goto default;
				case '<':
					TokenizerState = HtmlTokenizerState.TagOpen;
					break;
				//case 0: // parse error, but emit it anyway
				default:
					data.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.Data);

			return EmitDataToken (DecodeCharacterReferences);
		}

		HtmlToken ReadCharacterReferenceInData ()
		{
			return ReadCharacterReference (HtmlTokenizerState.Data);
		}

		HtmlToken ReadRcData ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					break;
				}

				c = (char) nc;

				switch (c) {
				case '&':
					if (DecodeCharacterReferences) {
						TokenizerState = HtmlTokenizerState.CharacterReferenceInRcData;
						return null;
					}

					goto default;
				case '<':
					TokenizerState = HtmlTokenizerState.RcDataLessThan;
					return EmitDataToken (DecodeCharacterReferences);
				default:
					data.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.RcData);

			return EmitDataToken (DecodeCharacterReferences);
		}

		HtmlToken ReadCharacterReferenceInRcData ()
		{
			return ReadCharacterReference (HtmlTokenizerState.RcData);
		}

		HtmlToken ReadRawText ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					break;
				}

				c = (char) nc;

				switch (c) {
				case '<':
					TokenizerState = HtmlTokenizerState.RawTextLessThan;
					return EmitDataToken (false);
				default:
					data.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.RawText);

			return EmitDataToken (false);
		}

		HtmlToken ReadScriptData ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					break;
				}

				c = (char) nc;

				switch (c) {
				case '<':
					TokenizerState = HtmlTokenizerState.ScriptDataLessThan;
					return EmitScriptDataToken ();
				default:
					data.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptData);

			return EmitScriptDataToken ();
		}

		HtmlToken ReadPlainText ()
		{
			int nc = Read ();

			while (nc != -1) {
				char c = (char) nc;

				data.Append (c == '\0' ? '\uFFFD' : c);
				nc = Read ();
			}

			TokenizerState = HtmlTokenizerState.EndOfFile;

			return EmitDataToken (false);
		}

		HtmlToken ReadTagOpen ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				var token = CreateDataToken ("<");
				return token;
			}

			c = (char) nc;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append ('<');
			data.Append (c);

			switch ((c = (char) nc)) {
			case '!': TokenizerState = HtmlTokenizerState.MarkupDeclarationOpen; break;
			case '?': TokenizerState = HtmlTokenizerState.BogusComment; break;
			case '/': TokenizerState = HtmlTokenizerState.EndTagOpen; break;
			default:
				if (IsAsciiLetter (c)) {
					TokenizerState = HtmlTokenizerState.TagName;
					isEndTag = false;
					name.Append (c);
				} else {
					TokenizerState = HtmlTokenizerState.Data;
				}
				break;
			}

			return null;
		}

		HtmlToken ReadEndTagOpen ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitDataToken (false);
			}

			c = (char) nc;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append (c);

			switch (c) {
			case '>': // parse error
				TokenizerState = HtmlTokenizerState.Data;
				data.Length = 0;
				break;
			default:
				if (IsAsciiLetter (c)) {
					TokenizerState = HtmlTokenizerState.TagName;
					isEndTag = true;
					name.Append (c);
				} else {
					TokenizerState = HtmlTokenizerState.BogusComment;
				}
				break;
			}

			return null;
		}

		HtmlToken ReadTagName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.BeforeAttributeName;
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					break;
				case '>':
					tag = CreateTagToken (name.ToString (), isEndTag);
					data.Length = 0;
					name.Length = 0;

					return EmitTagToken ();
				default:
					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.TagName);

			tag = CreateTagToken (name.ToString (), isEndTag);
			name.Length = 0;

			return null;
		}

		HtmlToken ReadRcDataLessThan ()
		{
			return ReadGenericRawTextLessThan (HtmlTokenizerState.RcData, HtmlTokenizerState.RcDataEndTagOpen);
		}

		HtmlToken ReadRcDataEndTagOpen ()
		{
			return ReadGenericRawTextEndTagOpen (DecodeCharacterReferences, HtmlTokenizerState.RcData, HtmlTokenizerState.RcDataEndTagName);
		}

		HtmlToken ReadRcDataEndTagName ()
		{
			return ReadGenericRawTextEndTagName (DecodeCharacterReferences, HtmlTokenizerState.RcData);
		}

		HtmlToken ReadRawTextLessThan ()
		{
			return ReadGenericRawTextLessThan (HtmlTokenizerState.RawText, HtmlTokenizerState.RawTextEndTagOpen);
		}

		HtmlToken ReadRawTextEndTagOpen ()
		{
			return ReadGenericRawTextEndTagOpen (false, HtmlTokenizerState.RawText, HtmlTokenizerState.RawTextEndTagName);
		}

		HtmlToken ReadRawTextEndTagName ()
		{
			return ReadGenericRawTextEndTagName (false, HtmlTokenizerState.RawText);
		}

		HtmlToken ReadScriptDataLessThan ()
		{
			int nc = Peek ();

			data.Append ('<');

			switch ((char) nc) {
			case '/':
				TokenizerState = HtmlTokenizerState.ScriptDataEndTagOpen;
				data.Append ('/');
				name.Length = 0;
				Read ();
				break;
			case '!':
				TokenizerState = HtmlTokenizerState.ScriptDataEscapeStart;
				data.Append ('!');
				Read ();
				break;
			default:
				TokenizerState = HtmlTokenizerState.ScriptData;
				break;
			}

			return null;
		}

		HtmlToken ReadScriptDataEndTagOpen ()
		{
			int nc = Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitScriptDataToken ();
			}

			c = (char) nc;

			if (c == 'S' || c == 's') {
				TokenizerState = HtmlTokenizerState.ScriptDataEndTagName;
				name.Append ('s');
				data.Append (c);
				Read ();
			} else {
				TokenizerState = HtmlTokenizerState.ScriptData;
			}

			return null;
		}

		HtmlToken ReadScriptDataEndTagName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitScriptDataToken ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					if (NameIs ("script")) {
						TokenizerState = HtmlTokenizerState.BeforeAttributeName;
						break;
					}

					goto default;
				case '/':
					if (NameIs ("script")) {
						TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
						break;
					}
					goto default;
				case '>':
					if (NameIs ("script")) {
						var token = CreateTagToken (name.ToString (), true);
						TokenizerState = HtmlTokenizerState.Data;
						data.Length = 0;
						name.Length = 0;
						return token;
					}
					goto default;
				default:
					if (!IsAsciiLetter (c)) {
						TokenizerState = HtmlTokenizerState.ScriptData;
						return null;
					}

					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataEndTagName);

			tag = CreateTagToken (name.ToString (), true);
			name.Length = 0;

			return null;
		}

		HtmlToken ReadScriptDataEscapeStart ()
		{
			int nc = Peek ();

			if (nc == '-') {
				TokenizerState = HtmlTokenizerState.ScriptDataEscapeStartDash;
				Read ();
			} else {
				TokenizerState = HtmlTokenizerState.ScriptData;
			}

			return null;
		}

		HtmlToken ReadScriptDataEscapeStartDash ()
		{
			int nc = Peek ();

			if (nc == '-') {
				TokenizerState = HtmlTokenizerState.ScriptDataEscapedDashDash;
				Read ();
			} else {
				TokenizerState = HtmlTokenizerState.ScriptData;
			}

			return null;
		}

		HtmlToken ReadScriptDataEscaped ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitScriptDataToken ();
				}

				c = (char) nc;

				switch (c) {
				case '-':
					TokenizerState = HtmlTokenizerState.ScriptDataEscapedDash;
					data.Append ('-');
					break;
				case '<':
					TokenizerState = HtmlTokenizerState.ScriptDataEscapedLessThan;
					break;
				default:
					data.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataEscaped);

			return null;
		}

		HtmlToken ReadScriptDataEscapedDash ()
		{
			int nc = Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitScriptDataToken ();
			}

			switch ((c = (char) nc)) {
			case '-':
				TokenizerState = HtmlTokenizerState.ScriptDataEscapedDashDash;
				data.Append ('-');
				break;
			case '<':
				TokenizerState = HtmlTokenizerState.ScriptDataEscapedLessThan;
				break;
			default:
				TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
				data.Append (c == '\0' ? '\uFFFD' : c);
				break;
			}

			return null;
		}

		HtmlToken ReadScriptDataEscapedDashDash ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitScriptDataToken ();
				}

				c = (char) nc;

				switch (c) {
				case '-':
					TokenizerState = HtmlTokenizerState.ScriptDataEscapedDash;
					data.Append ('-');
					break;
				case '<':
					TokenizerState = HtmlTokenizerState.ScriptDataEscapedLessThan;
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.ScriptData;
					data.Append ('>');
					break;
				default:
					TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
					data.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataEscaped);

			return null;
		}

		HtmlToken ReadScriptDataEscapedLessThan ()
		{
			int nc = Peek ();
			char c = (char) nc;

			if (c == '/') {
				TokenizerState = HtmlTokenizerState.ScriptDataEndTagOpen;
				name.Length = 0;
				Read ();
			} else if (IsAsciiLetter (c)) {
				TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
				data.Append ('<');
				data.Append (c);
				name.Append (c);
				Read ();
			} else {
				TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
				data.Append ('<');
			}

			return null;
		}

		HtmlToken ReadScriptDataEscapedEndTagOpen ()
		{
			int nc = Peek ();
			char c;

			data.Append ("</");

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitScriptDataToken ();
			}

			c = (char) nc;

			if (IsAsciiLetter (c)) {
				TokenizerState = HtmlTokenizerState.ScriptDataEscapedEndTagName;
				name.Append (c);
				Read ();
			} else {
				TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
			}

			return null;
		}

		HtmlToken ReadScriptDataEscapedEndTagName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitScriptDataToken ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					if (NameIs ("script")) {
						TokenizerState = HtmlTokenizerState.BeforeAttributeName;
						break;
					}

					goto default;
				case '/':
					if (NameIs ("script")) {
						TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
						break;
					}
					goto default;
				case '>':
					if (NameIs ("script")) {
						var token = CreateTagToken (name.ToString (), true);
						TokenizerState = HtmlTokenizerState.Data;
						data.Length = 0;
						name.Length = 0;
						return token;
					}
					goto default;
				default:
					if (!IsAsciiLetter (c)) {
						TokenizerState = HtmlTokenizerState.ScriptData;
						data.Append (c);
						return null;
					}

					name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataEscapedEndTagName);

			tag = CreateTagToken (name.ToString (), true);
			name.Length = 0;

			return null;
		}

		HtmlToken ReadScriptDataDoubleEscapeStart ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitScriptDataToken ();
				}

				c = (char) nc;

				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ': case '/': case '>':
					if (NameIs ("script"))
						TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
					else
						TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
					name.Length = 0;
					break;
				default:
					if (!IsAsciiLetter (c))
						TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
					else
						name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataDoubleEscapeStart);

			return null;
		}

		HtmlToken ReadScriptDataDoubleEscaped ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitScriptDataToken ();
				}

				c = (char) nc;

				switch (c) {
				case '-':
					TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscapedDash;
					data.Append ('-');
					break;
				case '<':
					TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscapedLessThan;
					break;
				default:
					data.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataEscaped);

			return null;
		}

		HtmlToken ReadScriptDataDoubleEscapedDash ()
		{
			int nc = Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitScriptDataToken ();
			}

			switch ((c = (char) nc)) {
			case '-':
				TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscapedDashDash;
				data.Append ('-');
				break;
			case '<':
				TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscapedLessThan;
				break;
			default:
				TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
				data.Append (c == '\0' ? '\uFFFD' : c);
				break;
			}

			return null;
		}

		HtmlToken ReadScriptDataDoubleEscapedDashDash ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitScriptDataToken ();
				}

				c = (char) nc;

				switch (c) {
				case '-':
					data.Append ('-');
					break;
				case '<':
					TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscapedLessThan;
					data.Append ('<');
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.ScriptData;
					data.Append ('>');
					break;
				default:
					TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
					data.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataEscaped);

			return null;
		}

		HtmlToken ReadScriptDataDoubleEscapedLessThan ()
		{
			int nc = Peek ();

			if (nc == '/') {
				TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscapeEnd;
				data.Append ('/');
				Read ();
			} else {
				TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
			}

			return null;
		}

		HtmlToken ReadScriptDataDoubleEscapeEnd ()
		{
			do {
				int nc = Peek ();
				char c = (char) nc;

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ': case '/': case '>':
					if (NameIs ("script"))
						TokenizerState = HtmlTokenizerState.ScriptDataEscaped;
					else
						TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
					data.Append (c);
					Read ();
					break;
				default:
					if (!IsAsciiLetter (c)) {
						TokenizerState = HtmlTokenizerState.ScriptDataDoubleEscaped;
					} else {
						name.Append (c);
						data.Append (c);
						Read ();
					}
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.ScriptDataDoubleEscapeEnd);

			return null;
		}

		HtmlToken ReadBeforeAttributeName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					tag = null;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					return null;
				case '>':
					return EmitTagToken ();
				case '"': case '\'': case '<': case '=':
					// parse error
					goto default;
				default:
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append (c == '\0' ? '\uFFFD' : c);
					return null;
				}
			} while (true);
		}

		HtmlToken ReadAttributeName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;
					tag = null;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.AfterAttributeName;
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					break;
				case '=':
					TokenizerState = HtmlTokenizerState.BeforeAttributeValue;
					break;
				case '>':
					EmitTagAttribute ();

					return EmitTagToken ();
				default:
					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.AttributeName);

			EmitTagAttribute ();

			return null;
		}

		HtmlToken ReadAfterAttributeName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					tag = null;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					return null;
				case '=':
					TokenizerState = HtmlTokenizerState.BeforeAttributeValue;
					return null;
				case '>':
					return EmitTagToken ();
				case '"': case '\'': case '<':
					// parse error
					goto default;
				default:
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append (c == '\0' ? '\uFFFD' : c);
					return null;
				}
			} while (true);
		}

		HtmlToken ReadBeforeAttributeValue ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					tag = null;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '"': case '\'':
					TokenizerState = HtmlTokenizerState.AttributeValueQuoted;
					quote = c;
					return null;
				case '&':
					TokenizerState = HtmlTokenizerState.AttributeValueUnquoted;
					return null;
				case '/':
					TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
					return null;
				case '>':
					return EmitTagToken ();
				case '<': case '=': case '`':
					// parse error
					goto default;
				default:
					TokenizerState = HtmlTokenizerState.AttributeName;
					name.Append (c == '\0' ? '\uFFFD' : c);
					return null;
				}
			} while (true);
		}

		HtmlToken ReadAttributeValueQuoted ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '&':
					TokenizerState = HtmlTokenizerState.CharacterReferenceInAttributeValue;
					return null;
				default:
					if (c == quote) {
						TokenizerState = HtmlTokenizerState.AfterAttributeValueQuoted;
						break;
					}

					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.AttributeValueQuoted);

			attribute.Value = name.ToString ();
			name.Length = 0;

			return null;
		}

		HtmlToken ReadAttributeValueUnquoted ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					name.Length = 0;

					return EmitDataToken (false);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.BeforeAttributeName;
					break;
				case '&':
					TokenizerState = HtmlTokenizerState.CharacterReferenceInAttributeValue;
					return null;
				case '>':
					return EmitTagToken ();
				case '\'': case '<': case '=': case '`':
					// parse error
					goto default;
				default:
					if (c == quote) {
						TokenizerState = HtmlTokenizerState.AfterAttributeValueQuoted;
						break;
					}

					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.AttributeValueUnquoted);

			attribute.Value = name.ToString ();
			name.Length = 0;

			return null;
		}

		HtmlToken ReadCharacterReferenceInAttributeValue ()
		{
			char additionalAllowedCharacter = quote == '\0' ? '>' : quote;
			int nc = Peek ();
			bool consume;
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				data.Append ('&');
				name.Length = 0;

				return EmitDataToken (false);
			}

			c = (char) nc;

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ': case '<': case '&':
				// no character is consumed, emit '&'
				data.Append ('&');
				name.Append ('&');
				consume = false;
				break;
			default:
				if (c == additionalAllowedCharacter) {
					// this is not a character reference, nothing is consumed
					data.Append ('&');
					name.Append ('&');
					consume = false;
					break;
				}

				entity.Push ('&');

				while (entity.Push (c)) {
					Read ();

					if ((nc = Peek ()) == -1) {
						TokenizerState = HtmlTokenizerState.EndOfFile;
						data.Append (entity.GetPushedInput ());
						entity.Reset ();

						return EmitDataToken (false);
					}

					c = (char) nc;
				}

				var pushed = entity.GetPushedInput ();
				string value;

				if (c == '=' || IsAlphaNumeric (c))
					value = pushed;
				else
					value = entity.GetValue ();

				data.Append (pushed);
				name.Append (value);
				consume = c == ';';
				entity.Reset ();
				break;
			}

			if (quote == '\0')
				TokenizerState = HtmlTokenizerState.AttributeValueUnquoted;
			else
				TokenizerState = HtmlTokenizerState.AttributeValueQuoted;

			if (consume)
				Read ();

			return null;
		}

		HtmlToken ReadAfterAttributeValueQuoted ()
		{
			HtmlToken token = null;
			int nc = Peek ();
			bool consume;
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitDataToken (false);
			}

			c = (char) nc;

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ':
				TokenizerState = HtmlTokenizerState.BeforeAttributeName;
				consume = true;
				break;
			case '/':
				TokenizerState = HtmlTokenizerState.SelfClosingStartTag;
				consume = true;
				break;
			case '>':
				token = EmitTagToken ();
				consume = true;
				break;
			default:
				TokenizerState = HtmlTokenizerState.BeforeAttributeName;
				consume = false;
				break;
			}

			if (consume)
				Read ();

			return token;
		}

		HtmlToken ReadSelfClosingStartTag ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitDataToken (false);
			}

			c = (char) nc;

			if (c == '>') {
				tag.IsEmptyElement = true;

				return EmitTagToken ();
			}

			// parse error
			TokenizerState = HtmlTokenizerState.BeforeAttributeName;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append (c);

			return null;
		}

		HtmlToken ReadBogusComment ()
		{
			int nc;
			char c;

			if (data.Length > 0) {
				c = data[data.Length - 1];
				data.Length = 1;
				data[0] = c;
			}

			do {
				if ((nc = Read ()) == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					break;
				}

				if ((c = (char) nc) == '>')
					break;

				data.Append (c == '\0' ? '\uFFFD' : c);
			} while (true);

			return EmitCommentToken (data);
		}

		HtmlToken ReadMarkupDeclarationOpen ()
		{
			int count = 0, nc;
			char c = '\0';

			while (count < 2) {
				if ((nc = Peek ()) == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitDataToken (false);
				}

				if ((c = (char) nc) != '-')
					break;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);
				Read ();
				count++;
			}

			if (count == 2) {
				TokenizerState = HtmlTokenizerState.CommentStart;
				name.Length = 0;
				return null;
			}

			if (count == 1) {
				// parse error
				TokenizerState = HtmlTokenizerState.BogusComment;
				return null;
			}

			if (c == 'D' || c == 'd') {
				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);
				name.Append (c);
				count = 1;
				Read ();

				while (count < 7) {
					if ((nc = Read ()) == -1) {
						TokenizerState = HtmlTokenizerState.EndOfFile;
						return EmitDataToken (false);
					}

					if (ToLower ((c = (char) nc)) != DocType[count])
						break;

					// Note: we save the data in case we hit a parse error and have to emit a data token
					data.Append (c);
					name.Append (c);
					count++;
				}

				if (count == 7) {
					doctype = CreateDocTypeToken (name.ToString ());
					TokenizerState = HtmlTokenizerState.DocType;
					name.Length = 0;
					return null;
				}

				name.Length = 0;
			} else if (c == '[') {
				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);
				count = 1;
				Read ();

				while (count < 7) {
					if ((nc = Read ()) == -1) {
						TokenizerState = HtmlTokenizerState.EndOfFile;
						return EmitDataToken (false);
					}

					if ((c = (char) nc) != CData[count])
						break;

					// Note: we save the data in case we hit a parse error and have to emit a data token
					data.Append (c);
					count++;
				}

				if (count == 7) {
					TokenizerState = HtmlTokenizerState.CDataSection;
					return null;
				}
			}

			// parse error
			TokenizerState = HtmlTokenizerState.BogusComment;

			return null;
		}

		HtmlToken ReadCommentStart ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.Data;

				return EmitCommentToken (string.Empty);
			}

			c = (char) nc;

			data.Append (c);

			switch (c) {
			case '-':
				TokenizerState = HtmlTokenizerState.CommentStartDash;
				break;
			case '>': // parse error
				TokenizerState = HtmlTokenizerState.Data;
				return EmitCommentToken (string.Empty);
			default: // parse error
				TokenizerState = HtmlTokenizerState.Comment;
				name.Append (c == '\0' ? '\uFFFD' : c);
				break;
			}

			return null;
		}

		HtmlToken ReadCommentStartDash ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.Data;
				return EmitCommentToken (name);
			}

			c = (char) nc;

			data.Append (c);

			switch (c) {
			case '-':
				TokenizerState = HtmlTokenizerState.CommentEnd;
				break;
			case '>': // parse error
				TokenizerState = HtmlTokenizerState.Data;
				return EmitCommentToken (name);
			default: // parse error
				TokenizerState = HtmlTokenizerState.Comment;
				name.Append ('-');
				name.Append (c == '\0' ? '\uFFFD' : c);
				break;
			}

			return null;
		}

		HtmlToken ReadComment ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitCommentToken (name);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '-':
					TokenizerState = HtmlTokenizerState.CommentEndDash;
					return null;
				default:
					name.Append (c == '\0' ? '\uFFFD' : c);
					break;
				}
			} while (true);
		}

		// FIXME: this is exactly the same as ReadCommentStartDash
		HtmlToken ReadCommentEndDash ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.Data;
				return EmitCommentToken (name);
			}

			c = (char) nc;

			data.Append (c);

			switch (c) {
			case '-':
				TokenizerState = HtmlTokenizerState.CommentEnd;
				break;
			case '>': // parse error
				TokenizerState = HtmlTokenizerState.Data;
				return EmitCommentToken (name);
			default: // parse error
				TokenizerState = HtmlTokenizerState.Comment;
				name.Append ('-');
				name.Append (c == '\0' ? '\uFFFD' : c);
				break;
			}

			return null;
		}

		HtmlToken ReadCommentEnd ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					return EmitCommentToken (name);
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					return EmitCommentToken (name);
				case '!': // parse error
					TokenizerState = HtmlTokenizerState.CommentEndBang;
					return null;
				case '-':
					name.Append ('-');
					break;
				default:
					TokenizerState = HtmlTokenizerState.Comment;
					name.Append (c == '\0' ? '\uFFFD' : c);
					return null;
				}
			} while (true);
		}

		HtmlToken ReadCommentEndBang ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				return EmitCommentToken (name);
			}

			c = (char) nc;

			data.Append (c);

			switch (c) {
			case '-':
				TokenizerState = HtmlTokenizerState.CommentEndDash;
				name.Append ("--!");
				break;
			case '>':
				TokenizerState = HtmlTokenizerState.Data;
				return EmitCommentToken (name);
			default: // parse error
				TokenizerState = HtmlTokenizerState.Comment;
				name.Append ("--!");
				name.Append (c == '\0' ? '\uFFFD' : c);
				break;
			}

			return null;
		}

		HtmlToken ReadDocType ()
		{
			int nc = Peek ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				doctype.ForceQuirksMode = true;
				name.Length = 0;

				return EmitDocType ();
			}

			TokenizerState = HtmlTokenizerState.BeforeDocTypeName;
			c = (char) nc;

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ':
				data.Append (c);
				Read ();
				break;
			}

			return null;
		}

		HtmlToken ReadBeforeDocTypeName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				case '\0':
					TokenizerState = HtmlTokenizerState.DocTypeName;
					name.Append ('\uFFFD');
					return null;
				default:
					TokenizerState = HtmlTokenizerState.DocTypeName;
					name.Append (c);
					return null;
				}
			} while (true);
		}

		HtmlToken ReadDocTypeName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.Name = name.ToString ();
					doctype.ForceQuirksMode = true;
					name.Length = 0;

					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					TokenizerState = HtmlTokenizerState.AfterDocTypeName;
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					doctype.Name = name.ToString ();
					name.Length = 0;

					return EmitDocType ();
				case '\0':
					name.Append ('\uFFFD');
					break;
				default:
					name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.DocTypeName);

			doctype.Name = name.ToString ();
			name.Length = 0;

			return null;
		}

		HtmlToken ReadAfterDocTypeName ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					return EmitDocType ();
				default:
					name.Append (c);
					if (name.Length < 6)
						break;

					if (NameIs ("public")) {
						TokenizerState = HtmlTokenizerState.AfterDocTypePublicKeyword;
						doctype.PublicKeyword = name.ToString ();
					} else if (NameIs ("system")) {
						TokenizerState = HtmlTokenizerState.AfterDocTypeSystemKeyword;
						doctype.SystemKeyword = name.ToString ();
					} else {
						TokenizerState = HtmlTokenizerState.BogusDocType;
					}

					name.Length = 0;
					return null;
				}
			} while (true);
		}

		HtmlToken ReadAfterDocTypePublicKeyword ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				doctype.ForceQuirksMode = true;
				return EmitDocType ();
			}

			c = (char) nc;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append (c);

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ':
				TokenizerState = HtmlTokenizerState.BeforeDocTypePublicIdentifier;
				break;
			case '"': case '\'': // parse error
				TokenizerState = HtmlTokenizerState.DocTypePublicIdentifierQuoted;
				doctype.PublicIdentifier = string.Empty;
				quote = c;
				break;
			case '>': // parse error
				TokenizerState = HtmlTokenizerState.Data;
				doctype.ForceQuirksMode = true;
				return EmitDocType ();
			default: // parse error
				TokenizerState = HtmlTokenizerState.BogusDocType;
				doctype.ForceQuirksMode = true;
				break;
			}

			return null;
		}

		HtmlToken ReadBeforeDocTypePublicIdentifier ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '"': case '\'':
					TokenizerState = HtmlTokenizerState.DocTypePublicIdentifierQuoted;
					doctype.PublicIdentifier = string.Empty;
					quote = c;
					return null;
				case '>': // parse error
					TokenizerState = HtmlTokenizerState.Data;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				default: // parse error
					TokenizerState = HtmlTokenizerState.BogusDocType;
					doctype.ForceQuirksMode = true;
					return null;
				}
			} while (true);
		}

		HtmlToken ReadDocTypePublicIdentifierQuoted ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.PublicIdentifier = name.ToString ();
					doctype.ForceQuirksMode = true;
					name.Length = 0;

					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\0': // parse error
					name.Append ('\uFFFD');
					break;
				case '>': // parse error
					TokenizerState = HtmlTokenizerState.Data;
					doctype.PublicIdentifier = name.ToString ();
					doctype.ForceQuirksMode = true;
					name.Length = 0;

					return EmitDocType ();
				default:
					if (c == quote) {
						TokenizerState = HtmlTokenizerState.AfterDocTypePublicIdentifier;
						break;
					}

					name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.DocTypePublicIdentifierQuoted);

			doctype.PublicIdentifier = name.ToString ();
			name.Length = 0;

			return null;
		}

		HtmlToken ReadAfterDocTypePublicIdentifier ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				doctype.ForceQuirksMode = true;
				return EmitDocType ();
			}

			c = (char) nc;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append (c);

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ':
				TokenizerState = HtmlTokenizerState.BetweenDocTypePublicAndSystemIdentifiers;
				break;
			case '>':
				TokenizerState = HtmlTokenizerState.Data;
				return EmitDocType ();
			case '"': case '\'': // parse error
				TokenizerState = HtmlTokenizerState.DocTypeSystemIdentifierQuoted;
				doctype.SystemIdentifier = string.Empty;
				quote = c;
				break;
			default: // parse error
				TokenizerState = HtmlTokenizerState.BogusDocType;
				doctype.ForceQuirksMode = true;
				break;
			}

			return null;
		}

		HtmlToken ReadBetweenDocTypePublicAndSystemIdentifiers ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					return EmitDocType ();
				case '"': case '\'':
					TokenizerState = HtmlTokenizerState.DocTypeSystemIdentifierQuoted;
					doctype.SystemIdentifier = string.Empty;
					quote = c;
					return null;
				default: // parse error
					TokenizerState = HtmlTokenizerState.BogusDocType;
					doctype.ForceQuirksMode = true;
					return null;
				}
			} while (true);
		}

		HtmlToken ReadAfterDocTypeSystemKeyword ()
		{
			int nc = Read ();
			char c;

			if (nc == -1) {
				TokenizerState = HtmlTokenizerState.EndOfFile;
				doctype.ForceQuirksMode = true;
				return EmitDocType ();
			}

			c = (char) nc;

			// Note: we save the data in case we hit a parse error and have to emit a data token
			data.Append (c);

			switch (c) {
			case '\t': case '\r': case '\n': case '\f': case ' ':
				TokenizerState = HtmlTokenizerState.BeforeDocTypeSystemIdentifier;
				break;
			case '"': case '\'': // parse error
				TokenizerState = HtmlTokenizerState.DocTypeSystemIdentifierQuoted;
				doctype.SystemIdentifier = string.Empty;
				quote = c;
				break;
			case '>': // parse error
				TokenizerState = HtmlTokenizerState.Data;
				doctype.ForceQuirksMode = true;
				return EmitDocType ();
			default: // parse error
				TokenizerState = HtmlTokenizerState.BogusDocType;
				doctype.ForceQuirksMode = true;
				break;
			}

			return null;
		}

		HtmlToken ReadBeforeDocTypeSystemIdentifier ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case '\r': case '\n': case '\f': case ' ':
					break;
				case '"': case '\'':
					TokenizerState = HtmlTokenizerState.DocTypeSystemIdentifierQuoted;
					doctype.SystemIdentifier = string.Empty;
					quote = c;
					return null;
				case '>': // parse error
					TokenizerState = HtmlTokenizerState.Data;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				default: // parse error
					TokenizerState = HtmlTokenizerState.BogusDocType;
					doctype.ForceQuirksMode = true;
					return null;
				}
			} while (true);
		}

		HtmlToken ReadDocTypeSystemIdentifierQuoted ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.SystemIdentifier = name.ToString ();
					doctype.ForceQuirksMode = true;
					name.Length = 0;

					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\0': // parse error
					name.Append ('\uFFFD');
					break;
				case '>': // parse error
					TokenizerState = HtmlTokenizerState.Data;
					doctype.SystemIdentifier = name.ToString ();
					doctype.ForceQuirksMode = true;
					name.Length = 0;

					return EmitDocType ();
				default:
					if (c == quote) {
						TokenizerState = HtmlTokenizerState.AfterDocTypeSystemIdentifier;
						break;
					}

					name.Append (c);
					break;
				}
			} while (TokenizerState == HtmlTokenizerState.DocTypeSystemIdentifierQuoted);

			doctype.SystemIdentifier = name.ToString ();
			name.Length = 0;

			return null;
		}

		HtmlToken ReadAfterDocTypeSystemIdentifier ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				switch (c) {
				case '\t': case'\r': case '\n': case '\f': case ' ':
					break;
				case '>':
					TokenizerState = HtmlTokenizerState.Data;
					return EmitDocType ();
				default: // parse error
					TokenizerState = HtmlTokenizerState.BogusDocType;
					return null;
				}
			} while (true);
		}

		HtmlToken ReadBogusDocType ()
		{
			do {
				int nc = Read ();
				char c;

				if (nc == -1) {
					TokenizerState = HtmlTokenizerState.EndOfFile;
					doctype.ForceQuirksMode = true;
					return EmitDocType ();
				}

				c = (char) nc;

				// Note: we save the data in case we hit a parse error and have to emit a data token
				data.Append (c);

				if (c == '>') {
					TokenizerState = HtmlTokenizerState.Data;
					return EmitDocType ();
				}
			} while (true);
		}

		HtmlToken ReadCDataSection ()
		{
			int nc = Read ();

			while (nc != -1) {
				char c = (char) nc;

				if (cdataIndex >= 3) {
					data.Append (cdata[0]);
					cdata[0] = cdata[1];
					cdata[1] = cdata[2];
					cdata[2] = c;

					if (cdata[0] == ']' && cdata[1] == ']' && cdata[2] == '>') {
						TokenizerState = HtmlTokenizerState.Data;
						cdataIndex = 0;

						return EmitCDataToken ();
					}
				} else {
					cdata[cdataIndex++] = c;
				}

				nc = Read ();
			}

			TokenizerState = HtmlTokenizerState.EndOfFile;

			for (int i = 0; i < cdataIndex; i++)
				data.Append (cdata[i]);

			cdataIndex = 0;

			return EmitCDataToken ();
		}

		public bool ReadNextToken (out HtmlToken token)
		{
			do {
				switch (TokenizerState) {
				case HtmlTokenizerState.Data:
					token = ReadData ();
					break;
				case HtmlTokenizerState.CharacterReferenceInData:
					token = ReadCharacterReferenceInData ();
					break;
				case HtmlTokenizerState.RcData:
					token = ReadRcData ();
					break;
				case HtmlTokenizerState.CharacterReferenceInRcData:
					token = ReadCharacterReferenceInRcData ();
					break;
				case HtmlTokenizerState.RawText:
					token = ReadRawText ();
					break;
				case HtmlTokenizerState.ScriptData:
					token = ReadScriptData ();
					break;
				case HtmlTokenizerState.PlainText:
					token = ReadPlainText ();
					break;
				case HtmlTokenizerState.TagOpen:
					token = ReadTagOpen ();
					break;
				case HtmlTokenizerState.EndTagOpen:
					token = ReadEndTagOpen ();
					break;
				case HtmlTokenizerState.TagName:
					token = ReadTagName ();
					break;
				case HtmlTokenizerState.RcDataLessThan:
					token = ReadRcDataLessThan ();
					break;
				case HtmlTokenizerState.RcDataEndTagOpen:
					token = ReadRcDataEndTagOpen ();
					break;
				case HtmlTokenizerState.RcDataEndTagName:
					token = ReadRcDataEndTagName ();
					break;
				case HtmlTokenizerState.RawTextLessThan:
					token = ReadRawTextLessThan ();
					break;
				case HtmlTokenizerState.RawTextEndTagOpen:
					token = ReadRawTextEndTagOpen ();
					break;
				case HtmlTokenizerState.RawTextEndTagName:
					token = ReadRawTextEndTagName ();
					break;
				case HtmlTokenizerState.ScriptDataLessThan:
					token = ReadScriptDataLessThan ();
					break;
				case HtmlTokenizerState.ScriptDataEndTagOpen:
					token = ReadScriptDataEndTagOpen ();
					break;
				case HtmlTokenizerState.ScriptDataEndTagName:
					token = ReadScriptDataEndTagName ();
					break;
				case HtmlTokenizerState.ScriptDataEscapeStart:
					token = ReadScriptDataEscapeStart ();
					break;
				case HtmlTokenizerState.ScriptDataEscapeStartDash:
					token = ReadScriptDataEscapeStartDash ();
					break;
				case HtmlTokenizerState.ScriptDataEscaped:
					token = ReadScriptDataEscaped ();
					break;
				case HtmlTokenizerState.ScriptDataEscapedDash:
					token = ReadScriptDataEscapedDash ();
					break;
				case HtmlTokenizerState.ScriptDataEscapedDashDash:
					token = ReadScriptDataEscapedDashDash ();
					break;
				case HtmlTokenizerState.ScriptDataEscapedLessThan:
					token = ReadScriptDataEscapedLessThan ();
					break;
				case HtmlTokenizerState.ScriptDataEscapedEndTagOpen:
					token = ReadScriptDataEscapedEndTagOpen ();
					break;
				case HtmlTokenizerState.ScriptDataEscapedEndTagName:
					token = ReadScriptDataEscapedEndTagName ();
					break;
				case HtmlTokenizerState.ScriptDataDoubleEscapeStart:
					token = ReadScriptDataDoubleEscapeStart ();
					break;
				case HtmlTokenizerState.ScriptDataDoubleEscaped:
					token = ReadScriptDataDoubleEscaped ();
					break;
				case HtmlTokenizerState.ScriptDataDoubleEscapedDash:
					token = ReadScriptDataDoubleEscapedDash ();
					break;
				case HtmlTokenizerState.ScriptDataDoubleEscapedDashDash:
					token = ReadScriptDataDoubleEscapedDashDash ();
					break;
				case HtmlTokenizerState.ScriptDataDoubleEscapedLessThan:
					token = ReadScriptDataDoubleEscapedLessThan ();
					break;
				case HtmlTokenizerState.ScriptDataDoubleEscapeEnd:
					token = ReadScriptDataDoubleEscapeEnd ();
					break;
				case HtmlTokenizerState.BeforeAttributeName:
					token = ReadBeforeAttributeName ();
					break;
				case HtmlTokenizerState.AttributeName:
					token = ReadAttributeName ();
					break;
				case HtmlTokenizerState.AfterAttributeName:
					token = ReadAfterAttributeName ();
					break;
				case HtmlTokenizerState.BeforeAttributeValue:
					token = ReadBeforeAttributeValue ();
					break;
				case HtmlTokenizerState.AttributeValueQuoted:
					token = ReadAttributeValueQuoted ();
					break;
				case HtmlTokenizerState.AttributeValueUnquoted:
					token = ReadAttributeValueUnquoted ();
					break;
				case HtmlTokenizerState.CharacterReferenceInAttributeValue:
					token = ReadCharacterReferenceInAttributeValue ();
					break;
				case HtmlTokenizerState.AfterAttributeValueQuoted:
					token = ReadAfterAttributeValueQuoted ();
					break;
				case HtmlTokenizerState.SelfClosingStartTag:
					token = ReadSelfClosingStartTag ();
					break;
				case HtmlTokenizerState.BogusComment:
					token = ReadBogusComment ();
					break;
				case HtmlTokenizerState.MarkupDeclarationOpen:
					token = ReadMarkupDeclarationOpen ();
					break;
				case HtmlTokenizerState.CommentStart:
					token = ReadCommentStart ();
					break;
				case HtmlTokenizerState.CommentStartDash:
					token = ReadCommentStartDash ();
					break;
				case HtmlTokenizerState.Comment:
					token = ReadComment ();
					break;
				case HtmlTokenizerState.CommentEndDash:
					token = ReadCommentEndDash ();
					break;
				case HtmlTokenizerState.CommentEnd:
					token = ReadCommentEnd ();
					break;
				case HtmlTokenizerState.CommentEndBang:
					token = ReadCommentEndBang ();
					break;
				case HtmlTokenizerState.DocType:
					token = ReadDocType ();
					break;
				case HtmlTokenizerState.BeforeDocTypeName:
					token = ReadBeforeDocTypeName ();
					break;
				case HtmlTokenizerState.DocTypeName:
					token = ReadDocTypeName ();
					break;
				case HtmlTokenizerState.AfterDocTypeName:
					token = ReadAfterDocTypeName ();
					break;
				case HtmlTokenizerState.AfterDocTypePublicKeyword:
					token = ReadAfterDocTypePublicKeyword ();
					break;
				case HtmlTokenizerState.BeforeDocTypePublicIdentifier:
					token = ReadBeforeDocTypePublicIdentifier ();
					break;
				case HtmlTokenizerState.DocTypePublicIdentifierQuoted:
					token = ReadDocTypePublicIdentifierQuoted ();
					break;
				case HtmlTokenizerState.AfterDocTypePublicIdentifier:
					token = ReadAfterDocTypePublicIdentifier ();
					break;
				case HtmlTokenizerState.BetweenDocTypePublicAndSystemIdentifiers:
					token = ReadBetweenDocTypePublicAndSystemIdentifiers ();
					break;
				case HtmlTokenizerState.AfterDocTypeSystemKeyword:
					token = ReadAfterDocTypeSystemKeyword ();
					break;
				case HtmlTokenizerState.BeforeDocTypeSystemIdentifier:
					token = ReadBeforeDocTypeSystemIdentifier ();
					break;
				case HtmlTokenizerState.DocTypeSystemIdentifierQuoted:
					token = ReadDocTypeSystemIdentifierQuoted ();
					break;
				case HtmlTokenizerState.AfterDocTypeSystemIdentifier:
					token = ReadAfterDocTypeSystemIdentifier ();
					break;
				case HtmlTokenizerState.BogusDocType:
					token = ReadBogusDocType ();
					break;
				case HtmlTokenizerState.CDataSection:
					token = ReadCDataSection ();
					break;
				case HtmlTokenizerState.EndOfFile:
				default:
					token = null;
					return false;
				}
			} while (token == null);

			return true;
		}
	}
}