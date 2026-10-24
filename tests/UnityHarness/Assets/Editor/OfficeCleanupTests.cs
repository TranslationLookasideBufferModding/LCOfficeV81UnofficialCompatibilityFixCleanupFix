using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

public static class OfficeCleanupTests
{
    const BindingFlags All=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    static Type runtime,systemType,storageType,harmonyType;
    static object harmony;
    static int checks,unregisters,serverRetire,clientRetire;
    static bool server;
    static readonly List<GameObject> objects=new List<GameObject>();
    static string Arg(string key){var a=Environment.GetCommandLineArgs();return a[Array.IndexOf(a,key)+1];}
    public static void Run()
    {
        string managed=Arg("-managedPath"), core=Arg("-corePath"), plugins=Arg("-pluginsPath");
        string[] dlls=Directory.GetFiles(plugins,"*.dll",SearchOption.AllDirectories);
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            foreach(string dir in new[]{core,managed}){string p=Path.Combine(dir,name);if(File.Exists(p))return Assembly.LoadFrom(p);}
            string match=dlls.FirstOrDefault(p=>Path.GetFileName(p)==name);
            return match==null?null:Assembly.LoadFrom(match);
        };
        try
        {
            Assembly office=Assembly.LoadFrom(dlls.Single(p=>Path.GetFileName(p)=="LCOffice.dll"));
            Assembly game=Assembly.LoadFrom(Path.Combine(managed,"Assembly-CSharp.dll"));
            Assembly addon=Assembly.LoadFrom(Arg("-pluginPath"));
            runtime=addon.GetType("LCOfficeCleanup.Runtime",true);
            systemType=office.GetType("LCOffice.Components.ElevatorSystem",true);
            storageType=office.GetType("LCOffice.Components.ElevatorStorage",true);
            Call("Bind");
            Set("Log",new Action<string>(s=>Debug.Log("ADDON "+s)));
            Set("Warn",new Action<string>(s=>Debug.Log("ADDON_WARNING "+s)));
            Set("IsServer",new Func<bool>(()=>server));
            Set("Retire",new Action<GameObject,bool>((g,host)=>{if(host)serverRetire++;else clientRetire++;}));
            Scene scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene,"Assets/TestOffice.unity");
            // Use actual LCOffice classes and actual destroyed Unity transforms, not a C# null stand-in.
            Component storage=Storage("OfficeStorage");
            Transform target=Make("StorageTarget").transform;
            Field(storage,"target",target);
            SetInSystem(storage,true);
            target.position=new Vector3(10,20,30);
            storageType.GetMethod("LateUpdate",All).Invoke(storage,null);
            Check(storage.transform.position==target.position,"original valid storage follows the live Office target");
            Object.DestroyImmediate(target.gameObject);
            bool originalThrows=false;
            try{storageType.GetMethod("LateUpdate",All).Invoke(storage,null);}catch(TargetInvocationException ex){originalThrows=ex.InnerException is MissingReferenceException || ex.InnerException is NullReferenceException;}
            Check(originalThrows,"original storage update reproduces missing/destroyed target exception");
            harmonyType=Assembly.LoadFrom(Path.Combine(core,"0Harmony.dll")).GetType("HarmonyLib.Harmony");
            harmony=Activator.CreateInstance(harmonyType,new object[]{"LCOfficeCleanup.Tests"});
            Patch(systemType.GetMethod("RegisterPathfinding",All),"SkipRegister");
            // Editor fixture teardown has no live NGO session or StartOfRound.
            Patch(systemType.GetMethod("OnDestroy",All),"Skip");
            Type regionType=game.GetType("PlayerPhysicsRegion",true);
            Patch(regionType.GetMethod("OnDestroy",All),"Skip");
            addon.GetType("LCOfficeCleanup.Patches").GetMethod("Install",All).Invoke(null,new[]{harmony});
            Check(PrefixPresent(game.GetType("StartOfRound").GetMethod("ShipLeave",All)),"takeoff cleanup is attached to the real v81 ShipLeave method");
            Check(PrefixPresent(game.GetType("RoundManager").GetMethod("LoadNewLevel",All)),"fallback is attached before real v81 level generation");
            Type startType=game.GetType("StartOfRound",true);
            Component round=Make("RoundFixture").AddComponent(startType);
            startType.GetProperty("Instance",All).SetValue(null,round,null);
            if(startType.GetMethod("OnDestroy",All)!=null)Patch(startType.GetMethod("OnDestroy",All),"Skip");
            Component system=Make("OfficeElevator").AddComponent(systemType);
            Assembly pathfinding=Assembly.LoadFrom(dlls.Single(p=>Path.GetFileName(p)=="PathfindingLib.dll"));
            Type floorType=pathfinding.GetType("PathfindingLib.API.SmartPathfinding.ElevatorFloor",true);
            object floor=Activator.CreateInstance(floorType,new object[]{system,Make("OfficeFloorNode").transform});
            IList floors=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(floorType));floors.Add(floor);
            Field(system,"elevatorFloors",floors);Field(system,"registeredPathfinding",true);
            pathfinding.GetType("PathfindingLib.API.SmartPathfinding.SmartPathfinding").GetMethod("RegisterElevatorFloor",All).Invoke(null,new[]{floor});
            IDictionary registered=(IDictionary)pathfinding.GetType("PathfindingLib.Data.SmartPathLinks").GetField("elevators",All).GetValue(null);
            Check(registered.Contains(system),"fixture registers a real Office floor in the installed PathfindingLib");
            BoxCollider wall=system.gameObject.AddComponent<BoxCollider>();
            system.transform.position=new Vector3(100,0,0);
            GameObject zone=Make("PhysicsRegion");zone.transform.SetParent(system.transform,false);
            BoxCollider trigger=zone.AddComponent<BoxCollider>();trigger.isTrigger=true;
            Component region=zone.AddComponent(regionType);
            Field(region,"physicsCollider",trigger);Field(region,"physicsTransform",zone.transform);
            IList regions=(IList)Activator.CreateInstance(startType.GetField("CurrentPlayerPhysicsRegions",All).FieldType);
            regions.Add(region);Field(round,"CurrentPlayerPhysicsRegions",regions);
            Type playerType=game.GetType("GameNetcodeStuff.PlayerControllerB",true);
            Component player=Make("PlayerInOldPhysicsZone").AddComponent(playerType);
            player.transform.SetParent(zone.transform,false);
            Field(player,"physicsParent",zone.transform);Field(player,"overridePhysicsParent",zone.transform);Field(player,"lastSyncedPhysicsParent",zone.transform);
            BoxCollider playerCollider=player.gameObject.AddComponent<BoxCollider>();
            player.gameObject.layer=2; // Retained player collision must not be mistaken for the removed wall by the ray test.
            Array players=Array.CreateInstance(playerType,1);players.SetValue(player,0);Field(round,"allPlayerScripts",players);
            Component obstacle=system.gameObject.AddComponent(typeof(UnityEngine.AI.NavMeshObstacle));
            Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3(98,0,0),Vector3.right,4),"orphan fixture really blocks a physics ray before cleanup");
            BoxCollider shelf=storage.gameObject.AddComponent<BoxCollider>();
            server=true;
            storageType.GetMethod("LateUpdate",All).Invoke(storage,null);
            Check(!wall.enabled && !shelf.enabled,"destroyed-target guard disables both elevator and storage solid collisions");
            Check(!trigger.enabled && (bool)regionType.GetField("disablePhysicsRegion").GetValue(region),"orphan player physics trigger is made inert");
            Check(!((Behaviour)region).enabled && !((Behaviour)obstacle).enabled,"orphan physics region and navigation obstacle stop updating");
            Check(!regions.Contains(region),"old Office region is removed from the game's active player physics list");
            Check(player!=null && player.transform.parent==null && playerCollider.enabled,"player is detached and survives cleanup with own collision intact");
            Check(playerType.GetField("physicsParent").GetValue(player)==null && playerType.GetField("overridePhysicsParent").GetValue(player)==null && playerType.GetField("lastSyncedPhysicsParent").GetValue(player)==null,"player references to the retired physics transform are cleared");
            Check(!((Behaviour)storage).enabled && !((Behaviour)system).enabled,"orphan Office scripts are stopped");
            Physics.SyncTransforms();
            Check(!Physics.Raycast(new Vector3(98,0,0),Vector3.right,4),"previously blocking invisible wall no longer blocks physics");
            Check(unregisters==1,"stale Office floor links are explicitly unregistered");
            Check(!registered.Contains(system) && !(bool)systemType.GetField("registeredPathfinding",All).GetValue(system),"original LCOffice unregister code removes the real PathfindingLib elevator entry");
            Check(serverRetire==2 && clientRetire==0,"host retires both Office network objects");
            storageType.GetMethod("LateUpdate",All).Invoke(storage,null);
            Check(unregisters==1 && serverRetire==2,"repeated stale update does not repeat cleanup or spam");
            // A fresh, valid Office round must remain functional after an earlier cleanup.
            Component second=Storage("SecondOfficeStorage");
            Transform valid=Make("SecondTarget").transform;valid.position=new Vector3(3,4,5);
            Field(second,"target",valid);SetInSystem(second,true);
            storageType.GetMethod("LateUpdate",All).Invoke(second,null);
            Check(second.transform.position==valid.position && ((Behaviour)second).enabled,"second round retains normal movement and updates");
            Component waiting=Storage("AwaitingSetup");SetInSystem(waiting,false);
            storageType.GetMethod("LateUpdate",All).Invoke(waiting,null);
            Check(((Behaviour)waiting).enabled,"not-yet-initialized storage is not mistaken for an orphan");
            GameObject native=Make("VanillaMineshaftElevator");BoxCollider nativeWall=native.AddComponent<BoxCollider>();
            GameObject deepcore=Make("DeepcoreElevator");BoxCollider deepcoreWall=deepcore.AddComponent<BoxCollider>();
            server=false;
            Call("Cleanup","client takeoff");
            Check(serverRetire==2 && clientRetire>=2,"client cleanup does not request a server despawn");
            Check(!((Behaviour)second).enabled,"client makes old Office objects inert while awaiting authoritative despawn");
            Check(nativeWall.enabled && deepcoreWall.enabled,"unrelated vanilla and Deepcore objects remain untouched");
            // Inactive objects must also be found; prefab assets must never be enumerated.
            Component inactive=Storage("InactiveOrphan");inactive.gameObject.SetActive(false);
            var scan=((Component[])Call("FreshScan")).ToArray();
            Check(scan.Contains(inactive) && scan.All(c=>c.gameObject.scene.IsValid()),"fresh scan includes inactive scene objects and excludes prefab assets");
            Set("PersistentScene",scene);
            Check(((Component[])Call("FreshScan")).Select(c=>c.GetInstanceID()).Distinct().Count()==scan.Length,"scene enumeration does not duplicate the persistent scene");
            // The production network retirement method must leave spawned NGO objects alone on clients.
            Type netType=Assembly.LoadFrom(Path.Combine(managed,"Unity.Netcode.Runtime.dll")).GetType("Unity.Netcode.NetworkObject",true);
            GameObject netRoot=Make("SpawnedNetworkFixture");Component net=netRoot.AddComponent(netType);
            netType.GetProperty("IsSpawned",All).SetValue(net,true,null);
            runtime.GetMethod("RetireNetworkObject",All).Invoke(null,new object[]{netRoot,false});
            Check(netRoot!=null && (bool)netType.GetProperty("IsSpawned",All).GetValue(net,null),"production client branch preserves spawned NetworkObject and server ownership");
            netType.GetProperty("IsSpawned",All).SetValue(net,false,null);
            Component leaving=Storage("TakeoffHookFixture");Transform leaveTarget=Make("StillLiveTarget").transform;
            Field(leaving,"target",leaveTarget);SetInSystem(leaving,true);
            Patch(startType.GetMethod("ShipLeave",All),"Skip");
            startType.GetMethod("ShipLeave",All).Invoke(round,null);
            Check(!((Behaviour)leaving).enabled,"actual patched ShipLeave performs cleanup before target destruction");
            startType.GetProperty("Instance",All).SetValue(null,null,null);
            Debug.Log("OFFICE_CLEANUP_TESTS_OK "+checks+" checks. Multiplayer validation is separate from these checks.");
        }
        catch(Exception ex){Debug.LogError("OFFICE_CLEANUP_TESTS_FAIL "+ex);throw;}
        finally
        {
            foreach(GameObject g in objects.AsEnumerable().Reverse())if(g!=null)Object.DestroyImmediate(g);
            if(harmony!=null)harmonyType.GetMethod("UnpatchSelf").Invoke(harmony,null);
        }
    }
    static object Call(string name,params object[] args){return runtime.GetMethod(name,All).Invoke(null,args);}
    static void Set(string name,object value){runtime.GetField(name,All).SetValue(null,value);}
    static void Field(object o,string name,object value){o.GetType().GetField(name,All).SetValue(o,value);}
    static void SetInSystem(Component c,bool value){storageType.BaseType.GetProperty("InSystem",All).SetValue(c,value,null);}
    static GameObject Make(string name){var g=new GameObject(name);objects.Add(g);return g;}
    static Component Storage(string name){return Make(name).AddComponent(storageType);}
    static void Check(bool ok,string message){if(!ok)throw new Exception("FAIL "+message);checks++;Debug.Log("PASS "+message);}
    public static bool Skip(){return false;}
    public static bool SkipRegister(bool active){if(!active)unregisters++;return true;}
    static void Patch(MethodInfo original,string prefix)
    {
        Type hm=harmonyType.Assembly.GetType("HarmonyLib.HarmonyMethod");
        var hook=Activator.CreateInstance(hm,new object[]{typeof(OfficeCleanupTests).GetMethod(prefix)});
        harmonyType.GetMethods().Single(m=>m.Name=="Patch"&&m.GetParameters().Length==6).Invoke(harmony,new object[]{original,hook,null,null,null,null});
    }
    static bool PrefixPresent(MethodInfo method)
    {
        object info=harmonyType.GetMethod("GetPatchInfo").Invoke(null,new object[]{method});
        return ((IEnumerable)info.GetType().GetField("Prefixes").GetValue(info)).Cast<object>().Any(p=>((MethodInfo)p.GetType().GetProperty("PatchMethod").GetValue(p,null)).DeclaringType.FullName=="LCOfficeCleanup.Patches");
    }
}
