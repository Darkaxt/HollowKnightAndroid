using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace DualSouls.Skins.HollowKnight.Core
{
    // Strict byte/JSON foundation only. Success does not validate any launch descriptor schema.
    // Only this parser constructs nodes, so rendering cannot receive forged, cyclic or unbounded trees.
    internal sealed class StrictJson
    {
        private const int MaxBytes = 8388608;
        private const int MaxDepth = 32;
        private const int MaxNodes = 400000;
        private const int MaxContainerItems = 256;
        private const string Hex = "0123456789abcdef";
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal enum ValueKind { Object, Array, String, Integer, Boolean, Null }
        internal enum Failure { None, InvalidInput, HashMismatch, InvalidUtf8, InvalidJson, NonCanonical }

        internal ValueKind Kind { get; }
        // Members/Items/Text are present only for their corresponding kinds; no coercions.
        internal IReadOnlyDictionary<string, StrictJson> Members { get; }
        internal IReadOnlyList<StrictJson> Items { get; }
        internal string Text { get; }
        internal bool Boolean { get; }
        internal bool ContainsNull { get; }

        private StrictJson(ValueKind kind, string text = null, bool boolean = false,
            Dictionary<string, StrictJson> members = null, List<StrictJson> items = null)
        {
            Kind = kind;
            Text = text;
            Boolean = boolean;
            bool containsNull = kind == ValueKind.Null;
            if (members != null)
            {
                Members = new ReadOnlyDictionary<string, StrictJson>(
                    new Dictionary<string, StrictJson>(members, StringComparer.Ordinal));
                foreach (var item in Members.Values) containsNull |= item.ContainsNull;
            }
            if (items != null)
            {
                Items = new List<StrictJson>(items).AsReadOnly();
                foreach (var item in Items) containsNull |= item.ContainsNull;
            }
            ContainsNull = containsNull;
        }

        // The expected digest is supplied by a trusted caller, not proof of external authenticity.
        // The bounded input is cloned once; hash, decoding and byte equality all use that snapshot.
        // No bytes are retained or exposed after the immutable tree has been produced.
        internal static bool TryParseCanonical(byte[] bytes, string expectedSha256,
            out StrictJson value, out Failure failure)
        {
            value = null;
            failure = Failure.InvalidInput;
            if (bytes == null || bytes.Length > MaxBytes || !IsDigest(expectedSha256)) return false;
            var snapshot = (byte[])bytes.Clone();
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(snapshot);
            for (int i = 0; i < digest.Length; i++)
            {
                if (expectedSha256[2 * i] != Hex[digest[i] >> 4] ||
                    expectedSha256[2 * i + 1] != Hex[digest[i] & 15])
                {
                    failure = Failure.HashMismatch;
                    return false;
                }
            }

            try
            {
                string text = Utf8.GetString(snapshot);
                var parsed = new Parser(text).Parse();
                byte[] canonical = Utf8.GetBytes(parsed.RenderCanonical());
                failure = Failure.NonCanonical;
                if (canonical.Length != snapshot.Length) return false;
                for (int i = 0; i < snapshot.Length; i++)
                    if (canonical[i] != snapshot[i]) return false;
                value = parsed;
                failure = Failure.None;
                return true;
            }
            catch (DecoderFallbackException)
            {
                failure = Failure.InvalidUtf8;
                return false;
            }
            catch (InvalidJsonException)
            {
                failure = Failure.InvalidJson;
                return false;
            }
        }

        private static bool IsDigest(string text)
        {
            if (text == null || text.Length != 64) return false;
            foreach (char c in text)
                if (!IsDigit(c) && (c < 'a' || c > 'f')) return false;
            return true;
        }

        internal bool TryGetNonnegativeInt32(out int value)
        {
            value = 0;
            if (Kind != ValueKind.Integer || !TryDecimal(Text, int.MaxValue, out long parsed)) return false;
            value = (int)parsed;
            return true;
        }

        internal bool TryGetNonnegativeInt64String(out long value)
        {
            value = 0;
            return Kind == ValueKind.String && TryDecimal(Text, long.MaxValue, out value);
        }

        private static bool TryDecimal(string text, long maximum, out long value)
        {
            value = 0;
            if (text.Length == 0 || (text.Length > 1 && text[0] == '0')) return false;
            long result = 0;
            foreach (char c in text)
            {
                if (!IsDigit(c)) return false;
                int digit = c - '0';
                if (result > (maximum - digit) / 10) return false;
                result = result * 10 + digit;
            }
            value = result;
            return true;
        }

        // Immutable text, not a mutable byte buffer. Callers may encode this with strict UTF-8.
        // This is Kotlin descriptor-style canonical rendering, not general RFC 8785 numbers.
        internal string RenderCanonical()
        {
            var output = new StringBuilder();
            AppendValue(output);
            return output.ToString();
        }

        private void AppendValue(StringBuilder output)
        {
            switch (Kind)
            {
                case ValueKind.Object:
                    output.Append('{');
                    var keys = new List<string>(Members.Keys);
                    // JSON property order is UTF-16 ordinal; pack ordering is a later typed concern.
                    keys.Sort(StringComparer.Ordinal);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        if (i != 0) output.Append(',');
                        AppendString(output, keys[i]);
                        output.Append(':');
                        Members[keys[i]].AppendValue(output);
                    }
                    output.Append('}');
                    break;
                case ValueKind.Array:
                    output.Append('[');
                    for (int i = 0; i < Items.Count; i++)
                    {
                        if (i != 0) output.Append(',');
                        Items[i].AppendValue(output);
                    }
                    output.Append(']');
                    break;
                case ValueKind.String: AppendString(output, Text); break;
                case ValueKind.Integer: output.Append(Text); break;
                case ValueKind.Boolean: output.Append(Boolean ? "true" : "false"); break;
                case ValueKind.Null: output.Append("null"); break;
            }
        }

        private static void AppendString(StringBuilder output, string text)
        {
            output.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\t': output.Append("\\t"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\r': output.Append("\\r"); break;
                    default:
                        if (c < 0x20) output.Append("\\u00").Append(Hex[c >> 4]).Append(Hex[c & 15]);
                        else output.Append(c);
                        break;
                }
            }
            output.Append('"');
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
        private sealed class InvalidJsonException : Exception { }

        private sealed class Parser
        {
            private readonly string source;
            private int index;
            private int nodes;

            internal Parser(string source) { this.source = source; }

            internal StrictJson Parse()
            {
                var value = Value(0);
                Require(index == source.Length);
                return value;
            }

            private StrictJson Value(int depth)
            {
                Require(depth <= MaxDepth);
                ReserveNode();
                Require(index < source.Length);
                switch (source[index])
                {
                    case '{': return ObjectValue(depth + 1);
                    case '[': return ArrayValue(depth + 1);
                    case '"': return new StrictJson(ValueKind.String, text: StringValue());
                    case 't': return Literal("true", ValueKind.Boolean, true);
                    case 'f': return Literal("false", ValueKind.Boolean, false);
                    case 'n': return Literal("null", ValueKind.Null, false);
                    default:
                        Require(source[index] == '-' || IsDigit(source[index]));
                        return Number();
                }
            }

            private StrictJson ObjectValue(int childDepth)
            {
                index++;
                var members = new Dictionary<string, StrictJson>(StringComparer.Ordinal);
                if (Take('}')) return new StrictJson(ValueKind.Object, members: members);
                while (true)
                {
                    Require(members.Count < MaxContainerItems);
                    ReserveNode(); // Keys consume nodes, but not value depth.
                    string key = StringValue();
                    Require(!members.ContainsKey(key)); // After escape decoding.
                    Require(Take(':'));
                    members.Add(key, Value(childDepth));
                    if (Take('}')) return new StrictJson(ValueKind.Object, members: members);
                    Require(Take(','));
                }
            }

            private StrictJson ArrayValue(int childDepth)
            {
                index++;
                var items = new List<StrictJson>();
                if (Take(']')) return new StrictJson(ValueKind.Array, items: items);
                while (true)
                {
                    Require(items.Count < MaxContainerItems);
                    items.Add(Value(childDepth));
                    if (Take(']')) return new StrictJson(ValueKind.Array, items: items);
                    Require(Take(','));
                }
            }

            private string StringValue()
            {
                Require(Take('"'));
                var output = new StringBuilder();
                while (index < source.Length)
                {
                    char c = source[index++];
                    if (c == '"')
                    {
                        string text = output.ToString();
                        RequirePairedSurrogates(text);
                        return text;
                    }
                    if (c == '\\')
                    {
                        Require(index < source.Length);
                        char escaped = source[index++];
                        switch (escaped)
                        {
                            case '"': case '\\': case '/': output.Append(escaped); break;
                            case 'b': output.Append('\b'); break;
                            case 't': output.Append('\t'); break;
                            case 'n': output.Append('\n'); break;
                            case 'f': output.Append('\f'); break;
                            case 'r': output.Append('\r'); break;
                            case 'u': output.Append(UnicodeEscape()); break;
                            default: throw new InvalidJsonException();
                        }
                    }
                    else
                    {
                        Require(c >= 0x20);
                        output.Append(c);
                    }
                }
                throw new InvalidJsonException();
            }

            private char UnicodeEscape()
            {
                Require(source.Length - index >= 4);
                int value = 0;
                for (int i = 0; i < 4; i++)
                {
                    char c = source[index++];
                    int digit = IsDigit(c) ? c - '0' :
                        c >= 'a' && c <= 'f' ? c - 'a' + 10 :
                        c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    Require(digit >= 0);
                    value = value * 16 + digit;
                }
                return (char)value;
            }

            private static void RequirePairedSurrogates(string text)
            {
                for (int i = 0; i < text.Length; i++)
                {
                    if (char.IsHighSurrogate(text[i]))
                    {
                        Require(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]));
                        i++;
                    }
                    else Require(!char.IsLowSurrogate(text[i]));
                }
            }

            private StrictJson Number()
            {
                int start = index;
                bool negative = Take('-');
                Require(index < source.Length && IsDigit(source[index]));
                if (Take('0'))
                {
                    // Reject -0 early: no typed descriptor integer accepts it.
                    Require(!negative);
                    Require(index == source.Length || !IsDigit(source[index]));
                }
                else while (index < source.Length && IsDigit(source[index])) index++;
                Require(index == source.Length || (source[index] != '.' && source[index] != 'e' && source[index] != 'E'));
                return new StrictJson(ValueKind.Integer, text: source.Substring(start, index - start));
            }

            private StrictJson Literal(string text, ValueKind kind, bool boolean)
            {
                Require(source.Length - index >= text.Length &&
                    string.CompareOrdinal(source, index, text, 0, text.Length) == 0);
                index += text.Length;
                return new StrictJson(kind, boolean: boolean);
            }

            private void ReserveNode()
            {
                Require(nodes < MaxNodes);
                nodes++;
            }

            private bool Take(char c)
            {
                if (index >= source.Length || source[index] != c) return false;
                index++;
                return true;
            }

            private static void Require(bool condition)
            {
                if (!condition) throw new InvalidJsonException();
            }
        }
    }
}
