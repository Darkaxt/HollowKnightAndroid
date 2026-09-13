using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DualSouls.Mods
{
    /// <summary>Atomic profile file shared by the launcher and Unity processes.</summary>
    public sealed class LineFileTweakStore : ITweakStore
    {
        readonly string _path;
        readonly Dictionary<string, string> _pending = new Dictionary<string, string>(StringComparer.Ordinal);

        public LineFileTweakStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A state path is required.", nameof(path));
            _path = Path.GetFullPath(path);
        }

        public static string ProfilePath(string persistentDataPath, string gameId)
        {
            if (string.IsNullOrWhiteSpace(persistentDataPath))
                throw new ArgumentException("A persistent data path is required.", nameof(persistentDataPath));
            if (!IsSafeSegment(gameId)) throw new ArgumentException("An exact game id is required.", nameof(gameId));
            return Path.Combine(Path.GetFullPath(persistentDataPath), "profiles", gameId, "mods", "builtin-state.txt");
        }

        public string Read(string key)
        {
            string pending;
            if (_pending.TryGetValue(key, out pending)) return pending;
            Dictionary<string, string> values = Load();
            string value;
            return values.TryGetValue(key, out value) ? value : null;
        }

        public void Write(string key, string value)
        {
            ValidateToken(key, nameof(key));
            ValidateToken(value, nameof(value));
            _pending[key] = value;
        }

        public void Flush()
        {
            if (_pending.Count == 0) return;
            Dictionary<string, string> values = Load();
            foreach (KeyValuePair<string, string> pair in _pending) values[pair.Key] = pair.Value;

            string directory = Path.GetDirectoryName(_path);
            Directory.CreateDirectory(directory);
            string temp = _path + ".tmp";
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            using (var writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
                for (int i = 0; i < keys.Count; i++) writer.Write(keys[i] + "=" + values[keys[i]] + "\n");

            try
            {
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
                _pending.Clear();
            }
            catch
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw;
            }
        }

        Dictionary<string, string> Load()
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                string[] lines = File.ReadAllLines(_path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int separator = line.IndexOf('=');
                    if (separator <= 0 || separator == line.Length - 1)
                        return new Dictionary<string, string>(StringComparer.Ordinal);
                    string key = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);
                    if (!IsToken(key) || !IsToken(value) || values.ContainsKey(key))
                        return new Dictionary<string, string>(StringComparer.Ordinal);
                    values.Add(key, value);
                }
                return values;
            }
            catch { return new Dictionary<string, string>(StringComparer.Ordinal); }
        }

        static bool IsSafeSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!(char.IsLetterOrDigit(c) || c == '-')) return false;
            }
            return true;
        }

        static bool IsToken(string value) =>
            !string.IsNullOrEmpty(value) && value.IndexOfAny(new[] { '=', '\r', '\n' }) < 0;

        static void ValidateToken(string value, string name)
        {
            if (!IsToken(value)) throw new ArgumentException("State keys and values must be one non-empty line.", name);
        }
    }
}
