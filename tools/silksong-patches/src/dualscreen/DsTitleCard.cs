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

    public void Build(Transform parent)
    {
        _root = DsWidgets.Rect(parent, "title-card");
        DsWidgets.Stretch(_root);

        var bg = DsWidgets.Box(_root, "bg", DsTheme.Ground);
        DsWidgets.Stretch(bg.rectTransform);

        var logo = DsWidgets.Icon(_root, "logo", DsLogoArt.Sprite, Color.white);
        logo.useSpriteMesh = false;
        DsWidgets.Stretch(logo.rectTransform);

        SetVisible(false);
    }

    public void SetVisible(bool on)
    {
        if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
    }
}
#endif
