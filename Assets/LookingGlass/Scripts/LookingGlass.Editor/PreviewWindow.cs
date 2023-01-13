using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;

namespace LookingGlass.Editor {
    [Serializable]
    public class PreviewWindow : ScriptableObject {
        private const string WindowNamePrefix = "LookingGlass Game View";

        public static PreviewWindow Create(HologramCamera hologramCamera) =>
            Create(hologramCamera, null);

        public static PreviewWindow Create(HologramCamera hologramCamera, EditorWindow gameView) {
            if (hologramCamera == null)
                throw new ArgumentNullException(nameof(hologramCamera));

            PreviewWindow preview = ScriptableObject.CreateInstance<PreviewWindow>();

            //Accounts for the case that the PreviewWindow destroys itself during Awake or OnEnable
            if (preview == null) {
                Debug.LogWarning("The preview window destroyed itself before being able to be used!");
                return null;
            }

            try {
                preview.name = WindowNamePrefix;

                if (all == null)
                    all = new List<PreviewWindow>();

                all.Add(preview);
                if (all.Count == 1) {
                    EditorApplication.wantsToQuit += CloseAllAndAcceptQuit;
                }

                preview.hologramCamera = hologramCamera;
                preview.RecreateGameView(hologramCamera);
                EditorApplication.update += preview.OnUpdate;
                return preview;
            } catch (Exception e) {
                Debug.LogException(e);
                ScriptableObject.DestroyImmediate(preview);
                return null;
            }
        }

        private static List<PreviewWindow> all;
        private static List<WindowsOSMonitor> monitors;
        private static bool supportsExperimentalDisplayDPIScaling =
#if UNITY_EDITOR_WIN
            true
#else
            false
#endif
            ;

        public static int Count => all?.Count ?? 0;
        public static IEnumerable<PreviewWindow> All {
            get {
                if (all == null)
                    yield break;
                foreach (PreviewWindow preview in all)
                    yield return preview;
            }
        }

        [SerializeField] private EditorWindow gameView;
        [SerializeField] private HologramCamera hologramCamera;
        [SerializeField] private WindowsOSMonitor matchingMonitor;

        private bool setCustomRenderSize = false;
        private Rect lastPos;
        private int frameCount = 0;

        public HologramCamera HologramCamera => hologramCamera;
        public EditorWindow GameView => gameView;

        #region Unity Messages
        //NOTE: For some reason, Unity auto-destroys this ScriptableObject when loading a new scene..
        private void OnDestroy() {
            EditorApplication.update -= OnUpdate;

            if (gameView != null) {
                gameView.Close();
                EditorWindow.DestroyImmediate(gameView);
            }

            if (hologramCamera != null) {
                hologramCamera.ClearCustomRenderingResolution();
                hologramCamera.RenderBlack = false;
            }

            if (all != null) {
                all.Remove(this);
                if (all.Count == 0) {
                    EditorApplication.wantsToQuit -= CloseAllAndAcceptQuit;
                }
            }
        }
        #endregion

        //NOTE: We recreate the game view more often than necessary because if we rely on Reflection too much,
        //the game view(s) get stuck. Recreating the whole window seems to flush Unity's internal state, and prevent that!
        private EditorWindow RecreateGameView(HologramCamera hologramCamera) {
            if (this.gameView != null) {
                this.gameView.Close();
                EditorWindow.DestroyImmediate(this.gameView);
            }

            frameCount = 0;
            lastPos = default;

            hologramCamera.RenderBlack = true;
            EditorWindow gameView = (EditorWindow)EditorWindow.CreateInstance(global::LookingGlass.GameViewExtensions.GameViewType);
#if UNITY_EDITOR_WIN
            gameView.Show();
#else
            //NOTE: On MacOS Big Sur (Intel) with Unity 2019.4, there was a 25px bottom-bar of unknown origin messing up the preview window.
            //This weird bottom bar did NOT occur on Unity 2018.4.
            //Either way, ShowUtility() made this issue go away.
            gameView.ShowUtility();
#endif
            gameView.titleContent = new GUIContent(name);
            this.gameView = gameView;

            if (frameCount >= 5) {
                InitializeWithHologramCamera();
                UpdateFromResolutionIfNeeded();
            }

            return gameView;
        }

        private void OnUpdate() {
            if (this == null) {
                //NOTE: Not sure why OnDestroy is not picking this up first.. but let's check ourselves anyway.
                EditorApplication.update -= OnUpdate;
                return;
            }

            if (hologramCamera == null) {
                Debug.LogWarning("The target " + nameof(LookingGlass) + " component was destroyed. Closing its preview window.");
                DestroyImmediate(this);
                return;
            }

            if (gameView == null) {
                Debug.LogWarning("The editor preview window was closed.");
                DestroyImmediate(this);
                return;
            }

            if (frameCount < 5) {
                Rect position = gameView.position;
                if (position != lastPos) {
                    lastPos = position;
                    try {
                        InitializeWithHologramCamera();
                        UpdateFromResolutionIfNeeded();
                    } catch (Exception e) {
                        Debug.LogError("An error occurred while updating the preview window! It will be closed.");
                        Debug.LogException(e);
                        DestroyImmediate(this);
                        return;
                    }
                }
            }
            if (frameCount == 7) {
                hologramCamera.RenderBlack = false;
                if (hologramCamera.Preview2D)
                    hologramCamera.RenderPreview2D();
                else
                    hologramCamera.RenderQuilt();
                EditorApplication.QueuePlayerLoopUpdate();
                gameView.Repaint();
            }

            frameCount++;
        }

