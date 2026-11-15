// DsShell.Report — the QA pane census for the command channel's `report`.
// Kept apart from DsShell.cs so the shell's extracted test fixture does not
// need the UI Graphic hierarchy this census walks.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;

public partial class DsShell
{
    /// <summary>QA census for the command channel: one line per screen with its state and how much
    /// content its host holds (enabled images with a sprite/texture, non-empty texts), active first marked "&gt;".</summary>
    public string Report()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("panes: active=").Append(ActiveId ?? "-").Append(" paused=").Append(_paused);
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry e = _entries[i];
            int images = 0, texts = 0;
            var sample = new System.Text.StringBuilder();
            if (e.Host != null && e.Built)
                foreach (Graphic g in e.Host.GetComponentsInChildren<Graphic>(true))
                {
                    if (!g.enabled || g.color.a < 0.01f) continue;
                    var image = g as Image;
                    var raw = g as RawImage;
                    var text = g as TmpText;
                    if ((image != null && image.sprite != null) || (raw != null && raw.texture != null)) images++;
                    else if (text != null && !string.IsNullOrEmpty(text.text))
                    {
                        texts++;
                        if (sample.Length < 60) sample.Append(sample.Length > 0 ? " | " : "").Append(text.text.Replace('\n', ' '));
                    }
                }
            bool available;
            try { available = e.Screen.Available; } catch (Exception) { available = false; }
            sb.Append('\n').Append(i == _active ? "> " : "  ").Append(e.Screen.Id)
              .Append(" built=").Append(e.Built).Append(" broken=").Append(e.Broken).Append(" available=").Append(available)
              .Append(" visible=").Append(e.Host != null && e.Host.gameObject.activeInHierarchy)
              .Append(" images=").Append(images).Append(" texts=").Append(texts);
            if (sample.Length > 0)
                sb.Append(" \"").Append(sample.Length > 60 ? sample.ToString(0, 60) + "…" : sample.ToString()).Append('"');
        }
        return sb.ToString();
    }
}
#endif
