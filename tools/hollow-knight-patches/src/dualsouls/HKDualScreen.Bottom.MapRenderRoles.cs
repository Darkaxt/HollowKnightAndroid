using System;
using System.Collections.Generic;
using UnityEngine;

// A21: the camera-space cubic ramp covers native room art, not annotations.
// Discovery belongs to clone/setup/TMP/marker events; healthy ticks do no walks.
public partial class HKDualScreen
{
    const int MapArtFadeOrder = 4000, MapAnnotationFirstOrder = 5000;
    sealed class MapRenderOrder
    {
        public Renderer Renderer;
        public bool Art;
        public int NativeLayer, NativeLayerValue, NativeOrder;
        public MeshSortingOrder Driver;
        public string NativeDriverLayer;
        public int NativeDriverOrder;
    }
    sealed class MapRenderOrderComparer : IComparer<MapRenderOrder>
    {
        public int Compare(MapRenderOrder a, MapRenderOrder b)
        {
            if (a.Art != b.Art) return a.Art ? -1 : 1;
            int layer = a.NativeLayerValue.CompareTo(b.NativeLayerValue);
            return layer != 0 ? layer : a.NativeOrder.CompareTo(b.NativeOrder);
        }
    }
    static readonly MapRenderOrderComparer mapRenderComparer = new MapRenderOrderComparer();
    Dictionary<Renderer, MapRenderOrder> mapRenderOrders = new Dictionary<Renderer, MapRenderOrder>();
    readonly List<MapRenderOrder> mapRenderRanked = new List<MapRenderOrder>();
    GameObject mapRenderOwner;
    GameMap mapRenderMap;
    int mapRenderUntil = -1, mapRenderFrame = -1;
    Action<UnityEngine.Object> mapRenderTextChanged;
    bool mapRenderEventsBound;

