using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppBopIt;

namespace BlindIt
{
    /// <summary>Hooks request a fresh state read; only the per-frame readers speak.</summary>
    internal static class TrackChoicePatch
    {
        [HarmonyPatch(typeof(DeviceContainer), nameof(DeviceContainer.SetDevice))]
        internal static class SetDevice
        {
            private static void Postfix(DeviceType deviceType)
            {
                try
                {
                    Log.Line("track", "device setter observed | requested=" + deviceType);
                    TrackChoiceReader.RequestPoll();
                }
                catch (Exception ex)
                {
                    Log.Line("track", "device hook failed: " + ex.GetType().Name);
                }
            }
        }

        /// <summary>
        /// Leaving to play. The reader goes quiet at once but keeps the screen until
        /// the transition finishes; see TrackChoiceReader.BeginLeaving for why a
        /// plain reset re-announced the choices on the way out.
        /// </summary>
        [HarmonyPatch(typeof(App), nameof(App.GoToGame))]
        internal static class LeavingToGame
        {
            private static void Postfix() => TrackChoiceReader.BeginLeaving();
        }

        /// <summary>
        /// Leaving to the main menu, which is the Backspace the player reported on
        /// 2026-10-01 as re-reading the device and song.
        /// </summary>
        [HarmonyPatch(typeof(App), nameof(App.GoToMainMenu))]
        internal static class LeavingToMenu
        {
            private static void Postfix() => TrackChoiceReader.BeginLeaving();
        }
    }
}

