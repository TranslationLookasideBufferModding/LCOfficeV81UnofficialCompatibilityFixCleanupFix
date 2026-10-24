using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]

namespace LCOfficeCleanup
{
    [BepInPlugin(Id, "LC Office Cleanup", "0.1.0")]
    [BepInDependency("Piggy.LCOffice")]
    [BepInDependency("lcoffice.v81compat.runtimepatch")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Id = "TranslationLookasideBufferModding.LCOfficeCleanup";
        private void Awake()
        {
            Runtime.Log = s => Logger.LogInfo(s);
            Runtime.Warn = s => Logger.LogWarning(s);
            Runtime.PersistentScene = gameObject.scene;
            try
            {
                Runtime.Bind();
                Patches.Install(new Harmony(Id));
                Logger.LogInfo("LC Office Cleanup 0.1.0 active: takeoff cleanup, pre-generation fallback and orphan guard.");
            }
            catch (Exception ex) { Logger.LogError("LC Office Cleanup could not bind; no successful installation: " + ex); }
        }
    }

    internal static class Runtime
    {
        internal const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        internal static Type SystemType, StorageType;
        internal static FieldInfo StorageTarget;
        internal static PropertyInfo InSystem;
        internal static MethodInfo Unregister;
        internal static Scene PersistentScene;
        internal static Action<string> Log = s => { };
        internal static Action<string> Warn = s => { };
        internal static Func<bool> IsServer = () => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        internal static Action<GameObject, bool> Retire = RetireNetworkObject;
        private static readonly HashSet<int> retired = new HashSet<int>();
        private static bool cleaning;

        internal static void Bind()
        {
            SystemType = AccessTools.TypeByName("LCOffice.Components.ElevatorSystem");
            StorageType = AccessTools.TypeByName("LCOffice.Components.ElevatorStorage");
            if (SystemType == null || StorageType == null) throw new MissingMemberException("LCOffice elevator types");
            StorageTarget = AccessTools.Field(StorageType, "target");
            InSystem = AccessTools.Property(StorageType, "InSystem");
            Unregister = AccessTools.Method(SystemType, "RegisterPathfinding", new[] { typeof(bool) });
            if (StorageTarget == null || InSystem == null || Unregister == null)
                throw new MissingMemberException("LCOffice 2.3.4 lifetime members");
        }

