using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack
{
    public class LuaWriterTests
    {
        private static Dictionary<string, object> Table(params (string key, object value)[] entries)
        {
            var table = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach ((string key, object value) in entries)
            {
                table.Add(key, value);
            }
            return table;
        }

        [Test]
        public void WritesTablesLikeLuaTableDumps()
        {
            Dictionary<string, object> value = Table(
                ("id", "mika"),
                ("format_version", 1),
                ("tintable", false),
                ("icon", Table(("size", 64), ("filename", "__vrc-avatar-mika__/graphics/icon.png"))),
                ("animations", Table(("run-gun", Table(("direction_count", 18), ("shift", new List<object> { 0.0, -1.03125 }))))),
                ("end", new List<object>()),
                ("empty", Table()));
            const string expected =
                "{\n" +
                "  animations = {\n" +
                "    [\"run-gun\"] = {\n" +
                "      direction_count = 18,\n" +
                "      shift = { 0.0, -1.03125 },\n" +
                "    },\n" +
                "  },\n" +
                "  empty = {},\n" +
                "  [\"end\"] = {},\n" +
                "  format_version = 1,\n" +
                "  icon = {\n" +
                "    filename = \"__vrc-avatar-mika__/graphics/icon.png\",\n" +
                "    size = 64,\n" +
                "  },\n" +
                "  id = \"mika\",\n" +
                "  tintable = false,\n" +
                "}";
            Assert.That(LuaWriter.Serialize(value), Is.EqualTo(expected));
        }

        [Test]
        public void KeysAreBareOnlyForIdentifiersThatAreNotKeywords()
        {
            Dictionary<string, object> value = Table(("_x1", 1), ("A", 2), ("1a", 3), ("a b", 4), ("", 5), ("nil", 6),
                ("é", 7), ("while", 8));
            const string expected =
                "{\n  [\"\"] = 5,\n  [\"1a\"] = 3,\n  A = 2,\n  _x1 = 1,\n  [\"a b\"] = 4,\n  [\"nil\"] = 6,\n" +
                "  [\"while\"] = 8,\n  [\"é\"] = 7,\n}";
            Assert.That(LuaWriter.Serialize(value), Is.EqualTo(expected));
        }

        [Test]
        public void SequencesOfScalarsStayOnOneLineUpToEightyCharacters()
        {
            // "{ " + 25 x "1, " ... : 2 + 26 * 3 - 2 + 2 = 80 characters
            var fits = Enumerable.Range(0, 26).Select(_ => (object)1).ToList();
            string line = LuaWriter.Serialize(fits);
            Assert.That(line.Length, Is.EqualTo(80));
            Assert.That(line, Does.Not.Contain("\n"));
            fits.Add(1);
            Assert.That(LuaWriter.Serialize(fits), Does.StartWith("{\n  1,\n  1,\n"));
            // the limit is on the sequence itself, not its indentation
            string nested = LuaWriter.Serialize(Table(("a", Table(("b", Enumerable.Range(0, 26).Select(_ => (object)1).ToList())))));
            Assert.That(nested.Split('\n')[2], Has.Length.EqualTo(4 + "b = ".Length + 80 + ",".Length));
            // a table inside a sequence breaks the line
            Assert.That(LuaWriter.Serialize(new List<object> { 1, new List<object> { 2 } }), Is.EqualTo("{\n  1,\n  { 2 },\n}"));
            Assert.That(LuaWriter.Serialize(new object[] { "a", true }), Is.EqualTo("{ \"a\", true }"));
        }

        [TestCase(1.0, "1.0")]
        [TestCase(-2.0, "-2.0")]
        [TestCase(0.0, "0.0")]
        [TestCase(-0.0, "-0.0")]
        [TestCase(0.119, "0.119")]
        [TestCase(0.1, "0.1")]
        [TestCase(-1.03125, "-1.03125")]
        [TestCase(0.0001, "0.0001")]
        [TestCase(0.00001, "1e-05")]
        [TestCase(1.5e-10, "1.5e-10")]
        [TestCase(1e15, "1000000000000000.0")]
        [TestCase(1234567890123456.0, "1234567890123456.0")]
        [TestCase(1e16, "1e+16")]
        [TestCase(123456789012345678.0, "1.2345678901234568e+17")]
        [TestCase(1e100, "1e+100")]
        [TestCase(5e-324, "5e-324")]
        [TestCase(double.MaxValue, "1.7976931348623157e+308")]
        [TestCase(123.456, "123.456")]
        // Mono's double.ToString("R") writes 17 digits for the first and too few to read back for the others
        [TestCase(0.08658892128279881, "0.08658892128279881")]
        [TestCase(0.057055269068598403, "0.057055269068598403")]
        [TestCase(20.153200000000002, "20.153200000000002")]
        // a power of two where repr picks the decimal on the other side of the nearest one
        [TestCase(7.120236347223045e-307, "7.120236347223045e-307")]
        public void FormatsNumbersLikePythonRepr(double value, string expected)
        {
            Assert.That(LuaWriter.FormatNumber(value), Is.EqualTo(expected));
        }

        [Test]
        public void FormatsNumbersTheSameInEveryCulture()
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(LuaWriter.Serialize(new List<object> { 0.5, -1234567.25, 3 }), Is.EqualTo("{ 0.5, -1234567.25, 3 }"));
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Test]
        public void EscapesStringsLikeLuaTable()
        {
            Assert.That(LuaWriter.FormatString("a\"b\\c\nd\te\u007f\u0001ミ😀"), Is.EqualTo("\"a\\\"b\\\\c\\nd\\009e\\127\\001ミ😀\""));
            Assert.That(LuaWriter.Serialize("x"), Is.EqualTo("\"x\""));
            Assert.Throws<ArgumentNullException>(() => LuaWriter.FormatString(null));
        }

        [Test]
        public void RefusesWhatLuaLiteralsCanNotHold()
        {
            Assert.Throws<ArgumentException>(() => LuaWriter.FormatNumber(double.NaN));
            Assert.Throws<ArgumentException>(() => LuaWriter.Serialize(new List<object> { double.PositiveInfinity }));
            Assert.Throws<ArgumentException>(() => LuaWriter.Serialize(Table(("a", null))));
            Assert.Throws<ArgumentException>(() => LuaWriter.Serialize(new List<object> { 1L }));
            Assert.Throws<ArgumentException>(() => LuaWriter.Serialize(new Dictionary<string, int> { ["a"] = 1 }));
        }

        [Test]
        public void MatchesLuaTableDumpsAndReadsBack()
        {
            var rng = new Random(13);
            var values = new List<object>();
            for (int i = 0; i < 150; i++)
            {
                values.Add(RandomValue(rng, 0));
            }
            values.Add(SyntheticPackTable());
            var cases = values.Select(v => new { value = Json(v), lua = LuaWriter.Serialize(v) }).ToList();
            string input = "[" + string.Join(",", cases.Select(c => "{\"value\":" + c.value + ",\"lua\":" + JsonSerializer.Serialize(c.lua) + "}")) + "]";
            const string script = @"
import json
import lua_table
cases = json.load(sys.stdin)
print(json.dumps([{'dumps': lua_table.dumps(c['value']), 'loaded': lua_table.loads(c['lua'])} for c in cases]))
";
            using (JsonDocument doc = JsonDocument.Parse(PythonTools.Run(script, input)))
            {
                JsonElement[] results = doc.RootElement.EnumerateArray().ToArray();
                for (int i = 0; i < values.Count; i++)
                {
                    Assert.That(cases[i].lua, Is.EqualTo(results[i].GetProperty("dumps").GetString()), $"case {i}");
                    AssertSame(values[i], results[i].GetProperty("loaded"), $"case {i}");
                }
            }
        }

        [Test]
        public void NumbersMatchPythonReprForAnyDouble()
        {
            var rng = new Random(17);
            var bits = new List<long>();
            while (bits.Count < 3000)
            {
                long b = (long)(rng.NextDouble() * long.MaxValue) * (rng.Next(2) == 0 ? 1 : -1);
                double v = BitConverter.Int64BitsToDouble(b);
                if (!double.IsNaN(v) && !double.IsInfinity(v))
                {
                    bits.Add(b);
                }
                // and "nice" numbers, where repr chooses between notations
                bits.Add(BitConverter.DoubleToInt64Bits(Math.Round(rng.NextDouble() * 1000, rng.Next(0, 6)) * Math.Pow(10, rng.Next(-8, 20))));
            }
            AssertMatchesPythonRepr(bits);
        }

        [Test]
        public void NumbersMatchPythonReprForPowersOfTwo()
        {
            // the gap to the double below a power of two is half the gap above it; random bits hardly
            // ever hit one, so test them all (2^-1074 is 5e-324), their neighbours and the largest double
            var bits = new List<long> { BitConverter.DoubleToInt64Bits(double.MaxValue) };
            for (int k = -1074; k <= 1023; k++)
            {
                long power = k < -1022 ? 1L << (k + 1074) : (long)(k + 1023) << 52;
                bits.AddRange(new[] { power - 1, power, power + 1 });
            }
            bits.AddRange(bits.Select(b => b | long.MinValue).ToList());
            AssertMatchesPythonRepr(bits);
        }

        private static void AssertMatchesPythonRepr(List<long> bits)
        {
            const string script = @"
import json, struct
print(json.dumps([repr(struct.unpack('<d', struct.pack('<q', b))[0]) for b in json.load(sys.stdin)]))
";
            string[] python = JsonSerializer.Deserialize<string[]>(PythonTools.Run(script, JsonSerializer.Serialize(bits)));
            for (int i = 0; i < bits.Count; i++)
            {
                Assert.That(LuaWriter.FormatNumber(BitConverter.Int64BitsToDouble(bits[i])), Is.EqualTo(python[i]));
            }
        }

        private static readonly string[] RandomKeys =
        {
            "idle", "run-gun", "_x", "A1", "end", "nil", "return", "1abc", "a b", "", "ü", "shift", "z", "k\"q",
        };

        private static object RandomValue(Random rng, int depth)
        {
            int kind = rng.Next(depth >= 3 ? 4 : 7);
            switch (kind)
            {
                case 0:
                    return RandomInt(rng);
                case 1:
                    return RandomDouble(rng);
                case 2:
                    return RandomString(rng);
                case 3:
                    return rng.Next(2) == 0;
                case 4:
                case 5:
                {
                    // sequences of scalars, long or short, or nested ones
                    int count = rng.Next(1, 30);
                    bool nested = rng.Next(3) == 0;
                    return Enumerable.Range(0, count).Select(_ => nested ? RandomValue(rng, depth + 1) : RandomValue(rng, 3)).ToList();
                }
                default:
                {
                    var table = new Dictionary<string, object>(StringComparer.Ordinal);
                    int count = rng.Next(1, 6);
                    for (int i = 0; i < count; i++)
                    {
                        table[RandomKeys[rng.Next(RandomKeys.Length)]] = RandomValue(rng, depth + 1);
                    }
                    return table;
                }
            }
        }

        private static int RandomInt(Random rng)
        {
            switch (rng.Next(3))
            {
                case 0: return rng.Next(-10, 300);
                case 1: return rng.Next(int.MinValue, int.MaxValue);
                default: return rng.Next(2) == 0 ? int.MaxValue : int.MinValue + 1;
            }
        }

        private static double RandomDouble(Random rng)
        {
            switch (rng.Next(4))
            {
                case 0: return Math.Round((rng.NextDouble() - 0.5) * 8, 5);
                case 1: return rng.Next(-5, 5);
                case 2: return (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-30, 30));
                default: return rng.Next(-100, 100) / 64.0;
            }
        }

        private static string RandomString(Random rng)
        {
            string[] pieces = { "a", "Z", " ", "\"", "\\", "\n", "\t", "\r", "\u0001", "\u007f", "ミ", "😀", "é", "]]", "--", "'" };
            var text = new StringBuilder();
            int count = rng.Next(0, 12);
            for (int i = 0; i < count; i++)
            {
                text.Append(pieces[rng.Next(pieces.Length)]);
            }
            return text.ToString();
        }

        /// <summary>The kind of table a pack description is, written by the pack writer.</summary>
        private static object SyntheticPackTable() => PackDescription.Table(SyntheticPack.Build(true, true));

        /// <summary>JSON for a value tree that keeps doubles and ints apart (doubles always have '.' or 'e').</summary>
        private static string Json(object value)
        {
            switch (value)
            {
                case IDictionary<string, object> table:
                    return "{" + string.Join(",", table.Select(e => JsonSerializer.Serialize(e.Key) + ":" + Json(e.Value))) + "}";
                case IList list:
                    return "[" + string.Join(",", list.Cast<object>().Select(Json)) + "]";
                case double d:
                    return LuaWriter.FormatNumber(d);
                case int i:
                    return i.ToString(CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "true" : "false";
                default:
                    return JsonSerializer.Serialize((string)value);
            }
        }

        /// <summary>Compares a value tree with what Python read back (an empty table reads as a list).</summary>
        private static void AssertSame(object expected, JsonElement actual, string where)
        {
            switch (expected)
            {
                case IDictionary<string, object> table when table.Count == 0:
                case IList list when list.Count == 0:
                    Assert.That(actual.ValueKind, Is.EqualTo(JsonValueKind.Array), where);
                    Assert.That(actual.GetArrayLength(), Is.EqualTo(0), where);
                    break;
                case IDictionary<string, object> table:
                    Assert.That(actual.ValueKind, Is.EqualTo(JsonValueKind.Object), where);
                    Assert.That(actual.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(table.Keys), where);
                    foreach (KeyValuePair<string, object> entry in table)
                    {
                        AssertSame(entry.Value, actual.GetProperty(entry.Key), where + "." + entry.Key);
                    }
                    break;
                case IList list:
                    Assert.That(actual.ValueKind, Is.EqualTo(JsonValueKind.Array), where);
                    Assert.That(actual.GetArrayLength(), Is.EqualTo(list.Count), where);
                    for (int i = 0; i < list.Count; i++)
                    {
                        AssertSame(list[i], actual[i], where + "[" + i + "]");
                    }
                    break;
                case double d:
                    string raw = actual.GetRawText();
                    Assert.That(raw.IndexOfAny(new[] { '.', 'e', 'E' }), Is.GreaterThanOrEqualTo(0), where + " stays a float: " + raw);
                    Assert.That(actual.GetDouble(), Is.EqualTo(d), where);
                    break;
                case int i:
                    Assert.That(actual.GetRawText(), Is.EqualTo(i.ToString(CultureInfo.InvariantCulture)), where);
                    break;
                case bool b:
                    Assert.That(actual.GetBoolean(), Is.EqualTo(b), where);
                    break;
                default:
                    Assert.That(actual.GetString(), Is.EqualTo((string)expected), where);
                    break;
            }
        }
    }
}
