using System;
using System.IO;
using UnityEngine;

namespace RealGlass
{
    /// <summary>
    /// Writes to a dedicated file next to the game's user data, so diagnostics can be read
    /// without digging through a multi-megabyte output_log. Everything also goes to the Unity log
    /// so it still shows up in ModTools.
    /// </summary>
    public static class Log
    {
        private const string FileName = "RealGlass.log";

        private static readonly object Sync = new object();

        private static string _path;
        private static bool _disabled;

        /// <summary>
        /// True from <see cref="Start"/> on. Before it, nothing goes to the FILE: the Content Manager
        /// reads Mod.Description of a DISABLED mod too, and the localization line that lookup logs
        /// would be appended to the previous session's file, one stray line per launch (audit,
        /// 2026-10-11). The Unity log gets every line regardless.
        /// </summary>
        private static bool _started;

        /// <summary>
        /// Next to RealGlass.xml, in the folder the game itself uses: on Windows and Linux the
        /// .NET LocalApplicationData folder (with XDG_DATA_HOME honoured on Linux), on a Mac
        /// ~/Library/Application Support -- where .NET's answer is ~/.local/share, a folder that
        /// does not exist there (Volumetric Weather wrote no log on a Mac until it asked the game).
        /// </summary>
        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    string dir = null;
                    try
                    {
                        dir = GameDataFolder();
                    }
                    catch (Exception)
                    {
                        // Outside the game (an offline test loading this DLL), or a game API
                        // change: the .NET folder is what every Windows player gets anyway.
                    }

                    if (string.IsNullOrEmpty(dir))
                    {
                        dir = System.IO.Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            System.IO.Path.Combine("Colossal Order", "Cities_Skylines"));
                    }

                    _path = System.IO.Path.Combine(dir, FileName);
                }

                return _path;
            }
        }

        /// <summary>
        /// Its own method so that a missing ColossalManaged.dll (offline) surfaces as an exception
        /// at the call, inside the try above, rather than when the caller is compiled. The game's
        /// getter also creates the folder when it is missing.
        /// </summary>
        private static string GameDataFolder()
        {
            return ColossalFramework.IO.DataLocation.localApplicationData;
        }

        /// <summary>Starts a fresh file for this session.</summary>
        public static void Start()
        {
            lock (Sync)
            {
                _disabled = false;
                _started = true;

                try
                {
                    string dir = System.IO.Path.GetDirectoryName(Path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    File.WriteAllText(Path,
                        Mod.DisplayName + " " + Mod.Version + " (build " +
                        typeof(Mod).Assembly.GetName().Version + ") log - session started " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
                }
                catch (Exception e)
                {
                    _disabled = true;
                    Debug.LogWarning("[RealGlass] File logging unavailable: " + e.Message);
                }
            }
        }

        public static void Msg(string message)
        {
            Debug.Log("[RealGlass] " + message);
            Write("INFO ", message);
        }

        /// <summary>
        /// True while the player has asked for the repeating diagnostic lines. Test it before
        /// BUILDING a detail string, or the concatenation is paid for whatever the switch says.
        /// </summary>
        public static bool Detailed
        {
            get { return Settings.DetailedLogging != null && Settings.DetailedLogging.value; }
        }

        /// <summary>
        /// A line that repeats: the per-second and per-five-second diagnostics this mod is
        /// developed against. Silent unless "Detailed logging" is on, and never goes to the Unity
        /// log, which follows every entry with a stack trace.
        /// </summary>
        /// <remarks>
        /// What is NOT allowed in here: anything that only happens once, any warning or error, and
        /// the state of anything that can suppress or replace rendering. A quiet log must still
        /// explain a black screen: a debug switch once left saved as on silently removed every
        /// vehicle light in Volumetric Weather, session after session, until a log line gave it
        /// away.
        /// </remarks>
        public static void Detail(string message)
        {
            if (!Detailed)
                return;

            Write("DEBUG", message);
        }

        /// <summary>
        /// Says whether the repeating lines are on, so a pasted log explains its own silence.
        /// Called once the settings exist, which is after <see cref="Start"/>.
        /// </summary>
        public static void ReportLevel()
        {
            Msg("detailed logging is " + (Detailed ? "ON" : "off") +
                (Detailed ? "" : " -- tick it on the mod's options page before reporting a problem"));
        }

        public static void Warn(string message)
        {
            Debug.LogWarning("[RealGlass] " + message);
            Write("WARN ", message);
        }

        public static void Error(string message, Exception e = null)
        {
            Debug.LogError("[RealGlass] " + message);
            if (e != null)
                Debug.LogException(e);

            Write("ERROR", message + (e == null ? string.Empty : Environment.NewLine + e));
        }

        private static void Write(string level, string message)
        {
            if (_disabled || !_started)
                return;

            lock (Sync)
            {
                try
                {
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("HH:mm:ss") + " " + level + " " + message + Environment.NewLine);
                }
                catch (Exception)
                {
                    // A locked or unwritable file shouldn't take the mod down with it.
                    _disabled = true;
                }
            }
        }
    }
}
