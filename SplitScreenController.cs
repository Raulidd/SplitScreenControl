using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ModLoader;
using SFS.World;
using HarmonyLib;

namespace SplitScreenControl
{
    // =====================================================================
    //  Mod entry point (SFS official API: ModLoader.Mod)
    // =====================================================================
    public class SplitScreenMod : Mod
    {
        public override string ModNameID => "splitscreencontrol";
        public override string DisplayName => "Split Screen Control";
        public override string Author => "Rauli";
        public override string MinimumGameVersionNecessary => "1.6.0";
        public override string ModVersion => "0.1.0";
        public override string Description =>
            "Press ALT+C to split the screen and control two rockets at once. " +
            "Click on the half of the screen showing the rocket you want to control.";

        public override void Load()
        {
            GameObject controllerObject = new GameObject("SplitScreenController_Root");
            Object.DontDestroyOnLoad(controllerObject);
            controllerObject.AddComponent<SplitScreenController>();

            new Harmony("com.splitscreencontrol.mod").PatchAll(typeof(SplitScreenMod).Assembly);
        }
    }

    // =====================================================================
    //  Main logic: ALT+C toggle, secondary camera, selection menu, and
    //  click-to-control.
    // =====================================================================
    public class SplitScreenController : MonoBehaviour
    {
        // ---- General state ----
        private bool splitActive = false;
        private bool selectionMenuOpen = false;

        public static bool IsSplitActive { get; private set; }

        // Rocket "A" is whichever rocket was already being controlled
        // before split screen was activated.
        // Rocket "B" is the rocket the player picks from the popup menu.
        private Rocket rocketA;
        private Rocket rocketB;
        private List<Rocket> selectableRockets = new List<Rocket>();

        // Secondary Camera
        private Camera secondaryCamera;

        private GUIStyle windowStyle;
        private GUIStyle buttonStyle;
        private GUIStyle titleStyle;
        private Rect menuRect = new Rect(0, 0, 440, 60);

        private const bool EnableFloatingOriginRecenterFix = true;

        private bool originShiftedForRender = false;
        private Location viewLocationToRestore;

        private bool originRecenterFixIsWorking = false;

        private void Awake()
        {
            Camera.onPreRender += OnCameraPreRender;
            Camera.onPostRender += OnCameraPostRender;
        }

        private void OnDestroy()
        {
            Camera.onPreRender -= OnCameraPreRender;
            Camera.onPostRender -= OnCameraPostRender;
        }

        private void Update()
        {
            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (altHeld && Input.GetKeyDown(KeyCode.C))
            {
                ToggleSplitScreen();
            }

            if (!splitActive || selectionMenuOpen)
                return;

            if (rocketA == null || rocketB == null)
            {
                StopSplitScreen();
                return;
            }

            if (IsMapOpen())
                return;

            if (Input.GetMouseButtonDown(0))
            {
                HandleClickToControl();
            }
        }

        private void LateUpdate()
        {
            if (!splitActive || selectionMenuOpen)
                return;

            if (IsMapOpen())
            {
                SuspendSplitScreenForMap();
                return;
            }

            if (secondaryCamera != null)
                secondaryCamera.enabled = true;

            ForceBuildingsLoaded();
            UpdateSecondaryCamera();
            UpdateViewportRects();
        }

        private void SuspendSplitScreenForMap()
        {
            if (secondaryCamera != null)
                secondaryCamera.enabled = false;

            Camera activeGameCam = GetActiveGameCamera();
            if (activeGameCam != null)
                activeGameCam.rect = new Rect(0f, 0f, 1f, 1f);
        }

        private bool IsMapOpen()
        {
            object mapManager = SafeAccess.GetStaticRaw("SFS.World.Maps.Map, Assembly-CSharp", "manager");
            if (mapManager == null)
                return false;

            object mapMode = SafeAccess.GetRaw(mapManager, "mapMode");
            return SafeAccess.GetBool(mapMode, "Value", false);
        }

