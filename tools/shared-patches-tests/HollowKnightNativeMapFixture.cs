namespace HkPauseContracts;

internal partial class HKDualScreen
{
    readonly List<Renderer> mapHiddenRs=new();
    int mapSetupFails,lastScenesMapped=-1,lastSgStamp=int.MinValue;
    bool lastCompassEquipped,lastMapAvail,compassPending;
    float compassFade=1;
    string lastMapScene="";
    GameMap realMapGm=null;
    System.Reflection.FieldInfo displayingCompassFI,gmapPdFI,gmapGmFI;
    internal bool NativeCurrentMapAvailable=true,NativeAnyMapAvailable=true;
    bool HasAnyMap() => NativeAnyMapAvailable;
    bool HasMapForCurrentZone() => NativeCurrentMapAvailable;
    void SetupQuickMap(GameMap m) { lastMapZone=Manager.Zone;lastMapScene=Manager.sceneName; }
    void MirrorRoomState(GameMap m) { }
    void GateMarkers(GameObject clone) { }
    void SetCompassAlpha(GameMap m,float alpha) { }
    internal void NativeMapTickStep() => MapTickBody();
    internal void NativeMapSettled()
    {
        lastMapZone=Manager.Zone;lastMapScene=Manager.sceneName;
        lastScenesMapped=PlayerData.instance.scenesMapped.Count;lastMapAvail=NativeCurrentMapAvailable;
        lastPinStamp=NativePinStamp;mapClone.SetComponent(mapGm);
    }
    internal void MapRolesStep() => MapRenderRolesTick();
    internal void RetireMapCachesStep() => RetireCompanionCaches();
}
internal sealed partial class GameMap
{
    // Native setup and compass services are explicit engine boundaries. The
    // COMPLETE production MapTick above owns event routing and ordering ingress.
    internal int FullSetups,PinSetups;
    internal bool inRoom=false;
    internal string doorScene="";
    internal GameObject currentScene=null;
    internal Vector3 currentScenePos;
    internal Action<bool> SetupObserver;
    internal void SetupMap(bool pinsOnly=false)
    { if(pinsOnly)PinSetups++;else FullSetups++;SetupObserver?.Invoke(pinsOnly); }
    internal void PositionCompass(bool shade) { }
    internal void LevelReady() { }
}

// Explicit native sorting/TMP engine boundaries, never the production role policy.
internal static class SortingLayer
{
    internal static int NameToID(string name) => name=="Inventory" ? 957720295 : name=="UI" ? 629535577 : name=="Default" ? 0 : throw new InvalidOperationException(name);
    internal static string IDToName(int id) => id==957720295 ? "Inventory" : id==629535577 ? "UI" : id==0 ? "Default" : throw new InvalidOperationException(id.ToString());
    internal static int GetLayerValueFromID(int id) => id==957720295 ? 15 : id==629535577 ? 14 : id==0 ? 0 : throw new InvalidOperationException(id.ToString());
}
internal sealed class MeshSortingOrder
{
    internal string layerName;internal int order;internal Renderer rend;
    public MeshSortingOrder() { }
    // Exact native MeshSortingOrder.Update statements, from pinned Assembly-CSharp.
    internal void Update()
    {
        if(rend.sortingLayerName != layerName) rend.sortingLayerName=layerName;
        if(rend.sortingOrder != order) rend.sortingOrder=order;
    }
}
internal static partial class TMProOld
{
    internal sealed class TextMeshPro:Component
    {
        internal Renderer renderer;internal object fontSharedMaterial;internal bool enabled=true;
        internal TextMeshPro(Renderer owner):base(owner) { renderer=owner; }
        internal void NativeMeshChanged() => TMPro_EventManager.TEXT_CHANGED_EVENT.Call(this);
    }
    internal sealed class TMP_SubMesh:Component
    {
        internal TextMeshPro textComponent;internal Renderer renderer;internal bool enabled;
        internal TMP_SubMesh(Renderer owner):base(owner) { renderer=owner; }
    }
    internal static class TMPro_EventManager
    {
        internal static readonly NativeFastAction<UnityEngine.Object> TEXT_CHANGED_EVENT=new();
    }
}
internal sealed class NativeFastAction<T>
{
    readonly List<Action<T>> handlers=new();
    internal int Count => handlers.Count;
    internal void Add(Action<T> handler) { if(!handlers.Contains(handler)) handlers.Add(handler); }
    internal void Remove(Action<T> handler) => handlers.Remove(handler);
    internal void Call(T arg) { for(int i=0;i<handlers.Count;i++) handlers[i](arg); }
}
