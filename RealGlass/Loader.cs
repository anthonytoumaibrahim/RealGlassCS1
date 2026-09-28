using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// Discovered automatically by the game alongside <see cref="Mod"/>. Owns the controller
    /// GameObject for the lifetime of a loaded city.
    /// </summary>
    public class Loader : LoadingExtensionBase
    {
        private static GameObject _root;
        private static bool _unsupportedShown;

        /// <summary>
        /// True once a city has stood down in this launch. A static: the machine does not change
        /// between cities.
        /// </summary>
        public static bool HasStoodDown { get; private set; }

        public override void OnLevelLoaded(LoadMode mode)
        {
            base.OnLevelLoaded(mode);

            // Cities only: a new game, a save, a scenario. Not the map, asset or theme editors.
            if (mode != LoadMode.NewGame && mode != LoadMode.LoadGame && mode != LoadMode.NewGameFromScenario)
                return;

            if (_root != null)
                return;

            if (!CanDrawGlass())
            {
                StandDown();
                return;
            }

            _root = new GameObject("RealGlassController");
            Object.DontDestroyOnLoad(_root);
            _root.AddComponent<ModController>();

            Debug.Log("[RealGlass] Controller created.");
        }

        public override void OnLevelUnloading()
        {
            base.OnLevelUnloading();

            if (_root == null)
                return;

            Object.Destroy(_root);
            _root = null;
        }

        /// <summary>
        /// The glass is drawn by shaders from the bundle built for this platform: Direct3D 11 or
        /// OpenGL Core on Windows, Metal or OpenGL Core on a Mac, OpenGL Core on Linux
        /// (<see cref="ShaderBundle"/>). Where the game runs on anything else, or the card cannot
        /// run shader model 3.5 / OpenGL 3.3, the glass shader is unsupported and ShaderBundle
        /// returns null: then nothing starts at all.
        /// </summary>
        private static bool CanDrawGlass()
        {
            return ShaderBundle.Get(ShaderBundle.Glass) != null;
        }

        /// <summary>
        /// No controller means nothing of ours runs in this city: no glass, no panel or button;
        /// the buildings are the game's. The Harmony patches applied in OnEnabled are inert
        /// without it.
        /// </summary>
        private static void StandDown()
        {
            HasStoodDown = true;

            // Always on: SystemReport, which would have named the machine, belongs to the
            // controller that is not being created.
            Log.Msg("NOT SUPPORTED here: the glass shader cannot run (" + SystemInfo.operatingSystem +
                    ", graphics " + SystemInfo.graphicsDeviceType + ", GPU '" + SystemInfo.graphicsDeviceName +
                    "'); the mod needs Direct3D 11 or OpenGL 3.3 on Windows, Metal or OpenGL 3.3 on a Mac, " +
                    "OpenGL 3.3 on Linux, and stays off in this city -- nothing of it runs");

            // Once per launch: every city load would be nagging.
            if (_unsupportedShown)
                return;

            _unsupportedShown = true;

            // Three texts, as Volumetric Weather's: Windows names the log to send (a real bug is
            // the one case worth a report); Linux says what the mod needs and the way out through
            // Proton; a Mac, or anything else, says what the mod needs.
            try
            {
                string message;
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsPlayer:
                    case RuntimePlatform.WindowsEditor:
                        message = Localization.Get("Mod.UnsupportedWindows", Log.Path);
                        break;
                    case RuntimePlatform.LinuxPlayer:
                    case RuntimePlatform.LinuxEditor:
                        message = Localization.Get("Mod.UnsupportedLinux");
                        break;
                    default:
                        message = Localization.Get("Mod.Unsupported");
                        break;
                }

                ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                panel.SetMessage(Mod.DisplayName, message, false);
            }
            catch (System.Exception e)
            {
                Log.Error("Could not show the 'not supported' message.", e);
            }
        }
    }
}