        private void OnGUI()
        {
            if (!selectionMenuOpen)
                return;

            if (windowStyle == null)
            {
                windowStyle = new GUIStyle(GUI.skin.window);
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16 };
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
            }

            menuRect.height = 64 + selectableRockets.Count * 42;
            menuRect.x = (Screen.width - menuRect.width) / 2f;
            menuRect.y = (Screen.height - menuRect.height) / 2f;

            menuRect = GUI.Window(837462, menuRect, DrawSelectionWindow,
                "Choose the rocket for split screen", windowStyle);
        }

        private void DrawSelectionWindow(int id)
        {
            GUILayout.Space(20);
            GUILayout.Label("This rocket will appear on the right half of the screen", titleStyle);
            GUILayout.Space(10);

            foreach (Rocket rocket in selectableRockets)
            {
                if (rocket == null) continue;

                Rocket capturedRocket = rocket;
                string label = string.IsNullOrEmpty(capturedRocket.rocketName)
                    ? "Unnamed rocket"
                    : capturedRocket.rocketName;

                if (GUILayout.Button(label, buttonStyle, GUILayout.Height(36)))
                {
                    ConfirmRocketSelection(capturedRocket);
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Cancel", buttonStyle, GUILayout.Height(30)))
            {
                CancelSelection();
            }
        }

        // ---------------------------------------------------------------
        //  Turn split screen mode on / off
        // ---------------------------------------------------------------
        private void ToggleSplitScreen()
        {
            if (!splitActive && !selectionMenuOpen)
            {
                TryStartSplitScreen();
            }
            else
            {
                StopSplitScreen();
            }
        }

        private void TryStartSplitScreen()
        {
            if (GameManager.main == null || PlayerController.main == null)
                return; // Not in the game world.

            rocketA = PlayerController.main.player.Value as Rocket;
            if (rocketA == null)
                return;

            selectableRockets = GameManager.main.rockets
                .Where(r => r != null && r != rocketA)
                .ToList();

            if (selectableRockets.Count == 0)
                return; // No other rocket available.

            selectionMenuOpen = true;
        }

        private void CancelSelection()
        {
            selectionMenuOpen = false;
            rocketA = null;
        }

        private void ConfirmRocketSelection(Rocket chosen)
        {
            rocketB = chosen;
            selectionMenuOpen = false;

            CreateSecondaryCamera();

            if (secondaryCamera == null)
            {
                rocketA = null;
                rocketB = null;
                return;
            }

            splitActive = true;
            IsSplitActive = true;

            UpdateSecondaryCamera();
            UpdateViewportRects();
        }

        private void StopSplitScreen()
        {
            splitActive = false;
            IsSplitActive = false;
            selectionMenuOpen = false;
            originShiftedForRender = false;
            originRecenterFixIsWorking = false;

            Camera activeCam = GetActiveGameCamera();
            if (activeCam != null)
                activeCam.rect = new Rect(0f, 0f, 1f, 1f);

            if (secondaryCamera != null)
            {
                Object.Destroy(secondaryCamera.gameObject);
                secondaryCamera = null;
            }

            rocketA = null;
            rocketB = null;
        }

        // ---------------------------------------------------------------
        //  Secondary camera (view of the rocket that is NOT being controlled)
        // ---------------------------------------------------------------
        private void CreateSecondaryCamera()
        {
            Camera source = GetActiveGameCamera();
            if (source == null)
                return;

            GameObject camObj = new GameObject("SplitScreen_SecondaryCamera");
            secondaryCamera = camObj.AddComponent<Camera>();
            secondaryCamera.CopyFrom(source);

            if (source.GetComponent<AudioListener>() != null)
            {
                AudioListener listener = camObj.AddComponent<AudioListener>();
                listener.enabled = false;
            }

            secondaryCamera.depth = source.depth + 1;
        }

        private const double FarSceneryJitterDistance = 20000.0;

        private void UpdateSecondaryCamera()
        {
            if (secondaryCamera == null)
                return;

            Camera activeGameCam = GetActiveGameCamera();
            Rocket activeRocket = GetActiveRocket();
            Rocket inactiveRocket = (activeRocket == rocketA) ? rocketB : rocketA;

            if (activeGameCam == null || activeRocket == null || inactiveRocket == null)
                return;

            ForceRocketLoaded(inactiveRocket);

            if (!TryGetLocation(activeRocket, out Location activeLoc) ||
                !TryGetLocation(inactiveRocket, out Location inactiveLoc))
            {
                Vector3 fallbackOffset = activeGameCam.transform.position - activeRocket.transform.position;
                secondaryCamera.transform.position = inactiveRocket.transform.position + fallbackOffset;
                secondaryCamera.farClipPlane = activeGameCam.farClipPlane;
            }
            else
            {
                Double2 activeAbs = activeLoc.GetSolarSystemPosition(activeLoc.time);
                Double2 inactiveAbs = inactiveLoc.GetSolarSystemPosition(inactiveLoc.time);
                double deltaX = inactiveAbs.x - activeAbs.x;
                double deltaY = inactiveAbs.y - activeAbs.y;
                secondaryCamera.transform.position =
                    activeGameCam.transform.position + new Vector3((float)deltaX, (float)deltaY, 0f);

                double separationSq = deltaX * deltaX + deltaY * deltaY;
                bool needsFallbackClip = !EnableFloatingOriginRecenterFix || !originRecenterFixIsWorking;
                secondaryCamera.farClipPlane = needsFallbackClip && separationSq > FarSceneryJitterDistance * FarSceneryJitterDistance
                    ? Mathf.Min(activeGameCam.farClipPlane, (float)FarSceneryJitterDistance)
                    : activeGameCam.farClipPlane;
            }

            secondaryCamera.transform.rotation = activeGameCam.transform.rotation;
            secondaryCamera.orthographic = activeGameCam.orthographic;
            secondaryCamera.orthographicSize = activeGameCam.orthographicSize;
            secondaryCamera.fieldOfView = activeGameCam.fieldOfView;
            secondaryCamera.nearClipPlane = activeGameCam.nearClipPlane;
        }

        private void ForceRocketLoaded(Rocket rocket)
        {
            if (rocket == null || rocket.physics == null) return;

            WorldLoader loader = rocket.physics.loader;
            if (loader == null) return;

            loader.loadDistance = 1e15;

            if (!loader.Loaded)
            {
                SafeAccess.InvokeMethod(loader, "SetLoaded", true);
            }
        }

        private void ForceStaticObjectLoaded(StaticWorldObject obj)
        {
            if (obj == null) return;

            WorldLoader loader = SafeAccess.GetRaw(obj, "loader") as WorldLoader;
            if (loader == null) return;

            loader.loadDistance = 1e15;

            if (!loader.Loaded)
            {
                SafeAccess.InvokeMethod(loader, "SetLoaded", true);
            }
        }

        private SpaceCenter spaceCenterCache;
        private bool spaceCenterSearched;

        private SpaceCenter GetSpaceCenter()
        {
            if (spaceCenterCache == null && !spaceCenterSearched)
            {
                spaceCenterCache = Object.FindFirstObjectByType<SpaceCenter>();
                spaceCenterSearched = true;
            }
            return spaceCenterCache;
        }

        private void ForceBuildingsLoaded()
        {
            SpaceCenter spaceCenter = GetSpaceCenter();
            if (spaceCenter == null) return;

            ForceStaticObjectLoaded(spaceCenter.vab.building);
            ForceStaticObjectLoaded(spaceCenter.launchPad.building);
        }

        private bool TryGetLocation(Rocket rocket, out Location location)
        {
            location = null;
            if (rocket == null) return false;

            object locationComponent = SafeAccess.GetRaw(rocket, "location");
            if (locationComponent == null) return false;

            location = (locationComponent as WorldLocation)?.Value
                ?? SafeAccess.GetRaw(locationComponent, "Value") as Location
                ?? locationComponent as Location;

            return location != null;
        }

        // ---------------------------------------------------------------
        //  Split the screen into two halves
        // ---------------------------------------------------------------
        private void UpdateViewportRects()
        {
            Camera activeGameCam = GetActiveGameCamera();
            if (activeGameCam == null || secondaryCamera == null)
                return;

            Rocket activeRocket = GetActiveRocket();

            Rect leftRect = new Rect(0f, 0f, 0.5f, 1f);
            Rect rightRect = new Rect(0.5f, 0f, 0.5f, 1f);

            if (activeRocket == rocketA)
            {
                activeGameCam.rect = leftRect;
                secondaryCamera.rect = rightRect;
            }
            else
            {
                activeGameCam.rect = rightRect;
                secondaryCamera.rect = leftRect;
            }
        }

        private void OnCameraPreRender(Camera cam)
        {
            if (!EnableFloatingOriginRecenterFix) return;
            if (!splitActive || selectionMenuOpen || secondaryCamera == null) return;
            if (cam != secondaryCamera) return;
            if (WorldView.main == null) { originRecenterFixIsWorking = false; return; }

            Rocket activeRocket = GetActiveRocket();
            Rocket inactiveRocket = (activeRocket == rocketA) ? rocketB : rocketA;
            if (activeRocket == null || inactiveRocket == null) return;

            Camera activeGameCam = GetActiveGameCamera();
            if (activeGameCam == null) return;

            if (!TryGetLocation(inactiveRocket, out Location inactiveLoc))
            {
                originRecenterFixIsWorking = false;
                return;
            }

            viewLocationToRestore = WorldView.main.ViewLocation;
            originShiftedForRender = true;
            originRecenterFixIsWorking = true;

            WorldView.main.SetViewLocation(inactiveLoc);
            ForceEnvironmentResync();

            secondaryCamera.transform.position = activeGameCam.transform.position;
            secondaryCamera.transform.rotation = activeGameCam.transform.rotation;
        }

        private void OnCameraPostRender(Camera cam)
        {
            if (cam != secondaryCamera || !originShiftedForRender) return;
            originShiftedForRender = false;

            if (WorldView.main == null) return;

            WorldView.main.SetViewLocation(viewLocationToRestore);
            ForceEnvironmentResync();
        }

        private void ForceEnvironmentResync()
        {
            if (GameManager.main == null) return;
            object environment = GameManager.main.environment;
            if (environment == null) return;

            SafeAccess.InvokeNoArgMethod(environment, "LateUpdate");
        }

        // ---------------------------------------------------------------
        //  Click to switch which rocket is controlled
        // ---------------------------------------------------------------
        private void HandleClickToControl()
        {
            bool clickedLeftHalf = Input.mousePosition.x < Screen.width * 0.5f;
            Rocket target = clickedLeftHalf ? rocketA : rocketB;

            if (target == null)
                return;

            Rocket current = GetActiveRocket();
            if (target == current)
                return;

            // Instantly switches control focus and the game's main camera
            // over to the chosen rocket.
            PlayerController.main.SmoothChangePlayer(target, 0f);
        }

        // ---------------------------------------------------------------
        //  Helpers
        // ---------------------------------------------------------------
        private Rocket GetActiveRocket()
        {

            if (PlayerController.main == null || PlayerController.main.player.Value == null)
                return null;

            return PlayerController.main.player.Value as Rocket;
        }

        private Camera GetActiveGameCamera()
        {
            GameCamerasManager gcm = GameCamerasManager.main;
            if (gcm == null)
                return null;

            if (gcm.world_Camera != null && gcm.world_Camera.camera != null && gcm.world_Camera.camera.isActiveAndEnabled)
                return gcm.world_Camera.camera;

            if (gcm.scaledWorld_Camera != null && gcm.scaledWorld_Camera.camera != null && gcm.scaledWorld_Camera.camera.isActiveAndEnabled)
                return gcm.scaledWorld_Camera.camera;

            return null;
        }

        private void OnDisable()
        {
            if (splitActive)
            {
                StopSplitScreen();
            }
        }
    }
}
