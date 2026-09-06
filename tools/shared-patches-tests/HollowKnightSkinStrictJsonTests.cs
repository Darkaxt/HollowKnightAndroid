using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DualSouls.Skins.HollowKnight.Core;
using Xunit;

namespace SharedPatches.Tests
{
    public sealed class HollowKnightSkinStrictJsonTests
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static StrictJson Parse(string text)
        {
            var bytes = Utf8.GetBytes(text);
            Assert.True(StrictJson.TryParseCanonical(bytes, Hash(bytes), out var value, out var failure), failure.ToString());
            Assert.Equal(StrictJson.Failure.None, failure);
            Assert.NotNull(value);
            return value;
        }

        private static void Reject(string text, StrictJson.Failure failure = StrictJson.Failure.InvalidJson)
        {
            var bytes = Utf8.GetBytes(text);
            Assert.False(StrictJson.TryParseCanonical(bytes, Hash(bytes), out var value, out var actual));
            Assert.Null(value);
            Assert.Equal(failure, actual);
        }

        [Theory]
        [InlineData("null", StrictJson.ValueKind.Null)]
        [InlineData("true", StrictJson.ValueKind.Boolean)]
        [InlineData("false", StrictJson.ValueKind.Boolean)]
        [InlineData("[]", StrictJson.ValueKind.Array)]
        [InlineData("\"text\"", StrictJson.ValueKind.String)]
        [InlineData("0", StrictJson.ValueKind.Integer)]
        [InlineData("-123456789012345678901234567890", StrictJson.ValueKind.Integer)]
        public void AllKindsRoundTripExactly(string text, object kind)
        {
            var value = Parse(text);
            Assert.Equal(kind, value.Kind);
            Assert.Equal(text, value.RenderCanonical());
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(" {}")]
        [InlineData("{} ")]
        [InlineData("{}\n")]
        [InlineData("{\"a\": 0}")]
        [InlineData("[0, 1]")]
        [InlineData("{}{}")]
        [InlineData("[0,]")]
        [InlineData("{\"a\":0,}")]
        [InlineData("{\"a\":0,\"a\":1}")]
        [InlineData("{\"a\":0,\"\\u0061\":1}")]
        [InlineData("{\"😀\":0,\"\\ud83d\\ude00\":1}")]
        [InlineData("{")]
        [InlineData("[")]
        [InlineData("\"")]
        [InlineData("\"\\")]
        [InlineData("\"\\u001\"")]
        [InlineData("\"\\u00xz\"")]
        [InlineData("\"\\x00\"")]
        [InlineData("\"\n\"")]
        [InlineData("\"\\ud800\"")]
        [InlineData("\"\\udc00\"")]
        [InlineData("\"\\ud800x\"")]
        [InlineData("\"\\ud800\\ud800\"")]
        [InlineData("{\"\\ud800\":0}")]
        [InlineData("tru")]
        [InlineData("True")]
        [InlineData("false0")]
        [InlineData("nulL")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("+1")]
        [InlineData("01")]
        [InlineData("-01")]
        [InlineData("-0")]
        [InlineData("-")]
        [InlineData("1.0")]
        [InlineData("1e0")]
        [InlineData("1E2")]
        [InlineData("١")]
        [InlineData("1١")]
        [InlineData("[1 2]")]
        [InlineData("{a:1}")]
        [InlineData("{\"a\"1}")]
        public void MalformedOrDisallowedSyntaxFailsClosed(string text) => Reject(text);

        [Theory]
        [InlineData("{\"b\":0,\"a\":1}")]
        [InlineData("\"\\/\"")]
        [InlineData("\"\\u0061\"")]
        [InlineData("\"\\ud83d\\ude00\"")]
        [InlineData("\"\\u000A\"")]
        [InlineData("\"\\u000a\"")]
        [InlineData("\"\\u001F\"")]
        [InlineData("{\"\\u0061\":0}")]
        public void ValidAlternativeSpellingsAreNotCanonical(string text) => Reject(text, StrictJson.Failure.NonCanonical);