        // Traverse loaded scene roots directly. This deliberately avoids cached global FindObject calls.
        // The plugin's scene includes DontDestroyOnLoad objects created by Spawn(false).
        internal static Component[] FreshScan()
        {
            var result = new List<Component>();
            var seen = new HashSet<int>();
            var scenes = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++) scenes.Add(SceneManager.GetSceneAt(i));
            if (PersistentScene.IsValid() && !scenes.Contains(PersistentScene)) scenes.Add(PersistentScene);
            foreach (Scene scene in scenes)
            {
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Type type in new[] { SystemType, StorageType })
                    {
                        foreach (Component component in root.GetComponentsInChildren(type, true))
                            if (component != null && seen.Add(component.GetInstanceID())) result.Add(component);
                    }
                }
            }
            return result.ToArray();
        }

        internal static bool StorageUpdate(Component storage)
        {
            if (storage == null) return false;
            if (retired.Contains(storage.GetInstanceID())) return false;
            // Setup has not run yet: preserve the original initialization coroutine and update.
            if (!(bool)InSystem.GetValue(storage, null)) return true;
            Transform target = (Transform)StorageTarget.GetValue(storage);
            if (target != null) return true; // Unity's destroyed-object semantics are essential here.
            Cleanup("destroyed Office storage target");
            return false;
        }

        internal static void Cleanup(string reason)
        {
            if (cleaning) return;
            cleaning = true;
            try
            {
                Component[] components = FreshScan();
                bool server = IsServer();
                int count = 0;
                foreach (Component component in components)
                {
                    if (component == null) continue;
                    bool first = retired.Add(component.GetInstanceID());
                    if (first)
                    {
                        // Unregister while the object still exists, before its floor transforms disappear.
                        if (SystemType.IsInstanceOfType(component))
                        {
                            try { Unregister.Invoke(component, new object[] { false }); }
                            catch (Exception ex) { Warn("Could not unregister Office pathfinding: " + ex.GetBaseException().Message); }
                        }
                        Quarantine(component.gameObject);
                        count++;
                    }
                    // A client makes stale physics inert, but only the server can despawn network objects.
                    // Retrying retirement at the next lifecycle hook is safe if a previous despawn failed.
                    try { Retire(component.gameObject, server); }
                    catch (Exception ex) { Warn("Office despawn failed; stale physics remains disabled: " + ex.GetBaseException().Message); }
                }
                if (count > 0) Log("Cleaned " + count + " Office elevator object(s): " + reason + "; server=" + server + ".");
                // Keep references bounded over long sessions, without forgetting still-live quarantined objects.
                var live = new HashSet<int>();
                foreach (Component component in components) if (component != null) live.Add(component.GetInstanceID());
                retired.IntersectWith(live);
            }
            finally { cleaning = false; }
        }

        internal static void Quarantine(GameObject root)
        {
            StartOfRound round = StartOfRound.Instance;
            // Detach players before destroying an old elevator, never destroy player objects as children.
            if (round != null && round.allPlayerScripts != null)
            {
                foreach (var player in round.allPlayerScripts)
                {
                    if (player == null) continue;
                    if (player.transform.IsChildOf(root.transform)) player.transform.SetParent(null, true);
                    if (Belongs(player.physicsParent, root)) player.physicsParent = null;
                    if (Belongs(player.overridePhysicsParent, root)) player.overridePhysicsParent = null;
                    if (Belongs(player.lastSyncedPhysicsParent, root)) player.lastSyncedPhysicsParent = null;
                }
            }
            foreach (PlayerPhysicsRegion region in root.GetComponentsInChildren<PlayerPhysicsRegion>(true))
            {
                region.disablePhysicsRegion = true;
                if (round != null && round.CurrentPlayerPhysicsRegions != null) round.CurrentPlayerPhysicsRegions.Remove(region);
                region.enabled = false;
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                // Items can be parented to the storage shelves; retain their own collision.
                if (collider.GetComponentInParent<GrabbableObject>() == null) collider.enabled = false;
            }
            foreach (NavMeshObstacle obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true)) obstacle.enabled = false;
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().FullName == "Unity.AI.Navigation.NavMeshSurface")
                {
                    behaviour.enabled = false;
                    continue;
                }
                if (behaviour.GetType().Assembly != SystemType.Assembly) continue;
                behaviour.StopAllCoroutines();
                behaviour.enabled = false;
            }
        }

        private static bool Belongs(Transform transform, GameObject root)
        { return transform != null && transform.IsChildOf(root.transform); }

        private static void RetireNetworkObject(GameObject root, bool server)
        {
            NetworkObject network = root.GetComponent<NetworkObject>();
            if (network != null && network.IsSpawned)
            {
                if (server) network.Despawn(true);
                return;
            }
            Object.Destroy(root);
        }
    }

    internal static class Patches
    {
        internal static void Install(Harmony harmony)
        {
            // Same phase as LethalLevelLoader's onShipLeave event used by the original LCOffice.Awake.
            MethodInfo leave = AccessTools.Method(typeof(StartOfRound), "ShipLeave");
            MethodInfo load = AccessTools.Method(typeof(RoundManager), "LoadNewLevel");
            MethodInfo update = AccessTools.Method(Runtime.StorageType, "LateUpdate");
            if (leave == null || load == null || update == null) throw new MissingMethodException("Expected v81 lifecycle methods");
            harmony.Patch(leave, prefix: new HarmonyMethod(typeof(Patches), "BeforeLeave"));
            harmony.Patch(load, prefix: new HarmonyMethod(typeof(Patches), "BeforeLoad"));
            harmony.Patch(update, prefix: new HarmonyMethod(typeof(Patches), "StoragePrefix"));
        }
        private static void BeforeLeave() { Runtime.Cleanup("ship leaving"); }
        private static void BeforeLoad() { Runtime.Cleanup("before next level generation"); }
        private static bool StoragePrefix(Component __instance) { return Runtime.StorageUpdate(__instance); }
    }
}
