//
// Parameter.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2012 Jeffrey Stedfast
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

using System;
using System.Text;

namespace MimeKit {
	public sealed class Parameter
	{
		string text;

		public Parameter (string name, string value)
		{
			if (name == null)
				throw new ArgumentNullException ("name");

			if (name.Length == 0)
				throw new ArgumentException ("Parameter names are not allowed to be empty.", "name");

			for (int i = 0; i < name.Length; i++) {
				if (name[i] > 127 || !IsAttr ((byte) name[i]))
					throw new ArgumentException ("Illegal characters in parameter name.", "name");
			}

			if (value == null)
				throw new ArgumentNullException ("value");

			Name = name;
			Value = value;
		}

		static bool IsAttr (byte c)
		{
			return c.IsAttr ();
		}

		public string Name {
			get; private set;
		}

		public string Value {
			get { return text; }
			set {
				if (value == null)
					throw new ArgumentNullException ("value");

				if (text == value)
					return;

				text = value;
				OnChanged ();
			}
		}

		enum EncodeMethod {
			None,
			Quote,
			Rfc2184
		}

		static EncodeMethod GetEncodeMethod (string name, string value, out string quoted)
		{
			quoted = null;

			if (name.Length + 1 + value.Length >= Rfc2047.MaxLineLength)
				return EncodeMethod.Rfc2184;

			for (int i = 0; i < value.Length; i++) {
				if (value[i] > 127 || ((byte) value[i]).IsCtrl ())
					return EncodeMethod.Rfc2184;

				if (!((byte) value[i]).IsAttr ()) {
					quoted = Rfc2047.Quote (value);

					if (name.Length + 1 + quoted.Length >= Rfc2047.MaxLineLength)
						return EncodeMethod.Rfc2184;

					return EncodeMethod.Quote;
				}
			}

			return EncodeMethod.None;
		}

		static EncodeMethod GetEncodeMethod (char[] value, int startIndex, int length)
		{
			for (int i = startIndex; i < length; i++) {
				if (((byte) value[i]) > 127 || ((byte) value[i]).IsCtrl ())
					return EncodeMethod.Rfc2184;

				if (!((byte) value[i]).IsAttr ())
					return EncodeMethod.Quote;
			}

			return EncodeMethod.None;
		}

		static EncodeMethod GetEncodeMethod (byte[] value, int length)
		{
			for (int i = 0; i < length; i++) {
				if (value[i] > 127 || value[i].IsCtrl ())
					return EncodeMethod.Rfc2184;

				if (!value[i].IsAttr ())
					return EncodeMethod.Quote;
			}

			return EncodeMethod.None;
		}

		static bool IsCtrl (char c)
		{
			return ((byte) c).IsCtrl ();
		}

		static Encoding GetBestEncoding (string value, Encoding defaultEncoding)
		{
			int encoding = 0; // us-ascii

			for (int i = 0; i < value.Length; i++) {
				if (value[i] < 128) {
					if (IsCtrl (value[i]))
						encoding = Math.Max (encoding, 1);
				} else if (value[i] < 256) {
					encoding = Math.Max (encoding, 1);
				} else {
					encoding = 2;
				}
			}

			switch (encoding) {
			case 0: return CharsetUtils.GetEncoding ("us-ascii");
			case 1: return CharsetUtils.GetEncoding ("iso-8859-1");
			default: return defaultEncoding;
			}
		}

