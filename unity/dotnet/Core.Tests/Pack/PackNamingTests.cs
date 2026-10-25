using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace MilkJugg24.FactorioAvatarExporter.Core.Tests.Pack
{
    public class PackNamingTests
    {
        [TestCase("Mika's Avatar!!", "mika-s-avatar")]
        [TestCase("mika", "mika")]
        [TestCase("  --Hello   World--  ", "hello-world")]
        [TestCase("ミカ", "avatar")]
        [TestCase("", "avatar")]
        [TestCase("!!!", "avatar")]
        [TestCase("Corpse Party", "avatar-corpse-party")]
        [TestCase("Sprint Queen", "avatar-sprint-queen")]
        [TestCase("corpse", "corpse")]
        [TestCase("Corpse-", "corpse")]
        [TestCase("Ünïcödé Ñame 2", "n-c-d-ame-2")]
        [TestCase("ミカ v2", "v2")]
        [TestCase("Zoë_the_Dragon", "zo-the-dragon")]
        [TestCase("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwx", "abcdefghijklmnopqrstuvwxyzabcdefghi")]
        [TestCase("abcdefghijklmnopqrstuvwxyzabcdefgh ij", "abcdefghijklmnopqrstuvwxyzabcdefgh")]
        [TestCase("Corpse abcdefghijklmnopqrstuvwxyz abc", "avatar-corpse-abcdefghijklmnopqrstu")]
        [TestCase("Corpse abcdefghijklmnopqrstuvw xyz", "avatar-corpse-abcdefghijklmnopqrstu")]
        [TestCase("Corpse abcdefghijklmnopqrstu vwxyz", "avatar-corpse-abcdefghijklmnopqrstu")]
        [TestCase("Corpse abcdefghijklmnopqrst uvwxyz", "avatar-corpse-abcdefghijklmnopqrst")]
        public void DerivesIds(string title, string id)
        {
            string derived = PackNaming.DeriveId(title);
            Assert.That(derived, Is.EqualTo(id));
            Assert.That(PackNaming.IsValidId(derived));
        }

        [Test]
        public void DerivedIdsAreAlwaysValid()
        {
            var rng = new Random(5);
            const string alphabet = "aZ09 -_!.'ミ😀Ö";
            for (int i = 0; i < 3000; i++)
            {
                string title = (rng.Next(4) == 0 ? "corpse-" : "") +
                    new string(Enumerable.Range(0, rng.Next(0, 60)).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
                string id = PackNaming.DeriveId(title);
                Assert.That(PackNaming.IsValidId(id), $"\"{title}\" -> \"{id}\"");
                Assert.That(id, Does.Not.EndWith("-"));
            }
            Assert.Throws<ArgumentNullException>(() => PackNaming.DeriveId(null));
        }

        [TestCase("mika", true)]
        [TestCase("a", true)]
        [TestCase("9lives", true)]
        [TestCase("a-", true)]
        [TestCase("corpse", true)]
        [TestCase("corpses-x", true)]
        [TestCase("corpse-x", false)]
        [TestCase("sprint-x", false)]
        [TestCase("sprinter", true)]
        [TestCase("-a", false)]
        [TestCase("Mika", false)]
        [TestCase("mi_ka", false)]
        [TestCase("mi ka", false)]
        [TestCase("ミカ", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        [TestCase("abcdefghijklmnopqrstuvwxyzabcdefghi", true)]
        [TestCase("abcdefghijklmnopqrstuvwxyzabcdefghij", false)]
        public void ValidatesIds(string id, bool valid)
        {
            Assert.That(PackNaming.IsValidId(id), Is.EqualTo(valid));
        }

        [TestCase("1.0.0", true)]
        [TestCase("0.12.345", true)]
        [TestCase("65535.65535.65535", true)]
        [TestCase("65536.0.0", false)]
        [TestCase("1.0", false)]
        [TestCase("1.0.0.0", false)]
        [TestCase("1..0", false)]
        [TestCase("1.0.x", false)]
        [TestCase("1.0.-1", false)]
        [TestCase("1.0.0 ", false)]
        [TestCase("١.٠.٠", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void ValidatesVersions(string version, bool valid)
        {
            Assert.That(PackNaming.IsValidVersion(version), Is.EqualTo(valid));
        }

        [Test]
        public void ModAndZipNames()
        {
            Assert.That(PackNaming.ModName("mika"), Is.EqualTo("vrc-avatar-mika"));
            Assert.That(PackNaming.ZipRootFolder("mika", "1.2.3"), Is.EqualTo("vrc-avatar-mika_1.2.3"));
            Assert.That(PackNaming.ZipFileName("mika", "1.2.3"), Is.EqualTo("vrc-avatar-mika_1.2.3.zip"));
            Assert.Throws<ArgumentException>(() => PackNaming.ModName("Mika"));
            Assert.Throws<ArgumentException>(() => PackNaming.ZipRootFolder("mika", "1.2"));
            Assert.Throws<ArgumentException>(() => PackNaming.ZipFileName("corpse-x", "1.0.0"));
        }

        [Test]
        public void TitleLengthCountsCodePoints()
        {
            Assert.That(PackNaming.TitleLength("Mika"), Is.EqualTo(4));
            Assert.That(PackNaming.TitleLength("ミカ"), Is.EqualTo(2));
            Assert.That(PackNaming.TitleLength("🐉 dragon"), Is.EqualTo(8));
            Assert.That(PackNaming.TitleLength(""), Is.EqualTo(0));
            Assert.That(PackNaming.TitleLength(new string('\uD83D', 3)), Is.EqualTo(3), "lone surrogates count one each");
            Assert.Throws<ArgumentNullException>(() => PackNaming.TitleLength(null));
        }

        [Test]
        public void InfoTitleFitsFactoriosByteLimit()
        {
            Assert.That(PackNaming.InfoTitle("Mika"), Is.EqualTo("VRC Avatar: Mika"));
            Assert.That(PackNaming.InfoTitle("Mika", " Kat "), Is.EqualTo("VRC Avatar: Mika by Kat"));
            Assert.That(PackNaming.InfoTitle("Mika", " "), Is.EqualTo("VRC Avatar: Mika"), "no author, no by");
            // 12 bytes of prefix + 88 = exactly 100
            string fits = new string('x', 88);
            Assert.That(PackNaming.InfoTitle(fits), Is.EqualTo("VRC Avatar: " + fits));
            string cut = PackNaming.InfoTitle(new string('x', 89));
            Assert.That(cut, Is.EqualTo("VRC Avatar: " + new string('x', 85) + "…"));
            Assert.That(Encoding.UTF8.GetByteCount(cut), Is.EqualTo(100));
            // the title is shortened, the author kept
            string withAuthor = PackNaming.InfoTitle(new string('x', 100), "Kat");
            Assert.That(withAuthor, Is.EqualTo("VRC Avatar: " + new string('x', 78) + "… by Kat"));
            Assert.That(Encoding.UTF8.GetByteCount(withAuthor), Is.EqualTo(100));
            // 3-byte characters: 85 bytes after the prefix and ellipsis hold 28 of them
            string japanese = PackNaming.InfoTitle(new string('ミ', 40));
            Assert.That(japanese, Is.EqualTo("VRC Avatar: " + new string('ミ', 28) + "…"));
            // one ASCII character first: still 28 whole characters, the cut falls between them
            string mixed = PackNaming.InfoTitle("a" + new string('ミ', 40));
            Assert.That(mixed, Is.EqualTo("VRC Avatar: a" + new string('ミ', 28) + "…"));
            Assert.That(Encoding.UTF8.GetByteCount(mixed), Is.EqualTo(100));
            // an author too long to keep: the whole text is cut
            string longAuthor = PackNaming.InfoTitle("Mika", new string('k', 120));
            Assert.That(Encoding.UTF8.GetByteCount(longAuthor), Is.LessThanOrEqualTo(100));
            Assert.That(longAuthor, Does.StartWith("VRC Avatar: Mika by k").And.EndWith("…"));
            Assert.Throws<ArgumentNullException>(() => PackNaming.InfoTitle(null));
        }

        [Test]
        public void MatchesPythonSpec()
        {
            var rng = new Random(9);
            string[] pieces = { "a", "z", "0", "-", "_", "A", "corpse-", "ミ", "😀", "é", " ", "x", "9" };
            var strings = new List<string> { "", "a", "corpse-", "corpse", new string('a', 35), new string('a', 36) };
            for (int i = 0; i < 1500; i++)
            {
                var text = new StringBuilder();
                int count = rng.Next(0, i % 3 == 0 ? 60 : 12);
                for (int k = 0; k < count; k++)
                {
                    text.Append(pieces[rng.Next(pieces.Length)]);
                }
                strings.Add(text.ToString());
            }
            const string script = @"
import json
from avatar_pack import spec
items = json.load(sys.stdin)
print(json.dumps([[spec.valid_id(s), spec.info_title(s), len(s)] for s in items]))
";
            string output = PythonTools.Run(script, JsonSerializer.Serialize(strings));
            using (JsonDocument doc = JsonDocument.Parse(output))
            {
                JsonElement[] results = doc.RootElement.EnumerateArray().ToArray();
                for (int i = 0; i < strings.Count; i++)
                {
                    string s = strings[i];
                    Assert.That(PackNaming.IsValidId(s), Is.EqualTo(results[i][0].GetBoolean()), $"valid_id(\"{s}\")");
                    Assert.That(PackNaming.InfoTitle(s), Is.EqualTo(results[i][1].GetString()), $"info_title(\"{s}\")");
                    Assert.That(PackNaming.TitleLength(s), Is.EqualTo(results[i][2].GetInt32()), $"len(\"{s}\")");
                }
            }
        }
    }
}
