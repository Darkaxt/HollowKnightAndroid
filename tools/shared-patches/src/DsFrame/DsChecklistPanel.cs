// DsChecklistPanel — #37 the completion checklist over a map page.
//
// One label, rich text: a totals line per kind, then one line per area that
// still has something missing. Rebuilt only when the text changes, so an open
// checklist costs nothing per frame.

#if UNITY_ANDROID && !UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using DualSouls.Mods;
using UnityEngine;
using UnityEngine.UI;
using TmpText = TMProOld.TextMeshProUGUI;
using TmpAlign = TMProOld.TextAlignmentOptions;

public sealed class DsChecklistPanel
{
    readonly Image _back;
    readonly TmpText _text;
    string _shown;

    public DsChecklistPanel(RectTransform parent, Rect bounds)
    {
        _back = DsWidgets.Box(parent, "checklist", DsTheme.Ground);
        DsWidgets.Place(_back.rectTransform, bounds);
        _text = DsWidgets.Label(_back.rectTransform, "checklist-text", "", DsTheme.SmallSize, DsTheme.Ink, TmpAlign.TopLeft);
        if (_text != null)
        {
            _text.richText = true;
            DsWidgets.Place(_text.rectTransform, DsTheme.Pad, DsTheme.Pad, bounds.width - DsTheme.Pad * 2f, bounds.height - DsTheme.Pad * 2f);
        }
        SetVisible(false);
    }

    public bool Visible { get { return _back != null && _back.gameObject.activeSelf; } }

    public void SetVisible(bool on) { DsWidgets.SetActive(_back, on); }

    /// <param name="kinds">Display order of kinds, with their plural labels.</param>
    public void Show(List<ChecklistArea> areas, ChecklistArea totals, IList<KeyValuePair<string, string>> kinds)
    {
        string text = Format(areas, totals, kinds);
        if (text == _shown || _text == null) return;
        _shown = text;
        _text.text = text;
    }

    public static string Format(List<ChecklistArea> areas, ChecklistArea totals, IList<KeyValuePair<string, string>> kinds)
    {
        var sb = new StringBuilder();
        sb.Append("<b>CHECKLIST</b>  <color=#9e9994>found / on the map</color>\n");
        for (int i = 0; i < kinds.Count; i++)
        {
            string k = kinds[i].Key;
            int total = totals.TotalOf(k);
            if (total == 0) continue;
            sb.Append(Tint(k)).Append(kinds[i].Value).Append(' ')
              .Append(total - totals.MissingOf(k)).Append('/').Append(total).Append("</color>   ");
        }
        sb.Append("\n\n");
        int lines = 0;
        foreach (var a in areas)
        {
            if (a.Missing == 0) continue;
            lines++;
            sb.Append("<b>").Append(a.Name).Append("</b>  ");
            for (int i = 0; i < kinds.Count; i++)
            {
                int m = a.MissingOf(kinds[i].Key);
                if (m == 0) continue;
                sb.Append(Tint(kinds[i].Key)).Append(m).Append(' ').Append(kinds[i].Value).Append("</color>  ");
            }
            sb.Append('\n');
        }
        if (lines == 0) sb.Append("Everything on the map is collected.");
        return sb.ToString();
    }

    static string Tint(string kind)
    {
        return "<color=#" + ColorUtility.ToHtmlStringRGB(CollectiblePins.ColorOf(kind)) + ">";
    }
}
#endif
