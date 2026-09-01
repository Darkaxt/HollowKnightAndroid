#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

namespace DualSouls.Mods
{
    public sealed class PlayerPrefsTweakStore : ITweakStore
    {
        public string Read(string key) =>
            PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;

        public void Write(string key, string value) =>
            PlayerPrefs.SetString(key, value);

        public void Flush() => PlayerPrefs.Save();
    }
}
#endif
