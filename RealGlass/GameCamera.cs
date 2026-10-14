using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// The game's own camera: the one its CameraController moves and the city is drawn with. The
    /// glass's copies, the water's city reflection and the sky layer hang on it.
    /// </summary>
    /// <remarks>
    /// Not Camera.main, which is the first ENABLED camera tagged MainCamera: a mod that renders the
    /// game's camera by hand takes that away. Lumina's Dynamic Resolution / SSAA (IL of Lumina 1.8.9,
    /// 2026-10-14) disables the game's camera at a city's load, makes an empty camera of its own
    /// (culling mask 0, depth -3) tagged MainCamera, and in that camera's OnRenderImage calls the
    /// game camera's Render() into a texture SSAA times the screen's size. Camera.main is then the
    /// empty one, and copies on it would reflect an empty picture. A camera rendered by hand still
    /// runs its command buffers and Camera.onPreRender, so following the game's camera keeps all of
    /// it working. Where no CameraController exists, Camera.main as before.
    /// </remarks>
    public static class GameCamera
    {
        private static CameraController _controller;
        private static Camera _camera;
        private static float _nextSearch;

        private static Camera _loggedGame, _loggedMain;
        private static bool _logged;

        /// <summary>The game's camera controller, or null where there is none (looked up at most once a second).</summary>
        public static CameraController Controller
        {
            get
            {
                // FindObjectOfType walks every object: not every frame while there is none.
                if (_controller == null && Time.realtimeSinceStartup >= _nextSearch)
                {
                    _nextSearch = Time.realtimeSinceStartup + 1f;
                    _controller = Object.FindObjectOfType<CameraController>();
                    _camera = null;
                }

                return _controller;
            }
        }

        /// <summary>The game's camera, else Camera.main; null when there is neither.</summary>
        public static Camera Get()
        {
            if (_camera == null)
            {
                CameraController controller = Controller;
                if (controller != null)
                    _camera = controller.GetComponent<Camera>();
            }

            Camera main = Camera.main;
            Camera game = _camera != null ? _camera : main;
            if (!_logged || game != _loggedGame || main != _loggedMain)
            {
                // One line a change, and only when it says something: the two differ, or did.
                if (game != main || (_logged && _loggedGame != _loggedMain))
                {
                    Log.Msg(game == main
                        ? "camera: Camera.main is the game's camera '" + Name(game) + "' again"
                        : "camera: the city is drawn by the game's camera '" + Name(game) + "'" +
                          (game != null && !game.enabled ? " (disabled: another mod renders it itself)" : "") +
                          ", while Camera.main is '" + Name(main) + "': the glass and the water follow the game's camera");
                }

                _logged = true;
                _loggedGame = game;
                _loggedMain = main;
            }

            return game;
        }

        private static string Name(Camera camera)
        {
            return camera != null ? camera.name : "none";
        }
    }
}
