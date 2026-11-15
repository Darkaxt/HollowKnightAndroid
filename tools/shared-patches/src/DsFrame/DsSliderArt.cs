// DsSliderArt — the map's zoom slider.
//
// Ours, not the game's: docs/Slider.webp is the track and docs/Slider_Bar.webp
// the thumb, embedded as PNG base64 for the same reason as DsTrashArt. The
// conversion whitens the fully transparent pixels, which the WebPs store as
// black and bilinear filtering would otherwise blend into a dark fringe.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

public static class DsSliderArt
{
    const string SliderPng = "iVBORw0KGgoAAAANSUhEUgAAAB0AAAKeCAYAAABZKBBQAAAFqElEQVR42u3XTagVZRzH8e+517C6WgSlCVJkRWRItTAMLAi3CdHCLpQkFWGbokWv5MaVFbUJeoGkFmUQ0qJFQkQgahqIpVSQUeSFCl8gtBeUvP7azIHTaWbuzNx7avP9w3CZZ+Z5Pvd5+89zeknoEKuBQ8CpLpXH6BZPAhs71qXXoadXAt8Dx4GrgNP/RU8fBsaBy4EN/0VP5wNTwKLi/gfgOuDsKHt69wAIsAxY17qrSdpcu/LvOJik16adNuCKVMedbdA2w/tIzbOnR7GQFgI/FX+r4jZg91wupPtmAAGemeuFdCgzx7kkN87VnK4GVjSZKuCpuerptjSPs0munm1PFxcJoWmMA0/MdiE9WKS+NrEBWNIVHS+Se1l8Aeyryc+Pd53TtRXzNp3kliSrihVbFqeSXNIlDe6oaPD1hovsubbosqJHw3F0qAdXJPmzAj2W5MI2q3djxXw/Afw6cD8FvFzRxmXAQ03ndH6S4yX/+c6KT9iCJD9X9PZIkvOaDO/6kspnkiyvmf8HauZ2w/D7ZV+Zz4Bbh8q2AK8Ak8A1Rdk0cAD4pPgC7QduLhnMb4o0eq5qeG+qSOS7ixRXFd8m2Vfz/K664X0jo4l9VcN7cTFME4wm1gCfDqfB+0cI/uNI0+9pD/gauL4oPwycLL4ySzscVU8A3wE3ABcNlK8E9vfH+Y5i7L9KMplkbGAO1hTZpUlsTXLpQN3zk9yTZG/xfPvgQnovyaYk4xX7cGmSPTOAL9Scf3tJ7k0yleRaioyxssEJ4oKazLO54VlrQZKJtif8x0rAP9qe8Nv+gJoAfq84lI3092lmi47xP4SoqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKioqKio6FyhE/8H+lBFea9VK0nOS7IyCTNcFyT5OeWxuUF9kixIMtG/eS/JpiTjFS8vTbIn9fFCkl5F/V6Se5NMJbm2X3hHUfGrJJNJxgYqrElyLM1ia5JLB+qen+SeJHuL59uT0EvSn5OvgeuLUT8MnAQWA0s7zP0J4DvgBuCigfKVwP7BIXg0o42P+1a/pwAXAz/N1bYoiTXAp8Nb5iTw7ojAz/tg2T59rWxXAXuA6ZpGDxcNV8WWwZt5Qw+/BPYCtw5t/F3AOmASuKYonwYOAJ8U07K/AvwG+HA4OQxf60sWwZkky2s2/QM1C2jD8PtlDcxPcryk8s6Kzb+gJlMdKTLeP+qU7b8zwFsl5bcD60vKnwaWVAztS8BfZbm37FqWZLrkPz+a5JKB965I8mdFL48lubCs/brkvKOisdcH3tlWM5fPVbVdh66taGw6yS1JViU5V/HOqaERaYyOJ/mxotEDA0m86otDF5Qkz3bIsaeTLKlrd6avx9ZiNbeJt4FfZnNcOQp80AKcBl6cizPSqy3Q94Hvm5yRmlyHGszluSQ3Nmmv6YngtQbv7AAONmls8CNeFwuLL8nCmnduA3bP5bn3N+Cdmud7moJt5pQkK2rm884W7bRCSbKrBDxYc96d1UKq2z7PF0eaxtF0IfVjPjAFLCrufwCuA86O8gfUGeDNgfsX24JdegpwZZF1jgNXAafbNjCvwxn2CPBRsUVOd6jfqacAq4FDwKkulf8Gd8LOWD3dXaAAAAAASUVORK5CYII=";
    const string SliderBarPng = "iVBORw0KGgoAAAANSUhEUgAAACgAAAAPCAYAAACWV43jAAAAtElEQVR42tXVQUoCUBCA4e+9TWh4irCoVUiXENq18AxCnsZFRxDBfcdQV5LSKSLT1bh5gieQ6T/Bx8DMlIjQusUEb3hC13XbY4MFpviF0oB3+ERfjnYY4rtERBerRLhzXxhUvCfEwQMmJSKWeJazdYmIA26SAo8lLtY4Y1Xy/gXwkNh3rO16Z21TMUsMnJ0/yRL32aaHl9qe9BDbRLgdXrEvF2ewgzFGeETvyqifNrU5PvAHJ+zOOCKybEshAAAAAElFTkSuQmCC";

    static Sprite _track, _thumb;
    static bool _trackTried, _thumbTried;

    /// <summary>The line with an arrowhead at each end, sliced so only the shaft stretches.</summary>
    public static Sprite Track
    {
        get
        {
            if (!_trackTried)
            {
                _trackTried = true;
                _track = Decode(SliderPng, "DsSliderTrack", DsZoomSlider.CapHeight);
            }
            return _track;
        }
    }

    public static Sprite Thumb
    {
        get
        {
            if (!_thumbTried)
            {
                _thumbTried = true;
                _thumb = Decode(SliderBarPng, "DsSliderThumb", 0f);
            }
            return _thumb;
        }
    }

    static Sprite Decode(string base64, string name, float cap)
    {
        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = name };
            if (!tex.LoadImage(Convert.FromBase64String(base64)))
            {
                UnityEngine.Object.Destroy(tex);
                Debug.LogWarning("[DsSliderArt] " + name + ": PNG did not decode");
                return null;
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;

            var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height),
                                       new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                                       new Vector4(0f, cap, 0f, cap));
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[DsSliderArt] " + name + " failed: " + e.Message);
            return null;
        }
    }
}
#endif