    bool NativeMapRoomArt(Renderer renderer)
    {
        // GameMap.SetupMap treats each immediate child of its fourteen typed
        // area roots as a room; RoughMapRoom changes that room's own sprite.
        // Pins, adjacent-area arrows, text/backboards and TMP submeshes are
        // descendants or separately referenced groups, never those room sprites.
        // Exact resources.assets donor audit: 344 room sprites, 259 other sprites,
        // 111 TMP roots, four fallback meshes and the mesh compass. No name guesses.
        if (!(renderer is SpriteRenderer) || mapGm == null) return false;
        var parent = renderer.transform.parent;
        return NativeMapAreaParent(parent, mapGm.areaAncientBasin) || NativeMapAreaParent(parent, mapGm.areaCity) ||
               NativeMapAreaParent(parent, mapGm.areaCliffs) || NativeMapAreaParent(parent, mapGm.areaCrossroads) ||
               NativeMapAreaParent(parent, mapGm.areaCrystalPeak) || NativeMapAreaParent(parent, mapGm.areaDeepnest) ||
               NativeMapAreaParent(parent, mapGm.areaFogCanyon) || NativeMapAreaParent(parent, mapGm.areaFungalWastes) ||
               NativeMapAreaParent(parent, mapGm.areaGreenpath) || NativeMapAreaParent(parent, mapGm.areaKingdomsEdge) ||
               NativeMapAreaParent(parent, mapGm.areaQueensGardens) || NativeMapAreaParent(parent, mapGm.areaRestingGrounds) ||
               NativeMapAreaParent(parent, mapGm.areaDirtmouth) || NativeMapAreaParent(parent, mapGm.areaWaterways);
    }
    static bool NativeMapAreaParent(Transform parent, GameObject area)
    {
        return area != null && parent == area.transform;
    }
    void RefreshMapRenderRoles()
    {
        if (mapClone == null || mapGm == null) return;
        if (!ReferenceEquals(mapRenderOwner, mapClone) || !ReferenceEquals(mapRenderMap, mapGm))
        {
            TeardownMapRenderRoles();
            mapRenderOwner = mapClone; mapRenderMap = mapGm;
            if (mapRenderTextChanged == null) mapRenderTextChanged = OnMapTextChanged;
            TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Add(mapRenderTextChanged);
            mapRenderEventsBound = true;
        }
        var renderers = mapClone.GetComponentsInChildren<Renderer>(true);
        var current = new Dictionary<Renderer, MapRenderOrder>(renderers.Length);
        mapRenderRanked.Clear();
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i]; if (renderer == null) continue;
            MapRenderOrder entry;
            if (!mapRenderOrders.TryGetValue(renderer, out entry))
            {
                int layer = renderer.sortingLayerID, order = renderer.sortingOrder;
                // Native AddSubTextObject copies its parent's *assigned* sorting.
                // A recreated fallback therefore inherits the cached authored key,
                // not a second application of our band. Existing serialized UI-layer
                // fallback meshes keep their own original key and relative order.
                var submesh = renderer.GetComponent<TMProOld.TMP_SubMesh>();
                var text = submesh != null ? submesh.textComponent as TMProOld.TextMeshPro : null;
                MapRenderOrder parent;
                if (text != null && mapRenderOrders.TryGetValue(text.renderer, out parent) &&
                    renderer.sortingLayerID == text.renderer.sortingLayerID && renderer.sortingOrder == text.renderer.sortingOrder)
                { layer = parent.NativeLayer; order = parent.NativeOrder; }
                var driver = renderer.GetComponent<MeshSortingOrder>();
                entry = new MapRenderOrder { Renderer = renderer, Art = NativeMapRoomArt(renderer),
                    NativeLayer = layer, NativeLayerValue = SortingLayer.GetLayerValueFromID(layer), NativeOrder = order,
                    Driver = driver, NativeDriverLayer = driver != null ? driver.layerName : null,
                    NativeDriverOrder = driver != null ? driver.order : 0 };
            }
            current.Add(renderer, entry); mapRenderRanked.Add(entry);
        }
        mapRenderRanked.Sort(mapRenderComparer);
        int rank = -1; MapRenderOrder previous = null;
        for (int i = 0; i < mapRenderRanked.Count; i++)
        {
            var entry = mapRenderRanked[i];
            if (previous == null || previous.Art != entry.Art) rank = 0;
            else if (previous.NativeLayerValue != entry.NativeLayerValue || previous.NativeOrder != entry.NativeOrder) rank++;
            // Separate bands preserve strict and equal authored within-role keys;
            // all annotations still draw below the outer hard masks at 10000.
            if (rank >= MapArtFadeOrder) throw new InvalidOperationException("native map sorting band exhausted");
            int order = entry.Art ? rank : MapAnnotationFirstOrder + rank;
            entry.Renderer.sortingLayerName = "Inventory"; entry.Renderer.sortingOrder = order;
            // MeshSortingOrder.Update is the native per-frame sorting authority.
            // Synchronize only its sorting fields; do not disable native behaviors.
            if (entry.Driver != null) { entry.Driver.layerName = "Inventory"; entry.Driver.order = order; }
            previous = entry;
        }
        mapRenderOrders = current; mapRenderFrame = Time.frameCount;
    }
    void RequestMapRenderRoles()
    {
        RefreshMapRenderRoles();
        mapRenderUntil = Time.frameCount + 2;
    }
    void OnMapTextChanged(UnityEngine.Object changed)
    {
        var text = changed as TMProOld.TextMeshPro;
        if (text == null || mapClone == null || !ReferenceEquals(mapRenderOwner, mapClone) ||
            !text.transform.IsChildOf(mapClone.transform)) return;
        // Native TMP emits this after rebuilding its fallback meshes. Refresh
        // immediately, then allow two event-bounded late-submesh settle ticks.
        RequestMapRenderRoles();
    }
    void MapRenderRolesTick()
    {
        if (mapClone == null || mapGm == null)
        {
            if (mapRenderOwner != null && !ReferenceEquals(mapRenderOwner, mapClone)) TeardownMapRenderRoles();
            return;
        }
        if (!ReferenceEquals(mapRenderOwner, mapClone) || !ReferenceEquals(mapRenderMap, mapGm)) RequestMapRenderRoles();
        if (Time.frameCount <= mapRenderUntil && mapRenderFrame != Time.frameCount) RefreshMapRenderRoles();
    }
    void TeardownMapRenderRoles()
    {
        if (mapRenderEventsBound) TMProOld.TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(mapRenderTextChanged);
        mapRenderEventsBound = false;
        foreach (var pair in mapRenderOrders)
        {
            var entry = pair.Value;
            if (entry.Renderer != null) { entry.Renderer.sortingLayerID = entry.NativeLayer; entry.Renderer.sortingOrder = entry.NativeOrder; }
            if (entry.Driver != null) { entry.Driver.layerName = entry.NativeDriverLayer; entry.Driver.order = entry.NativeDriverOrder; }
        }
        mapRenderOrders.Clear(); mapRenderRanked.Clear(); mapRenderOwner = null; mapRenderMap = null;
        mapRenderUntil = mapRenderFrame = -1;
    }
}
