// VR-KoboldKare — Module Registry
// Extension registry for VR subsystems. Allows adding new VR features
// without modifying core files. Follows the KK-LLM-NPC pattern.
//
using System;
using System.Collections.Generic;

namespace VRKoboldKare
{
    /// <summary>
    /// Module registry for VR-KoboldKare. Allows subsystems to register
    //  hooks (physics tick, update, perception) that are called by the
    //  core VR loop. Reflection-based discovery — new modules are found
    //  automatically without editing shared files.
    /// </summary>
    internal static class ModuleRegistry
    {
        // Update hook per module (called from VRManager.Update)
        private static readonly List<Action<float>> _updates = new List<Action<float>>();

        // Physics tick hook per module (called from VRManager.FixedUpdate)
        private static readonly List<Action<float>> _physics = new List<Action<float>>();

        // Scene load hook per module (called when a new scene loads)
        private static readonly List<Action<string>> _sceneLoads = new List<Action<string>>();

        private static bool _scanned;

        /// <summary>
        /// Discover every module in this assembly and run its static Register().
        /// Convention: internal/public static class with a parameterless
        //  static Register() that calls into ModuleRegistry.*.
        /// </summary>
        public static void Scan()
        {
            if (_scanned) return;
            _scanned = true;

            System.Type[] types;
            try { types = typeof(ModuleRegistry).Assembly.GetTypes(); }
            catch (Exception) { return; }

            foreach (var t in types)
            {
                if (t.IsInterface || t.IsGenericType) continue;
                var m = t.GetMethod("Register",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, Type.EmptyTypes, null);
                if (m == null) continue;
                try { m.Invoke(null, null); }
                catch (Exception e)
                {
                    try
                    {
                        var lg = VRKoboldKarePlugin.Log;
                        if (lg != null) lg.LogWarning("module scan: " + t.Name + ": " + e.Message);
                    }
                    catch (Exception) { }
                }
            }

            VRKoboldKarePlugin.Log?.LogInfo($"VR: module registry scanned {types.Length} types.");
        }

        // ------------------------------------------------------------------
        // Registration methods
        // ------------------------------------------------------------------

        public static void Update(Action<float> hook)
        {
            lock (_updates) _updates.Add(hook);
        }

        public static void PhysicsTick(Action<float> hook)
        {
            lock (_physics) _physics.Add(hook);
        }

        public static void SceneLoad(Action<string> hook)
        {
            lock (_sceneLoads) _sceneLoads.Add(hook);
        }

        // ------------------------------------------------------------------
        // Invocation (called from VRManager)
        // ------------------------------------------------------------------

        internal static void InvokeUpdate(float deltaTime)
        {
            lock (_updates)
            {
                for (int i = 0; i < _updates.Count; i++)
                {
                    try { _updates[i](deltaTime); }
                    catch (Exception e)
                    {
                        VRKoboldKarePlugin.Log?.LogWarning("module update error: " + e.Message);
                    }
                }
            }
        }

        internal static void InvokePhysics(float deltaTime)
        {
            lock (_physics)
            {
                for (int i = 0; i < _physics.Count; i++)
                {
                    try { _physics[i](deltaTime); }
                    catch (Exception e)
                    {
                        VRKoboldKarePlugin.Log?.LogWarning("module physics error: " + e.Message);
                    }
                }
            }
        }

        internal static void InvokeSceneLoad(string sceneName)
        {
            lock (_sceneLoads)
            {
                for (int i = 0; i < _sceneLoads.Count; i++)
                {
                    try { _sceneLoads[i](sceneName); }
                    catch (Exception e)
                    {
                        VRKoboldKarePlugin.Log?.LogWarning("module scene load error: " + e.Message);
                    }
                }
            }
        }
    }
}
