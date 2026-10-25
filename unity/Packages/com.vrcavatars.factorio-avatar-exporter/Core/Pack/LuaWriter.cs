using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace FactorioAvatarExporter.Core
{
    /// <summary>
    /// Writes data as a Lua table literal with exactly the layout of <c>dumps()</c> in
    /// tools/lua_table.py, so packs read the same as the Python tools write them.
    /// </summary>
    public static class LuaWriter
    {
        private const int Indent = 2;
        private const int MaxInlineLength = 80;

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if",
            "in", "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
        };

        /// <summary>
        /// The Lua literal of a value tree (without a leading "return"). Supported values:
        /// <list type="bullet">
        /// <item><c>IDictionary&lt;string, object&gt;</c>: a table, keys sorted ordinally, one entry per
        /// line; identifier keys are written bare, other keys and Lua keywords as ["key"];</item>
        /// <item><c>IList</c>: a sequence, on one line when all items are scalars and the line has at
        /// most 80 characters, otherwise one item per line;</item>
        /// <item><c>string</c>, <c>int</c>, <c>double</c> (see <see cref="FormatNumber"/>) and
        /// <c>bool</c>.</item>
        /// </list>
        /// Nested tables are indented by two spaces and every entry ends with a comma.
        /// </summary>
        public static string Serialize(object value)
        {
            var output = new StringBuilder();
            Write(value, 0, output);
            return output.ToString();
        }

        /// <summary>
        /// A double as Python's repr() writes it, so Lua reads it back as the same float: the shortest
        /// digits that round-trip, always with a '.' or an exponent (1.0, 0.0001, 1e-05, 1e+16).
        /// </summary>
        /// <exception cref="ArgumentException">The value is infinite or NaN.</exception>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException("Lua literals can not hold infinity or NaN", nameof(value));
            }
            bool negative = BitConverter.DoubleToInt64Bits(value) < 0;
            string sign = negative ? "-" : "";
            if (value == 0)
            {
                return sign + "0.0";
            }
            (string digits, int point) = ShortestDigits(Math.Abs(value));
            // value = 0.<digits> x 10^point; Python switches to exponent notation outside 1e-4 .. 1e16
            if (point <= -4 || point > 16)
            {
                string mantissa = digits.Length > 1 ? digits.Substring(0, 1) + "." + digits.Substring(1) : digits;
                int exponent = point - 1;
                return sign + mantissa + (exponent < 0 ? "e-" : "e+") +
                    Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
            }
            if (point <= 0)
            {
                return sign + "0." + new string('0', -point) + digits;
            }
            if (point >= digits.Length)
            {
                return sign + digits + new string('0', point - digits.Length) + ".0";
            }
            return sign + digits.Substring(0, point) + "." + digits.Substring(point);
        }

        /// <summary>A string literal escaped like lua_table.py: \" \\ \n, other control characters as \ddd.</summary>
        public static string FormatString(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            var output = new StringBuilder(value.Length + 2);
            output.Append('"');
            foreach (char c in value)
            {
                if (c == '"' || c == '\\')
                {
                    output.Append('\\').Append(c);
                }
                else if (c == '\n')
                {
                    output.Append("\\n");
                }
                else if (c < 32 || c == 127)
                {
                    output.Append('\\').Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
                }
                else
                {
                    output.Append(c);
                }
            }
            output.Append('"');
            return output.ToString();
        }

        /// <summary>
        /// The significant digits ("12345", no leading or trailing zeros) of a positive finite double and
        /// the position of the decimal point: value = 0.digits x 10^point. These are Python's repr()
        /// digits: the fewest that read back as the value, and of those the decimal nearest to it, ties to
        /// an even last digit. They are worked out exactly with BigInteger, because double.ToString("R")
        /// is the shortest round trip only on .NET Core 3.0 and later; Mono, which Unity runs, writes 15
        /// or 17 digits checked with its own inexact parser.
        /// </summary>
        private static (string digits, int point) ShortestDigits(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponentBits = (int)((bits >> 52) & 0x7FF);
            long mantissa = bits & 0xFFFFFFFFFFFFFL;
            int exponent;
            if (exponentBits == 0)
            {
                exponent = -1074; // subnormal
            }
            else
            {
                mantissa |= 1L << 52;
                exponent = exponentBits - 1075;
            }
            // In units of 2^(exponent - 2), value is 4 * mantissa. The decimals that read back as value
            // lie between low and high, the midpoints to its neighbours, or on them when the mantissa is
            // even (ties round to even). Below a normal power of two (other than the smallest normal)
            // the neighbour is half as far away as the one above.
            BigInteger center = (BigInteger)mantissa << 2;
            BigInteger high = center + 2;
            BigInteger low = center - (mantissa == 1L << 52 && exponentBits > 1 ? 1 : 2);
            bool inclusive = (mantissa & 1) == 0;
            // one unit is unit / unitDenominator
            BigInteger unit = exponent >= 2 ? BigInteger.One << (exponent - 2) : BigInteger.One;
            BigInteger unitDenominator = exponent >= 2 ? BigInteger.One : BigInteger.One << (2 - exponent);
            // Try ever finer decimal places 10^q, starting above value (Math.Log10 may be one off near
            // powers of ten). The first place with a multiple n * 10^q inside the interval gives the
            // fewest digits (17 always do); of the two multiples around value, the nearer wins, ties to
            // an even n.
            for (int q = (int)Math.Floor(Math.Log10(value)) + 2; ; q--)
            {
                // t units and n * 10^q compare as t * binaryScale and n * decimalScale, both integers
                BigInteger binaryScale = q >= 0 ? unit : unit * BigInteger.Pow(10, -q);
                BigInteger decimalScale = q >= 0 ? unitDenominator * BigInteger.Pow(10, q) : unitDenominator;
                BigInteger target = center * binaryScale;
                BigInteger n = BigInteger.Divide(target, decimalScale);
                BigInteger down = n * decimalScale;
                BigInteger up = down + decimalScale;
                bool downInside = inclusive ? down >= low * binaryScale : down > low * binaryScale;
                bool upInside = inclusive ? up <= high * binaryScale : up < high * binaryScale;
                if (!downInside && !upInside)
                {
                    continue;
                }
                if (upInside)
                {
                    int nearer = (up - target).CompareTo(target - down);
                    if (!downInside || nearer < 0 || (nearer == 0 && !n.IsEven))
                    {
                        n += 1;
                    }
                }
                string digits = n.ToString(CultureInfo.InvariantCulture);
                return (digits.TrimEnd('0'), digits.Length + q);
            }
        }

        private static void Write(object value, int level, StringBuilder output)
        {
            if (value is IDictionary<string, object> table)
            {
                WriteTable(table, level, output);
            }
            else if (value is IList list)
            {
                WriteSequence(list, level, output);
            }
            else
            {
                output.Append(Scalar(value));
            }
        }

        private static void WriteTable(IDictionary<string, object> table, int level, StringBuilder output)
        {
            if (table.Count == 0)
            {
                output.Append("{}");
                return;
            }
            var keys = new List<string>(table.Keys);
            keys.Sort(StringComparer.Ordinal);
            string pad = new string(' ', Indent * (level + 1));
            output.Append("{\n");
            foreach (string key in keys)
            {
                output.Append(pad).Append(Key(key)).Append(" = ");
                Write(table[key], level + 1, output);
                output.Append(",\n");
            }
            output.Append(' ', Indent * level).Append('}');
        }

        private static void WriteSequence(IList list, int level, StringBuilder output)
        {
            if (list.Count == 0)
            {
                output.Append("{}");
                return;
            }
            string line = InlineSequence(list);
            if (line != null && CodePoints.Count(line) <= MaxInlineLength)
            {
                output.Append(line);
                return;
            }
            string pad = new string(' ', Indent * (level + 1));
            output.Append("{\n");
            foreach (object item in list)
            {
                output.Append(pad);
                Write(item, level + 1, output);
                output.Append(",\n");
            }
            output.Append(' ', Indent * level).Append('}');
        }

        /// <summary>"{ a, b, c }" for a list of scalars, null when it holds a table.</summary>
        private static string InlineSequence(IList list)
        {
            var parts = new string[list.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                object item = list[i];
                if (item is IList || item is IDictionary<string, object>)
                {
                    return null;
                }
                parts[i] = Scalar(item);
            }
            return "{ " + string.Join(", ", parts) + " }";
        }

        private static string Key(string key) => IsIdentifier(key) && !Keywords.Contains(key) ? key : "[" + FormatString(key) + "]";

        private static bool IsIdentifier(string key)
        {
            if (key.Length == 0)
            {
                return false;
            }
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
                if (!letter && (i == 0 || c < '0' || c > '9'))
                {
                    return false;
                }
            }
            return true;
        }

        private static string Scalar(object value)
        {
            switch (value)
            {
                case bool b:
                    return b ? "true" : "false";
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case double d:
                    return FormatNumber(d);
                case string s:
                    return FormatString(s);
                case null:
                    throw new ArgumentException("Lua table literals can not hold null (nil) values");
                default:
                    throw new ArgumentException("can not write a " + value.GetType().Name + " as a Lua literal");
            }
        }
    }
}
