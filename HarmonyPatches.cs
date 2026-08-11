using System.Reflection;
using HarmonyLib;
using SFS.World.Terrain;

namespace SplitScreenControl
{
    [HarmonyPatch(typeof(DynamicTerrain), "SetViewPosition")]
    internal static class DynamicTerrain_SetViewPosition_RefreshLod
    {
        private static readonly MethodInfo UpdateMethod =
            typeof(DynamicTerrain).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Postfix(DynamicTerrain __instance)
        {
            if (!SplitScreenController.IsSplitActive) return;
            if (UpdateMethod == null) return;

            UpdateMethod.Invoke(__instance, null);
        }
    }
}
