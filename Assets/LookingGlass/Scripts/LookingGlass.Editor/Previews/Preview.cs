//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System;
using UnityEngine;
using UnityEditor;

namespace LookingGlass.Editor {
    public static class Preview {
        public const string togglePreviewShortcut =
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            "Toggle Preview ⌘E";
#else
            "Toggle Preview Ctrl + E";
#endif

        public const string manualSettingsPath = "Assets/HologramCameraPreviewSettings.asset";
        private static ManualPreviewSettings manualPreviewSettings;

        public static bool IsActive {
            get {
                if (!HologramCamera.AnyEnabled)
                    return false;

                return PreviewWindow.Count > 0;
            }
        }

        public static bool UseManualPreview => manualPreviewSettings != null && manualPreviewSettings.manualPosition;
        public static ManualPreviewSettings ManualPreviewSettings => manualPreviewSettings;

        [InitializeOnLoadMethod]
        private static void InitPreview() {
            RuntimePreviewInternal.Initialize(() => IsActive, () => TogglePreview());
            EditorUpdates.Delay(1, AutoCloseExtraWindows);
        }

        [MenuItem("Assets/Create/LookingGlass/Manual Preview Settings")]
        private static void CreateManualPreviewAsset() {
            ManualPreviewSettings previewSettings = AssetDatabase.LoadAssetAtPath<ManualPreviewSettings>(manualSettingsPath);
            if (previewSettings == null) {
                previewSettings = ScriptableObject.CreateInstance<ManualPreviewSettings>();
                AssetDatabase.CreateAsset(previewSettings, manualSettingsPath);
                AssetDatabase.SaveAssets();
            }
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = previewSettings;
        }

        [MenuItem("LookingGlass/Toggle Preview %e", false, 1)]
        public static bool TogglePreview() {
            if (manualPreviewSettings == null)
                manualPreviewSettings = AssetDatabase.LoadAssetAtPath<ManualPreviewSettings>(manualSettingsPath);
            return TogglePreviewInternal();
        }

        private static void AutoCloseExtraWindows() {
            PluginCore.GetLoadResults();

            if (manualPreviewSettings != null && CalibrationManager.CalibrationCount < 1) {
                int count = PreviewPairs.Count;
                if (count > 0)
                    Debug.Log("[LookingGlass] Closing " + count + " extra Hologram Camera window(s).");

                PreviewPairs.CloseAll();
            }
        }

        private static bool TogglePreviewInternal() {
            bool wasActive = IsActive;

            CloseAllWindowsImmediate();
            EditorUpdates.Delay(5, () => {
                if (!wasActive)
                    OpenAllWindowsImmediate();
                else
                    EditorUpdates.ForceUnityRepaintImmediate();
            });

            return !wasActive;
        }

        internal static void OpenAllWindowsImmediate() {
            if (HologramCamera.Count == 0)
                Debug.LogWarning("Unable to create a " + nameof(PreviewWindow) + ": there was no " + nameof(HologramCamera) + " instance available.");

            HologramCameraEditor.UpdateUserGameViews();
            if (!RenderPipelineUtil.IsBuiltIn)
                return;

            //WARNING: Potentially duplicate call to LookingGlass Core?
            LoadResults loadResults = HologramCamera.ReloadAllCalibrationsByName();
            if (!UseManualPreview && (!loadResults.attempted || !loadResults.lkgDisplayFound || !loadResults.calibrationFound)) {
                Debug.LogWarning("No Looking Glass detected. Please ensure your display is correctly connected, or use manual preview settings instead.");
                CloseAllWindowsImmediate();
                return;
            }

            foreach (HologramCamera hologramCamera in HologramCamera.All) {
                if (hologramCamera.HasTargetDevice && PreviewPairs.IsPreviewOpenForDevice(hologramCamera.TargetLKGName)) {
                    Debug.LogWarning("Skipping preview for " + hologramCamera.name + " because its target LKG device already has a preview showing! The game views would overlap.");
                    continue;
                }
                PreviewWindow preview = PreviewPairs.Create(hologramCamera);
            }

            HologramCameraEditor.UpdateUserGameViews();
        }

        internal static void CloseAllWindowsImmediate() {
            PreviewPairs.CloseAll();
        }

        public static bool UpdatePreview() {
            if (!IsActive)
                return false;

            CloseAllWindowsImmediate();
            EditorUpdates.Delay(5, () => {
                OpenAllWindowsImmediate();
            });
            return true;
        }
    }
}
