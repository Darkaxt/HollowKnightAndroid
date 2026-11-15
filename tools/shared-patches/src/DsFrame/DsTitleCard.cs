// DsTitleCard — our logo, for when there is no game to show.
//
// Outside a save the second screen has nothing worth saying, so it shows the
// port's own title. The art is drawn for this panel, at its full 1240x1080
// with the margins built in, so it is fitted whole rather than by its ink: the
// logo then sits where it was drawn, a little above the middle.

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

public class DsTitleCard
{
    RectTransform _root;
    Image _logo;
    Sprite _shown;
    float _nextTry;

    public void Build(Transform parent)
    {
        _root = DsWidgets.Rect(parent, "title-card");
        DsWidgets.Stretch(_root);

        var bg = DsWidgets.Box(_root, "bg", DsTheme.Ground);
        DsWidgets.Stretch(bg.rectTransform);

        _logo = DsWidgets.Icon(_root, "logo", null, Color.white);
        _logo.useSpriteMesh = false;
        DsWidgets.Stretch(_logo.rectTransform);
        RefreshLogo();

        SetVisible(false);
    }

    public void SetVisible(bool on)
    {
        if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        if (on) RefreshLogo();
    }

    /// <summary>Retry a logo that was not loaded yet, while the card is shown.</summary>
    public void Tick()
    {
        if (_shown != null || _root == null || !_root.gameObject.activeSelf) return;
        if (Time.unscaledTime < _nextTry) return;
        _nextTry = Time.unscaledTime + 1f;
        RefreshLogo();
    }

    void RefreshLogo()
    {
        if (_logo == null) return;
        Sprite art = DsFrame.TitleLogo();
        if (art == _shown && _logo.sprite == art) return;
        _shown = art;
        _logo.sprite = art;
        _logo.enabled = art != null;
        if (art == null) return;
        _logo.color = Color.white;
        _logo.preserveAspect = true;
        var rt = _logo.rectTransform;
        if (art == DsLogoArt.Sprite)
        {
            DsWidgets.Stretch(rt);
        }
        else
        {
            rt.anchorMin = new Vector2(0.15f, 0.30f);
            rt.anchorMax = new Vector2(0.85f, 0.78f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
#endif
