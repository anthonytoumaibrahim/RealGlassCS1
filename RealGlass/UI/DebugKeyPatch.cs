using System;
using System.Reflection;
using ColossalFramework;
using HarmonyLib;

namespace RealGlass.UI
{
    /// <summary>
    /// Keeps the game's debug-output panel from opening on the same key as ours.
    /// </summary>
    /// <remarks>
    /// F7 was asked for as this mod's key, and F7 is also the game's own: read from the IL
    /// (2026-09-28), DefaultSettings.debugOuput = F7 (KeyCode 288), stored under the key name
    /// Settings.debugOuput in Settings.inputSettingsFile, and DebugOutputPanel.Update opens and
    /// closes the debug panel on <c>m_DebugOuput.IsKeyUp()</c>. Without this, F7 in a city opens
    /// both panels.
    ///
    /// The prefix skips that one Update -- the frame the shared key is released -- when:
    /// "Keep the game's debug panel off this key" is on (Settings.QuietDebugKey), our key's
    /// encoding equals the game's debug key's (the player may have moved either), and a city is
    /// loaded (ModController exists: our panel can open). At the main menu, where our panel does
    /// not exist, F7 still opens the game's panel. The Update it skips also copies pending messages
    /// into the debug panel; the next frame does that. Nothing is ever written to the game's key.
    ///
    /// <c>___m_DebugOuput</c> is Harmony's private-field injection (read only). Never
    /// <c>__args</c>: CitiesHarmony's Harmony 2.0.4 writes that array back (see Patcher).
    ///
    /// The target comes from TargetMethod, not from the attribute: in the game (2026-09-28),
    /// <c>[HarmonyPatch(typeof(DebugOutputPanel), "Update")]</c> -- a private method named with no
    /// argument types -- failed with "Undefined target method" although the same Harmony 2.0.4
    /// resolves it under .NET; under the game's Mono only the name-only lookup was untried.
    /// </remarks>
    [HarmonyPatch]
    internal static class DebugKeyPatch
    {
        private static int _loggedKey = -1;
        private static bool _loggedClash;
        private static bool _loggedQuiet;
        private static bool _failed;

        /// <summary>DebugOutputPanel's private, parameterless Update, by its full signature.</summary>
        private static MethodBase TargetMethod()
        {
            MethodBase update = typeof(DebugOutputPanel).GetMethod("Update",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (update == null)
                Log.Warn("debug key: DebugOutputPanel.Update() not found; the game's debug panel keeps its key as it is");

            return update;
        }

        private static bool Prefix(SavedInputKey ___m_DebugOuput)
        {
            try
            {
                SavedInputKey ours = Settings.PanelKey;
                if (ours == null || ___m_DebugOuput == null)
                    return true;

                int mine = ours.value;
                int theirs = ___m_DebugOuput.value;
                bool clash = (mine & KeyMask) != 0 && mine == theirs;
                bool quiet = Settings.QuietDebugKey == null || Settings.QuietDebugKey.value;

                Note(mine, clash, quiet);

                if (!clash || !quiet || ModController.Instance == null)
                    return true;

                // The game's own test, on the game's own key: skip exactly the frame it would toggle on.
                return !___m_DebugOuput.IsKeyUp();
            }
            catch (Exception e)
            {
                // Once: this runs every frame. The game's panel then works as it always did.
                if (!_failed)
                {
                    _failed = true;
                    Log.Error("debug key: the check failed; the game's debug panel keeps its key as it is", e);
                }

                return true;
            }
        }

        /// <summary>SavedInputKey's key bits (the rest are Ctrl/Shift/Alt), read from ColossalManaged's IL.</summary>
        private const int KeyMask = 0x0FFFFFFF;

        /// <summary>One line whenever what happens on our key changes: the key, the clash, the switch.</summary>
        private static void Note(int key, bool clash, bool quiet)
        {
            if (key == _loggedKey && clash == _loggedClash && quiet == _loggedQuiet)
                return;

            _loggedKey = key;
            _loggedClash = clash;
            _loggedQuiet = quiet;

            string name = SettingsXmlFormat.FormatKey(key);
            if (!clash)
                Log.Msg("debug key: the panel key " + name + " is not the game's debug-output key; nothing to keep apart");
            else if (quiet)
                Log.Msg("debug key: the panel key " + name + " is also the game's debug-output key; in a city it opens only " +
                        Mod.DisplayName + "'s panel (Keep the game's debug panel off this key: on)");
            else
                Log.Msg("debug key: the panel key " + name + " is also the game's debug-output key, and the switch is off: " +
                        "it opens BOTH panels");
        }

        /// <summary>
        /// Says the same without waiting for the game's panel to update: at a city's start and when
        /// the switch changes. Reads the game's key from its settings file (the debug panel may not
        /// exist yet).
        /// </summary>
        public static void Describe()
        {
            try
            {
                SavedInputKey ours = Settings.PanelKey;
                if (ours == null)
                    return;

                // Read only, as SystemReport reads the game's graphics levels: nothing is written.
                int theirs = new SavedInputKey(global::Settings.debugOuput, global::Settings.inputSettingsFile,
                    DefaultSettings.debugOuput, false).value;
                int mine = ours.value;
                bool clash = (mine & KeyMask) != 0 && mine == theirs;
                bool quiet = Settings.QuietDebugKey == null || Settings.QuietDebugKey.value;
                Note(mine, clash, quiet);
            }
            catch (Exception e)
            {
                Log.Warn("debug key: could not read the game's debug-output key: " + e.Message);
            }
        }
    }
}
