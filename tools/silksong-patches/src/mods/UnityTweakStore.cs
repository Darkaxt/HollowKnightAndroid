#if UNITY_ANDROID && !UNITY_EDITOR
using DualSouls.Mods;
using UnityEngine;

namespace DualSouls.Mods.Silksong
{
    /// <summary>Durable per-profile storage backed by the game's PlayerPrefs file.</summary>
    public sealed class UnityTweakStore : ITweakStore
    {
        public string Read(string key)
        {
            return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        }

        public void Write(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
        }

        public void Flush()
        {
            PlayerPrefs.Save();
        }
    }
}
#endif