        [Theory]
        [InlineData("efbbbf7b7d", StrictJson.Failure.InvalidJson)]
        [InlineData("22c08022", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22c0af22", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22e080af22", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22f08080af22", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22eda08022", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22edbfbf22", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22f490808022", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22f580808022", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22e282", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22f09f98", StrictJson.Failure.InvalidUtf8)]
        [InlineData("228022", StrictJson.Failure.InvalidUtf8)]
        [InlineData("22ff22", StrictJson.Failure.InvalidUtf8)]
        public void InvalidUtf8AndBomFailClosed(string hex, object expected)
        {
            var bytes = Convert.FromHexString(hex);
            Assert.False(StrictJson.TryParseCanonical(bytes, Hash(bytes), out var value, out var failure));
            Assert.Null(value);
            Assert.Equal(expected, failure);
        }

        [Fact]
        public void InputAndHashChecksPrecedeDecode()
        {
            var invalidUtf8 = new byte[] { 255 };
            foreach (var hash in new[] { null, "", new string('a', 63), new string('a', 65), new string('A', 64), new string('g', 64) })
            {
                Assert.False(StrictJson.TryParseCanonical(invalidUtf8, hash, out var value, out var failure));
                Assert.Null(value);
                Assert.Equal(StrictJson.Failure.InvalidInput, failure);
            }
            Assert.False(StrictJson.TryParseCanonical(null, new string('a', 64), out var absent, out var nullFailure));
            Assert.Null(absent);
            Assert.Equal(StrictJson.Failure.InvalidInput, nullFailure);
            Assert.False(StrictJson.TryParseCanonical(invalidUtf8, new string('0', 64), out var mismatch, out var mismatchFailure));
            Assert.Null(mismatch);
            Assert.Equal(StrictJson.Failure.HashMismatch, mismatchFailure);
        }

        [Fact]
        public void ByteBoundIsInclusiveAndOversizeDoesNotClone()
        {
            const int max = 8388608;
            var exact = Utf8.GetBytes("\"" + new string('a', max - 2) + "\"");
            Assert.True(StrictJson.TryParseCanonical(exact, Hash(exact), out var value, out var failure));
            Assert.Equal(max - 2, value.Text.Length);
            Assert.Equal(StrictJson.Failure.None, failure);
            var oversized = new byte[max + 1];
            var expectedHash = new string('0', 64);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool accepted = StrictJson.TryParseCanonical(oversized, expectedHash, out var rejected, out var rejectedFailure);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.False(accepted);
            Assert.Null(rejected);
            Assert.Equal(StrictJson.Failure.InvalidInput, rejectedFailure);
            Assert.True(allocated < 1024 * 1024, $"Oversize input allocated {allocated} bytes");
        }

        [Fact]
        public void DepthBoundChargesRootAtZeroAndValuesNotKeys()
        {
            string AtDepth(string leaf, int depth) => new string('[', depth) + leaf + new string(']', depth);
            Parse(AtDepth("0", 32));
            Parse(AtDepth("{}", 32));
            Parse(AtDepth("[]", 32));
            Reject(AtDepth("0", 33));
            Reject(AtDepth("{}", 33));
            Reject(AtDepth("{\"a\":0}", 32));
        }

        [Fact]
        public void EachContainerAllows256EntriesButNot257()
        {
            Parse("[" + string.Join(",", Enumerable.Repeat("0", 256)) + "]");
            Reject("[" + string.Join(",", Enumerable.Repeat("0", 257)) + "]");
            string Obj(int count) => "{" + string.Join(",", Enumerable.Range(0, count).Select(i => $"\"k{i:D3}\":0")) + "}";
            Parse(Obj(256));
            Reject(Obj(257));
        }

        // The budget is exact: array = 1 + child budgets; object = 1 + 2 * field count.
        // Evenly split arrays keep all containers <=256 and this tree only four levels deep.
        private static string NodeBudgetText(int budget, bool useKeys, ref int keys)
        {
            if (budget == 1) return "0";
            if (useKeys && budget <= 513 && budget % 2 == 1)
            {
                int fields = (budget - 1) / 2;
                keys += fields;
                return "{" + string.Join(",", Enumerable.Range(0, fields).Select(i => $"\"k{i:D3}\":0")) + "}";
            }
            int count = Math.Min(256, budget - 1);
            int perChild = (budget - 1) / count;
            int remainder = (budget - 1) % count;
            var children = new List<string>();
            for (int i = 0; i < count; i++) children.Add(NodeBudgetText(perChild + (i < remainder ? 1 : 0), useKeys, ref keys));
            return "[" + string.Join(",", children) + "]";
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NodeBudgetIncludesContainersValuesAndObjectKeys(bool useKeys)
        {
            int keys = 0;
            string exact = NodeBudgetText(400000, useKeys, ref keys);
            Assert.True(Utf8.GetByteCount(exact) < 8388608);
            if (useKeys) Assert.True(keys > 0);
            Parse(exact);
            keys = 0;
            Reject(NodeBudgetText(400001, useKeys, ref keys));
        }

        [Fact]
        public void RenderingUsesUtf16OrdinalNotUtf8AndDoesNotNormalize()
        {
            // UTF-16 D800 sorts before E000, although its UTF-8 F0 sorts after EE.
            const string canonical = "{\"𐀀\":1,\"\ue000\":2}";
            Assert.Equal(canonical, Parse(canonical).RenderCanonical());
            Reject("{\"\ue000\":2,\"𐀀\":1}", StrictJson.Failure.NonCanonical);
            const string distinct = "{\"é\":0,\"é\":1}";
            Assert.Equal(2, Parse(distinct).Members.Count);
        }

        [Fact]
        public void CanonicalEscapesAndLiteralUnicodeMatchHandWrittenBytes()
        {
            const string canonical = "\"\\\"\\\\/\\b\\t\\n\\f\\r\\u0000\\u0001\\u001a\\u001f é😀\u202e\u2066\u2028\ufeff\"";
            var value = Parse(canonical);
            Assert.Equal("\"\\/\b\t\n\f\r\0\u0001\u001a\u001f é😀\u202e\u2066\u2028\ufeff", value.Text);
            Assert.Equal(Utf8.GetBytes(canonical), Utf8.GetBytes(value.RenderCanonical()));
        }

        [Fact]
        public void All66UnicodeNoncharactersRemainLiteral()
        {
            var text = new StringBuilder();
            for (int cp = 0xfdd0; cp <= 0xfdef; cp++) text.Append(char.ConvertFromUtf32(cp));
            for (int plane = 0; plane <= 16; plane++)
            {
                text.Append(char.ConvertFromUtf32(plane * 0x10000 + 0xfffe));
                text.Append(char.ConvertFromUtf32(plane * 0x10000 + 0xffff));
            }
            var literal = text.ToString();
            Assert.Equal(66, literal.EnumerateRunes().Count());
            Assert.Equal(literal, Parse("\"" + literal + "\"").Text);
            Assert.Equal(literal, Assert.Single(Parse("{\"" + literal + "\":0}").Members).Key);
        }

        [Theory]
        [InlineData("0", true, 0)]
        [InlineData("2147483647", true, int.MaxValue)]
        [InlineData("2147483648", false, 0)]
        [InlineData("18446744073709551615", false, 0)]
        [InlineData("99999999999999999999999999999999999999999999", false, 0)]
        [InlineData("-1", false, 0)]
        [InlineData("\"1\"", false, 0)]
        [InlineData("true", false, 0)]
        [InlineData("null", false, 0)]
        [InlineData("[]", false, 0)]
        [InlineData("{}", false, 0)]
        public void NonnegativeInt32RequiresNumberKindAndExactRange(string text, bool success, int expected)
        {
            var value = Parse(text);
            Assert.Equal(success, value.TryGetNonnegativeInt32(out int actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("\"0\"", true, 0L)]
        [InlineData("\"9223372036854775807\"", true, long.MaxValue)]
        [InlineData("\"9223372036854775808\"", false, 0L)]
        [InlineData("\"18446744073709551615\"", false, 0L)]
        [InlineData("\"9999999999999999999999999999999999999999\"", false, 0L)]
        [InlineData("\"01\"", false, 0L)]
        [InlineData("\"-0\"", false, 0L)]
        [InlineData("\"-1\"", false, 0L)]
        [InlineData("\"+1\"", false, 0L)]
        [InlineData("\"1e0\"", false, 0L)]
        [InlineData("\"1.0\"", false, 0L)]
        [InlineData("\"١\"", false, 0L)]
        [InlineData("\" 1\"", false, 0L)]
        [InlineData("\"\"", false, 0L)]
        [InlineData("1", false, 0L)]
        [InlineData("false", false, 0L)]
        [InlineData("null", false, 0L)]
        [InlineData("[]", false, 0L)]
        [InlineData("{}", false, 0L)]
        public void NonnegativeInt64RequiresDecimalStringKindAndSignedLongRange(string text, bool success, long expected)
        {
            var value = Parse(text);
            Assert.Equal(success, value.TryGetNonnegativeInt64String(out long actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SnapshotAndCollectionsCannotBeMutatedByCaller()
        {
            const string original = "{\"a\":[true,false,null,\"text\",123]}";
            var bytes = Utf8.GetBytes(original);
            var digest = Hash(bytes);
            Assert.True(StrictJson.TryParseCanonical(bytes, digest, out var value, out _));
            Array.Fill(bytes, (byte)255);
            Assert.Equal(original, value.RenderCanonical());
            Assert.Equal(digest, Hash(Utf8.GetBytes(value.RenderCanonical())));
            Assert.False(value.Members is Dictionary<string, StrictJson>);
            var dictionary = Assert.IsAssignableFrom<IDictionary<string, StrictJson>>(value.Members);
            Assert.Throws<NotSupportedException>(() => dictionary.Clear());
            Assert.Throws<NotSupportedException>(() => dictionary["a"] = value);
            var items = value.Members["a"].Items;
            Assert.False(items is List<StrictJson>);
            Assert.False(items is StrictJson[]);
            var list = Assert.IsAssignableFrom<IList<StrictJson>>(items);
            Assert.Throws<NotSupportedException>(() => list.Clear());
            Assert.Throws<NotSupportedException>(() => list[0] = value);
            Assert.True(items[0].Boolean);
            Assert.False(items[1].Boolean);
            Assert.Equal(StrictJson.ValueKind.Null, items[2].Kind);
            Assert.Equal("text", items[3].Text);
            Assert.Equal("123", items[4].Text);
            Assert.True(value.ContainsNull);
            Assert.False(Parse("{\"null\":[false,0,\"null\"]}").ContainsNull);
            Assert.Equal(original, value.RenderCanonical());
        }

        [Fact]
        public void KnownHashVectorAcceptsAnImmutableObject()
        {
            var bytes = new byte[] { 123, 125 };
            Assert.True(StrictJson.TryParseCanonical(bytes,
                "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a",
                out var value, out var failure));
            Assert.Equal(StrictJson.Failure.None, failure);
            Assert.Equal(StrictJson.ValueKind.Object, value.Kind);
            Assert.Empty(value.Members);
            Assert.Equal("{}", value.RenderCanonical());
        }
    }
}