        private static bool CloseAllAndAcceptQuit() {
            CloseAll();
            return true;
        }

        private static void CloseAll() {
            if (all == null)
                return;

            for (int i = all.Count - 1; i >= 0; i--) {
                PreviewWindow preview = all[i];
                preview.gameView.Close();
                ScriptableObject.DestroyImmediate(preview);
            }
        }

        private void InitializeWithHologramCamera() {
            Assert.IsNotNull(hologramCamera);
            setCustomRenderSize = false;

            Calibration cal = hologramCamera.UnmodifiedCalibration;

            RectInt unscaledRect;
            RectInt scaledRect;
            bool useManualPreview = Preview.UseManualPreview;

            if (useManualPreview) {
                ManualPreviewSettings settings = Preview.ManualPreviewSettings;
                unscaledRect = new RectInt(settings.position, settings.resolution);
            } else {
                unscaledRect = new RectInt(cal.xpos, cal.ypos, cal.screenWidth, cal.screenHeight);
            }

            int indexInList = -1;

            if (!useManualPreview && supportsExperimentalDisplayDPIScaling) {
                if (monitors == null)
                    monitors = new List<WindowsOSMonitor>();
                else
                    monitors.Clear();
                monitors.AddRange(WindowsOSMonitor.GetAll());
                indexInList = monitors.FindIndex((WindowsOSMonitor monitor) => monitor.NonScaledRect.Equals(unscaledRect));
            }

            if (indexInList >= 0) {
                matchingMonitor = monitors[indexInList];
                scaledRect = matchingMonitor.ScaledRect;
            } else {
                if (!useManualPreview && supportsExperimentalDisplayDPIScaling)
                    Debug.LogWarning("Unable to find a monitor matching the unscaled rect of " + unscaledRect + " from HoPS calibration data. " +
                        "The preview window might not handle DPI screen scaling properly.");

                scaledRect = unscaledRect;
            }

            //NOTE: When testing different resolutions, we must currently anchor the preview window to the bottom-left of the LKG device's screen.
            //This keeps the center visually consistent.
            //We've never tried to recalculate center values in the calibration data, though it might be possible.

            //After a few frames, we need to re-check to see what Unity allowed our position rect to be!
            //It will automatically resize to avoid going outside the screen, or overlapping the Windows taskbar.
            Rect idealRect = new Rect(scaledRect.position, scaledRect.size);

            //The default maxSize is usually good enough (Unity mentions 4000x4000),
            //But if we're on an 8K LKG device, this isn't large enough!
            //Just to be sure, let's check our maxSize is large enough for the ideal rect we want to set our size to:
            Vector2 prevMaxSize = gameView.maxSize;
            if (prevMaxSize.x < idealRect.width ||
                prevMaxSize.y < idealRect.height)
                gameView.maxSize = idealRect.size;
            
            if (frameCount < 1)
                gameView.position = idealRect;

            if (!useManualPreview) {
                //THIS ONLY WORKS WHEN DOCKED: Which never helps us lol..
                //gameView.maximized = true;

                //INSTEAD, let's do:
                gameView.AutoClickMaximizeButtonOnWindows();
            }

            gameView.SetFreeAspectSize();

            //WARNING: Our code didn't seem to be properly handling this.
            //While hiding the unnecessary toolbar was visually desirable,
            //This was causing preview window centerOffset / view-jumping issues on LKG devices.
            //gameView.SetShowToolbar(false);
        }

        private void UpdateFromResolutionIfNeeded() {
            Rect position = gameView.position;
            Calibration cal = hologramCamera.Calibration;
            Vector2 area = gameView.GetTargetSize();

            //These 2 variables are used for the custom rendering resolution:
            Vector2Int customPos;
            Vector2Int customSize;

            if (!Preview.UseManualPreview && supportsExperimentalDisplayDPIScaling) {
                //NOTE: The calibration works when using NON-scaled pixel coordinate values.
                //Even though this EditorWindow needs SCALED pixel coordinate values.
                Vector2Int scaledPos = new Vector2Int(
                    (int) position.x + (int) area.x,
                    (int) position.y + (int) area.y
                );

                Vector2Int scaledOffset = scaledPos - matchingMonitor.ScaledRect.position;
                Vector2Int unscaledOffset = Vector2Int.RoundToInt(matchingMonitor.UnscalePoint(scaledOffset));
                Vector2Int unscaledPos = unscaledOffset + matchingMonitor.NonScaledRect.position;

                Vector2Int scaledSize = new Vector2Int(
                    (int) area.x,
                    (int) area.y
                );

                Vector2Int unscaledSize = Vector2Int.RoundToInt(matchingMonitor.UnscalePoint(scaledSize));

                //Calibration uses NON-scaled values:
                customPos = unscaledPos;
                customSize = unscaledSize;
            } else {
                customPos = new Vector2Int(
                    (int) position.x,
                    (int) position.y
                );

                customSize = new Vector2Int(
                    (int) area.x,
                    (int) area.y
                );
            }

            if (!setCustomRenderSize &&
                (customPos.x != cal.xpos ||
                customPos.y != cal.ypos ||
                customSize.x != hologramCamera.ScreenWidth ||
                customSize.y != hologramCamera.ScreenHeight)) {
                hologramCamera.UseCustomRenderingResolution(customPos.x, customPos.y, customSize.x, customSize.y);
                hologramCamera.RenderQuilt(forceRender: true);
                setCustomRenderSize = true;
            }

            gameView.SetGameViewTargetDisplay((int) hologramCamera.TargetDisplay);
            gameView.SetGameViewZoom();
        }
    }
}