		static bool GetNextValue (string charset, Encoder encoder, HexEncoder hex, char[] chars, ref int index, int maxLength, out string value)
		{
			int length = chars.Length - index;

			if (length < maxLength) {
				switch (GetEncodeMethod (chars, index, length)) {
				case EncodeMethod.Quote:
					value = Rfc2047.Quote (new string (chars, index, length));
					index += length;
					return false;
				case EncodeMethod.None:
					value = new string (chars, index, length);
					index += length;
					return false;
				default:
					break;
				}
			}

			byte[] bytes = new byte[Math.Max (maxLength, 6)];
			byte[] encoded = new byte[bytes.Length];
			int count, n;

			do {
				if ((count = encoder.GetByteCount (chars, index, length, true)) > maxLength && length > 1) {
					length -= Math.Max ((count - maxLength) / 4, 1);
					continue;
				}

				if (bytes.Length < count)
					Array.Resize<byte> (ref bytes, count);

				count = encoder.GetBytes (chars, index, length, bytes, 0, true);

				switch (GetEncodeMethod (bytes, count)) {
				case EncodeMethod.Quote:
					value = Rfc2047.Quote (Encoding.ASCII.GetString (bytes, 0, count));
					index += length;
					return false;
				case EncodeMethod.None:
					value = Encoding.ASCII.GetString (bytes, 0, count);
					index += length;
					return false;
				default:
					n = hex.EstimateOutputLength (count);
					if (encoded.Length < n)
						Array.Resize<byte> (ref encoded, n);

					n = hex.Encode (bytes, 0, count, encoded);
					if (n > 3 && (charset.Length + 2 + n) > maxLength) {
						int x = 0;

						for (int i = n - 1; i >= 0 && (n - x) > maxLength; i--) {
							if (encoded[i] == (byte) '%')
								x--;
							else
								x++;
						}

						length -= Math.Max (x / 4, 1);
						break;
					}

					value = charset + "''" + Encoding.ASCII.GetString (encoded, 0, n);
					index += length;
					return true;
				}
			} while (true);
		}

		internal void Encode (StringBuilder sb, ref int lineLength, Encoding encoding)
		{
			if (sb == null)
				throw new ArgumentNullException ("sb");

			if (lineLength < 0)
				throw new ArgumentOutOfRangeException ("lineLength");

			if (encoding == null)
				throw new ArgumentNullException ("encoding");

			string quoted;

			var method = GetEncodeMethod (Name, Value, out quoted);
			if (method == EncodeMethod.None)
				quoted = Value;

			if (method != EncodeMethod.Rfc2184) {
				if (lineLength + 2 + Name.Length + 1 + quoted.Length >= Rfc2047.MaxLineLength) {
					sb.Append (";\n\t");
					lineLength = 1;
				} else {
					sb.Append ("; ");
					lineLength += 2;
				}

				lineLength += Name.Length + 1 + quoted.Length;
				sb.Append (Name);
				sb.Append ('=');
				sb.Append (quoted);
				return;
			}

			int maxLength = Rfc2047.MaxLineLength - (Name.Length + 6);
			var bestEncoding = GetBestEncoding (Value, encoding);
			var charset = CharsetUtils.GetMimeCharset (bestEncoding);
			var encoder = bestEncoding.GetEncoder ();
			var chars = Value.ToCharArray ();
			var hex = new HexEncoder ();
			int index = 0, i = 0;
			string value, id;
			bool encoded;
			int length;

			do {
				encoded = GetNextValue (charset, encoder, hex, chars, ref index, maxLength, out value);
				length = Name.Length + (encoded ? 1 : 0) + 1 + value.Length;

				if (i == 0 && index == chars.Length) {
					if (lineLength + 2 + length >= Rfc2047.MaxLineLength) {
						sb.Append (";\n\t");
						lineLength = 1;
					} else {
						sb.Append ("; ");
						lineLength += 2;
					}

					sb.Append (Name);
					if (encoded)
						sb.Append ('*');
					sb.Append ('=');
					sb.Append (value);
					lineLength += length;
					return;
				}

				sb.Append (";\n\t");
				lineLength = 1;

				id = i.ToString ();
				length += id.Length + 1;

				sb.Append (Name);
				sb.Append ('*');
				sb.Append (id);
				if (encoded)
					sb.Append ('*');
				sb.Append ('=');
				sb.Append (value);
				lineLength += length;
			} while (index < chars.Length);
		}

		public override string ToString ()
		{
			return Name + "=" + Rfc2047.Quote (Value);
		}

		public event EventHandler Changed;

		void OnChanged ()
		{
			if (Changed != null)
				Changed (this, EventArgs.Empty);
		}
	}
}
