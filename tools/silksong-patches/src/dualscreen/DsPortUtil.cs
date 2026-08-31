// DsPortUtil — ownership and cloning rules shared by the resident UI port.
//
// It never authors replacement art. Stage 2 static frame chrome is staged
// inactive and stripped of drivers before entering display 1. Later native
// HUD/page/overlay ports have different lifecycle contracts and must not use
// this static-only sanitizer.

#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;
using Object = UnityEngine.Object;

public static class DsPortUtil
{
    public static RectTransform CreateRoot(Transform parent, string name, int layer,
                                           Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name);
        go.layer = layer;
        var root = go.AddComponent<RectTransform>();
        root.SetParent(parent, false);
        root.anchorMin = anchorMin;
        root.anchorMax = anchorMax;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        return root;
    }

    public static GameObject CloneStaticResidentVisual(GameObject source, Transform parent,
                                                        string name, int layer,
                                                        Type retainedVisualType)
    {
        if (source == null || parent == null || retainedVisualType == null ||
            (retainedVisualType != typeof(UnityEngine.UI.Image) &&
             retainedVisualType != typeof(TMProOld.TextMeshPro))) return null;
        GameObject staging = null;
        GameObject clone = null;
        try
        {
            staging = new GameObject("DsStaticCloneStaging");
            staging.SetActive(false);
            clone = Object.Instantiate(source, staging.transform, false);
            clone.SetActive(false);
            clone.name = name;
            SetLayerRecursive(clone.transform, layer);

            // Stage 2 frame ornaments and tab labels retain exactly one
            // component of the declared concrete visual type. Disabling an
            // arbitrary driver is insufficient because Awake could still run
            // when the clone first becomes active, so remove all other
            // MonoBehaviours from this owned, hierarchy-inactive clone.
            var behaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            int retainedCount = 0;
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (behaviour.GetType() == retainedVisualType) retainedCount++;
            }
            if (retainedCount != 1) throw new InvalidOperationException(
                "retained static visual type count was " + retainedCount + ": " +
                retainedVisualType.FullName);

            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType() == retainedVisualType) continue;
                Object.DestroyImmediate(behaviour);
            }

            var remainingBehaviours = clone.GetComponentsInChildren<MonoBehaviour>(true);
            if (remainingBehaviours.Length != 1 || remainingBehaviours[0] == null ||
                remainingBehaviours[0].GetType() != retainedVisualType)
                throw new InvalidOperationException(
                    "static visual driver removal did not converge: " +
                    retainedVisualType.FullName);
            var retained = remainingBehaviours[0];
            retained.enabled = true;
            retained.gameObject.SetActive(true);

            // These components are Behaviours rather than arbitrary
            // MonoBehaviours (or are otherwise outside the retained policy).
            var animators = clone.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++) if (animators[i] != null) animators[i].enabled = false;
            var animations = clone.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++) if (animations[i] != null) animations[i].enabled = false;
            var audio = clone.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audio.Length; i++) if (audio[i] != null) audio[i].enabled = false;
            var colliders2d = clone.GetComponentsInChildren<Collider2D>(true);
            for (int i = 0; i < colliders2d.Length; i++) if (colliders2d[i] != null) colliders2d[i].enabled = false;

            // Match the oracle's static chrome normalization while the whole
            // clone is still inactive in hierarchy under staging.
            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null) continue;
                renderer.gameObject.SetActive(true);
                renderer.enabled = true;
            }
            clone.SetActive(false);
            clone.transform.SetParent(parent, false);
            clone.SetActive(true);
            return clone;
        }
        catch (Exception e)
        {
            if (clone != null) Object.Destroy(clone);
            Debug.LogWarning("[DualScreen][capability-gap] static resident clone failed: " +
                             e.GetType().Name + ": " + e.Message);
            return null;
        }
        finally
        {
            if (staging != null) Object.Destroy(staging);
        }
    }

    public static void SetLayerRecursive(Transform root, int layer)
    {
        if (root == null) return;
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayerRecursive(root.GetChild(i), layer);
    }

    public static void NormalizeRenderers(GameObject root, int baseOrder)
    {
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null) continue;
            renderer.sortingLayerID = 0;
            renderer.sortingOrder = baseOrder + i;
            renderer.enabled = true;
        }
    }

    public static void DestroyOwned(ref GameObject owned)
    {
        if (owned != null) Object.Destroy(owned);
        owned = null;
    }

    public static void DestroyOwned(ref RectTransform owned)
    {
        if (owned != null) Object.Destroy(owned.gameObject);
        owned = null;
    }
}

// Renderer-based pages are not clipped by RectMask2D. These four owned black
// cover strips reproduce the reference's functional screen-cover mask: page
// renderers sort below them, while frame labels/art and the HUD sort above.
public sealed class DsRendererMaskCover
{
    GameObject _root;
    Mesh _mesh;
    Material _material;

    public DsRendererMaskCover(Transform parent, string name, int layer, int sortingOrder)
    {
        _root = new GameObject(name);
        _root.layer = layer;
        _root.transform.SetParent(parent, false);
        var filter = _root.AddComponent<MeshFilter>();
        var renderer = _root.AddComponent<MeshRenderer>();
        _mesh = new Mesh { name = name + "-mesh" };
        _mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
        };
        _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        _mesh.RecalculateBounds();
        filter.sharedMesh = _mesh;

        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        if (shader != null)
        {
            _material = new Material(shader) { name = name + "-material", color = Color.black };
            renderer.sharedMaterial = _material;
        }
        else
        {
            renderer.enabled = false;
            Debug.LogWarning("[DualScreen][capability-gap] functional renderer mask shader unavailable");
        }
        renderer.sortingLayerID = 0;
        renderer.sortingOrder = sortingOrder;
    }

    public void SetRect(Rect rect)
    {
        if (_root == null) return;
        _root.transform.localPosition = new Vector3(rect.center.x, rect.center.y, 0f);
        _root.transform.localRotation = Quaternion.identity;
        _root.transform.localScale = new Vector3(Mathf.Max(0f, rect.width),
                                                  Mathf.Max(0f, rect.height), 1f);
    }

    public void Dispose()
    {
        if (_root != null) Object.Destroy(_root);
        if (_material != null) Object.Destroy(_material);
        if (_mesh != null) Object.Destroy(_mesh);
        _root = null;
        _material = null;
        _mesh = null;
    }
}
#endif
