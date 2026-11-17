using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DualSouls.Mods
{
    /// <summary>
    /// #38 per-save death counter, shared by both games. Counts every death of the
    /// loaded save slot, plus the deaths in the current boss fight: the streak grows
    /// while the player keeps dying to the boss of one room and ends when they leave
    /// that room alive. Stored in a small sidecar per game and slot, beside the save.
    /// </summary>
    public sealed class DeathTally
    {
        // A new game in a slot that was deleted from the game's own menu starts
        // again from zero: its play time is under a minute while the sidecar
        // recorded a longer one.
        const float NewSaveSeconds = 60f;

        readonly string _directory;
        readonly string _game;
        int _profile = -1;
        object _save;
        bool _wasDead;
        string _scene;
        bool _diedInScene;
        float _recordedPlayTime;

        public DeathTally(string directory, string game)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A sidecar directory is required.", nameof(directory));
            if (string.IsNullOrEmpty(game)) throw new ArgumentException("A game name is required.", nameof(game));
            _directory = directory;
            _game = game;
        }

        public int Total { get; private set; }
        public int BossDeaths { get; private set; }
        public string BossScene { get; private set; }
        public int Profile { get { return _profile; } }
        /// <summary>The last sidecar read or write failure; the count keeps working in memory.</summary>
        public string LastError { get; private set; }

        /// <summary>The header text, or null while no save slot is bound.</summary>
        public string Label
        {
            get
            {
                if (_profile < 0) return null;
                return BossDeaths > 0 ? "DEATHS " + Total + "   BOSS " + BossDeaths : "DEATHS " + Total;
            }
        }

        public string PathFor(int profile)
        {
            return Path.Combine(_directory, _game + "-profile-" + profile + ".txt");
        }

        /// <summary>
        /// Called once per frame with the loaded save. <paramref name="save"/> is the
        /// game's save object, used only for identity: a reload or a new game in the
        /// same slot replaces it. <paramref name="bossPresent"/> is asked only when a
        /// death is counted.
        /// </summary>
        public void Tick(object save, int profile, float playTime, string scene, bool dead, Func<bool> bossPresent)
        {
            if (save == null || profile <= 0)
            {
                Unbind();
                return;
            }
            if (profile != _profile || !ReferenceEquals(save, _save))
            {
                Bind(save, profile, playTime, scene, dead);
                return;
            }

            if (scene != _scene)
            {
                // Left the boss room alive: that fight is over.
                if (BossDeaths > 0 && _scene != null && _scene == BossScene && !_diedInScene && !dead)
                {
                    BossDeaths = 0;
                    BossScene = null;
                    Save(playTime);
                }
                _scene = scene;
                _diedInScene = false;
            }

            if (dead && !_wasDead)
            {
                Total++;
                bool boss = false;
                if (bossPresent != null)
                {
                    try { boss = bossPresent(); }
                    catch (Exception error) { LastError = "boss probe: " + error.Message; }
                }
                if (boss)
                {
                    if (scene == BossScene) BossDeaths++;
                    else { BossScene = scene; BossDeaths = 1; }
                }
                _diedInScene = true;
                Save(playTime);
            }
            _wasDead = dead;
        }

        void Bind(object save, int profile, float playTime, string scene, bool dead)
        {
            bool sameSlot = profile == _profile;
            _profile = profile;
            _save = save;
            // Binding never counts: a save loaded while dead was already counted.
            _wasDead = dead;
            _scene = scene;
            _diedInScene = false;
            if (!sameSlot) Load(profile);
            if (playTime < NewSaveSeconds && _recordedPlayTime > playTime + NewSaveSeconds)
            {
                Total = 0;
                BossDeaths = 0;
                BossScene = null;
                Save(playTime);
            }
        }

        void Unbind()
        {
            _profile = -1;
            _save = null;
            _scene = null;
            _wasDead = false;
            _diedInScene = false;
            Total = 0;
            BossDeaths = 0;
            BossScene = null;
            _recordedPlayTime = 0f;
        }

        void Load(int profile)
        {
            Total = 0;
            BossDeaths = 0;
            BossScene = null;
            _recordedPlayTime = 0f;
            string path = PathFor(profile);
            try
            {
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int split = line.IndexOf('=');
                    if (split <= 0) continue;
                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1).Trim();
                    int number;
                    float seconds;
                    switch (key)
                    {
                        case "total":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0) Total = number;
                            break;
                        case "boss":
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) && number >= 0) BossDeaths = number;
                            break;
                        case "bossScene":
                            BossScene = value.Length == 0 ? null : value;
                            break;
                        case "playTime":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds >= 0f) _recordedPlayTime = seconds;
                            break;
                    }
                }
                if (BossScene == null) BossDeaths = 0;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                LastError = "read " + path + ": " + error.Message;
            }
        }

        void Save(float playTime)
        {
            _recordedPlayTime = playTime;
            string path = PathFor(_profile);
            try
            {
                Directory.CreateDirectory(_directory);
                var text = new StringBuilder();
                text.Append("total=").Append(Total.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("boss=").Append(BossDeaths.ToString(CultureInfo.InvariantCulture)).Append('\n');
                text.Append("bossScene=").Append(BossScene ?? "").Append('\n');
                text.Append("playTime=").Append(playTime.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                LastError = "write " + path + ": " + error.Message;
            }
        }
    }
}
