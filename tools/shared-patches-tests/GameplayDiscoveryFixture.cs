// Typed scene/lifecycle/spawn boundaries for actual tutorial/heal bodies.
// No native timing, Unity lifecycle or particle-rendering proof.
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine.SceneManagement
{
    class SceneData
    {
        public int Handle;public bool Loaded=true;
        public readonly List<GameObject> Roots=new();
    }
    struct Scene
    {
        public SceneData Data;
        public int handle=>Data.Handle;public bool isLoaded=>Data.Loaded;public int rootCount=>Data.Roots.Count;
        public GameObject[] GetRootGameObjects(){SceneManager.RootReads++;return Data.Roots.ToArray();}
        public void GetRootGameObjects(List<GameObject> into){SceneManager.RootReads++;into.Clear();into.AddRange(Data.Roots);}
    }
    static class SceneManager
    {
        public static int RootReads;
        public static readonly List<SceneData> Scenes=new();
        public static int sceneCount=>Scenes.Count;
        public static Scene GetSceneAt(int i)=>new(){Data=Scenes[i]};
    }
}
namespace UnityEngine
{
    enum ParticleSystemSimulationSpace{World,Local}
    class ParticleSystem:Component
    {
        ParticleSystemSimulationSpace space;
        public int Clears,Plays;
        public MainModule main=>new(this);
        public struct MainModule
        {
            readonly ParticleSystem owner;public MainModule(ParticleSystem owner){this.owner=owner;}
            public ParticleSystemSimulationSpace simulationSpace{get=>owner.space;set=>owner.space=value;}
        }
        public void Clear(bool children){Clears++;}public void Play(bool children){Plays++;}
    }
}
class GameCameras:Component{}
partial class HKDualScreen
{
    const int UI_LAYER=5,hudLayer=6,ATTR_LAYER=3;
    Transform creditT;
    GameCameras resolvedGameCameras;
    readonly List<Transform> tutRoots=new();
    readonly Dictionary<Transform,Vector3> healBase=new();
    readonly List<Transform> healDead=new();
    int healFind;
    static bool NameHas(string text,string part)=>text!=null&&text.IndexOf(part,StringComparison.OrdinalIgnoreCase)>=0;
    static Transform Born(Transform parent,string name,int layer=5)
    {
        var go=new GameObject(name){layer=layer};go.transform.SetParent(parent);return go.transform;
    }
    static UnityEngine.SceneManagement.SceneData SceneRoot(int handle,string name)
    {
        var data=new UnityEngine.SceneManagement.SceneData{Handle=handle};data.Roots.Add(new GameObject(name));
        UnityEngine.SceneManagement.SceneManager.Scenes.Add(data);return data;
    }
    public static Dictionary<string,string> RunDiscoveryCases()
    {
        var results=new Dictionary<string,string>();
        void Run(string name,Action<HKDualScreen> test)
        {
            var h=new HKDualScreen();Time.frameCount=100;
            UnityEngine.SceneManagement.SceneManager.Scenes.Clear();GameObject.Resident.Clear();
            try{test(h);results.Add(name,"PASS");}catch(Exception e){results.Add(name,"FAIL: "+e.Message);}
        }
        Run("GP06_tutorial_inventory_settled",h=>
        {
            var scene=SceneRoot(1,"scene-root");var ui=Born(scene.Roots[0].transform,"ui");
            Born(ui,"Focus_Prompt");Born(ui,"Credits");
            for(int i=0;i<40;i++)Born(ui,"resident"+i);
            h.ScanTutorials(7);int names=GameObject.NameReads,roots=UnityEngine.SceneManagement.SceneManager.RootReads,children=Transform.ChildReads;
            for(int i=0;i<3;i++){Time.frameCount+=30;h.ScanTutorials(7);}
            Check(GameObject.NameReads==names&&UnityEngine.SceneManagement.SceneManager.RootReads==roots&&Transform.ChildReads==children,"unchanged thirty-frame tutorial polls rebuild root/name/hierarchy inventories");
            Check(h.tutRoots.Count==1&&h.creditT.gameObject.layer==7,"retained tutorial/credit ownership lost");
        });
        Run("GP06_tutorial_nested_ui_edge",h=>
        {
            var scene=SceneRoot(1,"root");var ui=Born(scene.Roots[0].transform,"ui");var container=Born(ui,"native-container");
            h.ScanTutorials(7);var prompt=Born(container,"attack_tutorial");
            Time.frameCount++;h.ScanTutorials(7);
            Check(prompt.gameObject.layer==7&&h.tutRoots.Contains(prompt),"late nested native prompt not admitted on structural edge");
            var detail=Born(prompt,"late-art");Time.frameCount++;h.ScanTutorials(7);
            Check(detail.gameObject.layer==7,"late art under a known prompt was not routed");
        });
        Run("GP06_tutorial_scene_identity_and_load",h=>
        {
            SceneRoot(1,"original");h.ScanTutorials(7);
            UnityEngine.SceneManagement.SceneManager.Scenes.Clear();var replacement=SceneRoot(2,"same-count-new-scene");
            replacement.Loaded=false;var tut=Born(replacement.Roots[0].transform,"Tutorial");h.ScanTutorials(7);
            Check(tut.gameObject.layer==5,"unloaded scene was scanned");replacement.Loaded=true;h.ScanTutorials(7);
            Check(tut.gameObject.layer==7,"same-count scene/load edge was missed");
        });
        Run("GP06_tutorial_root_and_persistent_replacement",h=>
        {
            var scene=SceneRoot(1,"root");h.ScanTutorials(7);
            var extra=new GameObject("Tutorial"){layer=5};scene.Roots.Add(extra);h.ScanTutorials(7);
            Check(extra.layer==7,"new scene root missed");
            var persistent=new GameObject("DontDestroyOnLoad");h.resolvedGameCameras=Born(persistent.transform,"GameCameras").gameObject.AddComponent<GameCameras>();
            var focus=Born(h.resolvedGameCameras.transform,"focus_prompt");h.ScanTutorials(7);
            Check(focus.gameObject.layer==7,"persistent-root replacement missed");
            var late=Born(h.resolvedGameCameras.transform,"tutorial_late");h.ScanTutorials(7);Check(late.gameObject.layer==7,"late persistent prompt missed");
        });
        Run("GP06_tutorial_retirement_and_restore",h=>
        {
            var scene=SceneRoot(1,"root");var prompt=Born(scene.Roots[0].transform,"tutorial");h.ScanTutorials(7);
            prompt.gameObject.layer=5;h.ScanTutorials(7);Check(prompt.gameObject.layer==7,"native/transport restoration did not rearm routing");
            prompt.Retired=true;h.ScanTutorials(7);Check(!h.tutRoots.Contains(prompt),"retired tutorial root retained");
            var next=Born(scene.Roots[0].transform,"Tutorial2");h.ScanTutorials(6);Check(next.gameObject.layer==6,"new target layer not applied");
        });
        Run("GP06_tutorial_restore_rebind_and_watch_retirement",h=>
        {
            var scene=SceneRoot(1,"root");var tut=Born(scene.Roots[0].transform,"Tutorial");var credits=Born(scene.Roots[0].transform,"Credits");
            h.ScanTutorials(7);h.RestoreRoutedLayers();
            Check(tut.gameObject.layer==5&&credits.gameObject.layer==5,"original layers not restored");
            h.ScanTutorials(7);Check(tut.gameObject.layer==7&&credits.gameObject.layer==7,"transport recovery did not route retained owners");
            h.RestoreRoutedLayers();
            // Reflect only the optional original-vs-candidate engine component type;
            // every actual watch present must detach, no manufactured owner state.
            int watches=0;
            foreach(var go in GameObject.Resident)foreach(var c in go.GetComponents<MonoBehaviour>())
                if(c.GetType().Name=="TutorialInventoryWatch")
                {
                    watches++;
                    Check(c.GetType().GetField("Owner",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(c)==null,"retired transport retained tutorial watch owner");
                }
            Check(watches>0,"structural watch engine boundary was not exercised");
        });
        Run("GP06_tutorial_unnotified_late_name_fallback",h=>
        {
            var scene=SceneRoot(1,"root");var node=Born(scene.Roots[0].transform,"ordinary");h.ScanTutorials(7);
            node.gameObject.name="Tutorial renamed";Time.frameCount+=120;h.ScanTutorials(7);
            Check(node.gameObject.layer==7,"unnotified native name change never recovered");
        });
        Run("GP06_heal_multi_clone_rename_and_pin",h=>
        {
            var first=new GameObject("HP Up Particles(Clone)");first.transform.position=new Vector3(10,20,0);var p=first.AddComponent<ParticleSystem>();
            h.RouteHealParticles();Check(first.name=="HP Up Particles(HKDS)"&&first.layer==7&&p.main.simulationSpace==ParticleSystemSimulationSpace.Local,"heal routing/rename/local emission changed");
            var second=new GameObject("HP Up Particles(Clone)");second.AddComponent<ParticleSystem>();
            int scans=GameObject.Finds;for(int i=0;i<5;i++){first.transform.position=new Vector3(999,999,0);h.RouteHealParticles();Check(first.transform.position.x==12&&first.transform.position.y==23,"tracked clone not pinned every frame");}
            Check(GameObject.Finds==scans,"heal fallback no longer six frames");h.RouteHealParticles();
            Check(second.layer==7&&h.healBase.Count==2&&p.Clears==1&&p.Plays==1,"renamed first clone blocked new clone or emission was repeated");
            first.transform.Retired=true;h.RouteHealParticles();Check(h.healBase.Count==1,"dead heal clone not pruned");
        });
        return results;
    }
}
