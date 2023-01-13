//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;
using LookingGlass.Editor.EditorPropertyGroups;

namespace LookingGlass.Editor {
    [CustomEditor(typeof(HologramCamera))]
    [CanEditMultipleObjects]
    public class HologramCameraEditor : UnityEditor.Editor {
        #region Static Section
        public const string PrefabGuid = "3019d4eebe2593e428d7b9ccb096ceda";
        public static string PrefabAssetPath => AssetDatabase.GUIDToAssetPath(PrefabGuid);


        [MenuItem("GameObject/Looking Glass/Add Hologram Camera to Scene", false, 10)]
        private static void AddHologramCameraToScene() => CreateHologramCamera();

        [MenuItem("GameObject/Looking Glass/Convert to Hologram Camera", validate = true)]
        private static bool ValidateCameraComponentToHologramCamera() {
            GameObject selected = Selection.activeGameObject;
            return selected != null && selected.GetComponent<Camera>() != null && selected.GetComponent<HologramCamera>() == null;
        }

        // This function is for a right click menu on a game object with a camera component.
        [MenuItem("GameObject/Looking Glass/Convert to Hologram Camera")]
        private static void CameraComponentToHologramCamera() => ConvertCameraConfirmWindow();
            

        // This function is for right clicking on a camera component
        [MenuItem("CONTEXT/Camera/Convert to Hologram Camera")]
        private static void ContextCameraObjectToHologramCamera() => ConvertCameraConfirmWindow();

        /// <summary>
        /// Creates a <see cref="HologramCamera"/> prefab instance with undo creation support, and selects it in the Unity editor.
        /// </summary>
        public static HologramCamera CreateHologramCamera(Transform parent = null, int siblingIndex = -1) {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LookingGlass/Prefabs/Hologram Camera.prefab");
            if (prefab == null) {
                Debug.LogWarning("[Hologram Camera] Couldn't find the Hologram Camera folder or prefab.");
                return null;
            }

            if (siblingIndex < 0) {
                //NOTE: This temp GameObject is just here to calculate what the sibling index WOULD be.
                //For some reason, PrefabUtility.InstantiatePrefab(...) may put the new prefab instance in the middle of the hierarchy,
                //So it makes the hierarchy less orderly, and more messy.
                //However, doing "new GameObject()" puts it at the end of the hierarchy. So we copy that sibling index to mimick the same behavior with the prefab instance.

                GameObject temp = new GameObject("temp");
                siblingIndex = temp.transform.GetSiblingIndex();
                GameObject.DestroyImmediate(temp);
            }

            GameObject gameObject = (GameObject) PrefabUtility.InstantiatePrefab(prefab, parent);
            gameObject.name = prefab.name;

            if (siblingIndex >= 0)
                gameObject.transform.SetSiblingIndex(siblingIndex);

            Selection.activeObject = gameObject;
            Undo.RegisterCreatedObjectUndo(gameObject, "Create Hologram Camera");

            HologramCamera hologramCamera = gameObject.GetComponent<HologramCamera>();
            Assert.IsNotNull(hologramCamera);
            return hologramCamera;
        }

        /// <summary>
        /// <para>Converts the given <paramref name="target"/> <see cref="GameObject"/> into a <see cref="HologramCamera"/> prefab instance.<br />This method has full undo support.</para>
        /// <para>When <paramref name="useHologramCameraDefaults"/> is set to <c>true</c>, and if <paramref name="target"/> has a <see cref="Camera"/> component, fields from it are copied over to the new <see cref="HologramCamera"/> component.</para>
        /// </summary>
        /// <param name="target">The object to convert.</param>
        /// <param name="useHologramCameraDefaults">
        /// Should the new <see cref="HologramCamera"/> component initialize with default values from the prefab?<br />
        /// When set to <c>false</c>, if <paramref name="target"/> has a <see cref="Camera"/> component, fields from it are copied over to the new <see cref="HologramCamera"/> component.
        /// </param>
        public static void ConvertToHologramCamera(GameObject target, bool useHologramCameraDefaults) {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Convert to Hologram Camera");

            HologramCamera hologramCamera = CreateHologramCamera(target.transform.parent, target.transform.GetSiblingIndex());
            if (!useHologramCameraDefaults && target.TryGetComponent(out Camera previousCamera))
                hologramCamera.CameraProperties.CopyFromCamera(previousCamera);

            Undo.DestroyObjectImmediate(target);
        }

        private static void ConvertCameraConfirmWindow()
        {
            GameObject target = Selection.activeGameObject;
            Assert.IsNotNull(target);

            int dialogChoice = EditorUtility.DisplayDialogComplex("Use recommended settings for Looking Glass Camera? ",
                "Looking Glass Cameras work best with a smaller field of view and constrained clipping planes." +
                "Would you like to set your camera to Looking Glass Camera defaults? \n\n" +
                "Warning! This may break existing camera animations!", "Yes", "Cancel", "No");

            switch (dialogChoice)
            {
                case 0:
                    ConvertToHologramCamera(target, true);
                    Debug.Log("Use recommended values function"); // Slap on default Looking Glass Camera?
                    break;
                case 1:
                    Debug.Log("Cancel"); // THIS IS CANCEL
                    break;
                case 2:
                    ConvertToHologramCamera(target, false);
                    Debug.Log("Copy camera values to Looking Glass Camera function");
                    break;
            }
        }

        public static bool AddErrorCondition(HelpMessageCondition condition) {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));

            if (errorConditions == null) {
                errorConditions = new List<HelpMessageCondition>();
                errorConditions.Add(condition);
                return true;
            }
            if (errorConditions.Contains(condition))
                return false;
            errorConditions.Add(condition);
            return true;
        }

        public static bool RemoveErrorCondition(HelpMessageCondition condition) {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));
            return errorConditions != null && errorConditions.Remove(condition);
        }
        #endregion

        private static GUIContent versionLabel1;
        private static GUIContent versionLabel2;

        private static int editorsOpen = 0;
        private static string[] lkgNames;
        private static string[] lkgIndices;

        private static bool showCalibrationData;
        private static List<HelpMessageCondition> errorConditions;

        private HologramCamera[] cameras;

        private bool changedTargetDisplay = false;
        private bool changedLKGName = false;
        private bool changedLKGIndex = false;
        private bool changedEmulatedDevice = false;
        private bool changedQuiltPreset = false;
        private bool requiresPreviewUpdate = false;
        private bool changedTransformMode = false;
        private TransformMode nextTransformMode;

        private static HashSet<string> volumeModeProperties = new HashSet<string> {
            nameof(HologramCamera.size),
            nameof(HologramCamera.sizeMode),
            nameof(HologramCamera.nearClipFactor),
            nameof(HologramCamera.farClipFactor)
        };
        private static HashSet<string> cameraModeProperties = new HashSet<string> {
            nameof(HologramCamera.nearClipPlane),
            nameof(HologramCamera.farClipPlane),
            nameof(HologramCamera.focalPlane)
        };
        private static readonly EditorPropertyGroup[] Groups = new EditorPropertyGroup[] {
            new EditorPropertyGroup(
                "Camera Properties",
                "Contains a set of fields that correspond to fields on a Unity Camera, with some extra hologramCamera fields.",
                new string[] {
                    nameof(HologramCamera.transformMode),
                    nameof(HologramCamera.nearClipFactor),
                    nameof(HologramCamera.farClipFactor),
                    nameof(HologramCamera.nearClipPlane),
                    nameof(HologramCamera.farClipPlane),
                    nameof(HologramCamera.focalPlane),
                    nameof(HologramCamera.size),
                    nameof(HologramCamera.sizeMode),
                    nameof(HologramCamera.clearFlags),
                    nameof(HologramCamera.background),
                    nameof(HologramCamera.cullingMask),
                    nameof(HologramCamera.fov),
                    nameof(HologramCamera.depth),
                    nameof(HologramCamera.renderingPath),
                    nameof(HologramCamera.useOcclusionCulling),
                    nameof(HologramCamera.allowHDR),
                    nameof(HologramCamera.allowMSAA),
                    nameof(HologramCamera.allowDynamicResolution),
                    nameof(HologramCamera.useFrustumTarget),
                    nameof(HologramCamera.frustumTarget),
                    nameof(HologramCamera.centerOffset),
                    nameof(HologramCamera.horizontalFrustumOffset),
                    nameof(HologramCamera.verticalFrustumOffset),
                    nameof(HologramCamera.depthiness)
                }
            ),
            new EditorPropertyGroup(
                "Gizmos",
                "Contains settings for visualizations in the Scene View.",
                new string[] {
                    nameof(HologramCamera.drawHandles),
                    nameof(HologramCamera.frustumColor),
                    nameof(HologramCamera.middlePlaneColor),
                    nameof(HologramCamera.handleColor)
                }
            ),
            new EditorPropertyGroup(
                "Events",
                "Contains LookingGlass events, related to initialization and rendering.",
                new string[] {
                    nameof(HologramCamera.onCoreLoaded),
                    nameof(HologramCamera.onViewRendered)
                }
            ),
            new EditorPropertyGroup(
                "Optimization",
                "Contains LookingGlass optimization techniques to increase performance.",
                new string[] {
                    nameof(HologramCamera.viewInterpolation),
                    nameof(HologramCamera.reduceFlicker),
                    nameof(HologramCamera.fillGaps),
                    nameof(HologramCamera.blendViews),
                }
            ),
            new EditorPropertyGroup(
                "Debugging",
                "Contains settings to ease debugging the LookingGlass script.",
                new string[] {
                    nameof(HologramCamera.showAllObjects),
                    nameof(HologramCamera.onlyShowView),
                    nameof(HologramCamera.onlyRenderOneView)
                }
            )
        };

        private static Dictionary<HologramCamera, HologramEmulation> holograms = new Dictionary<HologramCamera, HologramEmulation>();
        private static List<HologramCamera> removeBuffer = new List<HologramCamera>();

        private IEnumerable<HologramEmulation> Holograms {
            get {
                foreach (HologramCamera h in cameras)
                    if (holograms.TryGetValue(h, out HologramEmulation hologram))
                        yield return hologram;
            }
        }

        #region Unity Messages
        private void OnEnable() {
            cameras = targets.Select(t => (HologramCamera) t).ToArray();
            editorsOpen++;

            if (editorsOpen == 1) {
                CalibrationManager.onRefresh += RefreshLKGNames;
                RefreshLKGNames();
            }

            Groups[0].IsExpanded = true;
            serializedObject.FindProperty(nameof(HologramCamera.customRenderSettings)).isExpanded = true;
            foreach (HologramCamera h in cameras)
                h.onGameViewResolutionTypeChanged += UpdateUserGameViewsCallback;

            foreach (HologramCamera c in cameras)
                if (!holograms.ContainsKey(c))
                    holograms.Add(c, new HologramEmulation(c));
        }

        private void OnDisable() {
            DisposeHolograms();

            editorsOpen--;
            if (editorsOpen <= 0)
                CalibrationManager.onRefresh -= RefreshLKGNames;

            versionLabel1 = null;
            versionLabel2 = null;

            foreach (HologramCamera h in cameras)
                if (h != null)
                    h.onGameViewResolutionTypeChanged -= UpdateUserGameViewsCallback;
        }

        protected virtual void OnSceneGUI() {
            HologramCamera current = (HologramCamera) target;
            try {
                if (current.enabled && !current.Gizmos.DrawHandles) {
                    if (current.CameraProperties.TransformMode == TransformMode.Volume)
                        DrawSizeHandles(current);
                }
            } catch (Exception e) {
                Debug.LogException(e);
            }

            if (current.ShowHologramPreview && (cameras.Length == 1 || Array.IndexOf(cameras, current) == 0))
                holograms[current].OnSceneGUI();
        }
        #endregion

        #region Inspector Preview
        public override bool RequiresConstantRepaint() {
            HologramCamera current = (HologramCamera) target;
            if (!current.ShowHologramPreview)
                return false;
            HologramEmulation hologram = holograms[current];
            return hologram != null && hologram.RequiresConstantRepaint();
        }

        public override bool HasPreviewGUI() {
            HologramCamera current = (HologramCamera) target;
            if (!current.ShowHologramPreview)
                return false;
            HologramEmulation hologram = holograms[current];
            return hologram != null && hologram.HasPreviewGUI();
        }

        public override void OnPreviewSettings() {
            HologramCamera current = (HologramCamera) target;
            if (!current.ShowHologramPreview)
                return;
            HologramEmulation hologram = holograms[current];

            bool value = GUILayout.Toggle(hologram.AutoRotate, "Auto Rotate", EditorStyles.toolbarButton);
            if (value != hologram.AutoRotate) {
                foreach (HologramEmulation h in Holograms)
                    h.AutoRotate = value;
            }
        }

        public override string GetInfoString() {
            HologramCamera current = (HologramCamera) target;
            if (!current.ShowHologramPreview)
                return null;
            HologramEmulation hologram = holograms[current];
            return hologram.GetInfoString();
        }

        public override void OnPreviewGUI(Rect r, GUIStyle background) {
            HologramCamera current = (HologramCamera) target;
            if (!current.ShowHologramPreview)
                return;
            HologramEmulation hologram = holograms[current];
            hologram.OnPreviewGUI(r, background);
        }
        #endregion

        private void DisposeHolograms() {
            removeBuffer.Clear();
            foreach (HologramCamera camera in cameras)
                if (holograms.ContainsKey(camera))
                    removeBuffer.Add(camera);

            foreach (HologramCamera camera in removeBuffer) {
                HologramEmulation hologram = holograms[camera];
                holograms.Remove(camera);
                hologram.Dispose();
            }
        }

        private void DrawSizeHandles(HologramCamera camera) {
            // for some reason, doesn't need the gamma conversion like gizmos do
            Handles.color = camera.Gizmos.HandleColor;

            Matrix4x4 originalMatrix = Handles.matrix;
            Matrix4x4 hpMatrix = Matrix4x4.TRS(
                camera.transform.position,
                camera.transform.rotation,
                    new Vector3(camera.SingleViewCamera.aspect, 1, 1));
            Handles.matrix = hpMatrix;

            float size = camera.CameraProperties.Size;
            Vector3[] dirs = new Vector3[] {
                        new Vector3(-size, 0),
                        new Vector3( size, 0),
                        new Vector3(0, -size),
                        new Vector3(0,  size),
                    };

            foreach (Vector3 direction in dirs) {
                EditorGUI.BeginChangeCheck();
                Vector3 newDir = Handles.Slider(direction, direction, HandleUtility.GetHandleSize(direction) * 0.03f, Handles.DotHandleCap, 0);
                if (EditorGUI.EndChangeCheck()) {
                    Undo.RecordObject(camera, "LookingGlass Size");

                    camera.CameraProperties.Size = Vector3.Dot(newDir, direction.normalized);
                    camera.OnValidate();
                    foreach (PropertyGroup group in camera.PropertyGroups)
                        group.OnValidate();
                    camera.ResetCameras();
                }
            }

            Handles.matrix = originalMatrix;
        }

        private static void RefreshLKGNames() {
            lkgNames = new string[CalibrationManager.CalibrationCount];
            for (int i = 0; i < lkgNames.Length; i++)
                lkgNames[i] = CalibrationManager.GetCalibration(i).LKGname;

            lkgIndices = new string[lkgNames.Length];
            for (int i = 0; i < lkgIndices.Length; i++) {
                lkgIndices[i] = i.ToString();
                if (i != CalibrationManager.GetCalibration(i).index)
                    Debug.LogError("It is assumed that each calibration's index field matches its actual index in the array!");
            }
        }


        public override void OnInspectorGUI() {
            // Update the serializedProperty - always do this in the beginning of OnInspectorGUI.
            serializedObject.Update();

            //WARNING: This script has the [CanEditMultipleObjects] attribute, yet we directly use base.target instead of base.targets!!
            HologramCamera hp = (HologramCamera) target;

            if (errorConditions != null) {
                foreach (HologramCamera h in cameras) {
                    foreach (HelpMessageCondition condition in errorConditions) {
                        HelpMessage helpMessage = condition(h);
                        if (helpMessage.HasMessage) {
                            EditorGUILayout.HelpBox(helpMessage.message, helpMessage.type);
                            continue;
                        }
                    }
                }
            }

            changedTargetDisplay = false;
            changedLKGName = false;
            changedLKGIndex = false;
            changedEmulatedDevice = false;
            changedQuiltPreset = false;
            requiresPreviewUpdate = false;
            changedTransformMode = false;

            if (versionLabel1 == null) {
                string version = HologramCamera.Version.ToString();
                versionLabel1 = new GUIContent("Version");
                versionLabel2 = new GUIContent(version.ToString(), "LookingGlass Unity Plugin " + version);
            }
            EditorGUILayout.LabelField(versionLabel1, versionLabel2, EditorStyles.miniLabel);

            SectionGUILayout.DrawDefaultInspectorWithSections(serializedObject, Groups, CustomGUI);

            if (changedTransformMode) {
                Undo.IncrementCurrentGroup();
                string undoName = "Set " + nameof(HologramCameraProperties.TransformMode);
                Undo.RecordObject(hp.transform, undoName);
                Undo.RecordObject(hp,           undoName);

                hp.CameraProperties.TransformMode = nextTransformMode;
                serializedObject.Update();
            } else {
                //Let's make sure to save here, in case we used any non-SerializedProperty Editor GUI:
                if (serializedObject.ApplyModifiedProperties())
                    EditorUtility.SetDirty(hp);
            }

            bool needsToSaveAgain = false;
            if (changedTargetDisplay) {
                needsToSaveAgain = true;
                hp.TargetDisplay = hp.TargetDisplay;
            }
            if (changedLKGName) {
                needsToSaveAgain = true;
                hp.TargetLKGName = hp.TargetLKGName;
            }
            if (changedLKGIndex) {
                needsToSaveAgain = true;
                hp.TargetLKGIndex = hp.TargetLKGIndex;
            }
            if (changedEmulatedDevice) {
                needsToSaveAgain = true;
                hp.EmulatedDevice = hp.EmulatedDevice;

                //NOTE: This assumes the emulatedDevice field only shows in the inspector when 0 LKG devices are recognized.
                //Thus, all game views should be set to the resolution of the HologramCamera's emulated device
                HologramCameraEditor.UpdateUserGameViews();
            }
            if (changedQuiltPreset) {
                needsToSaveAgain = true;
                hp.QuiltPreset = hp.QuiltPreset;
            }

            if (needsToSaveAgain)
                EditorUtility.SetDirty(hp);
            if (requiresPreviewUpdate) {
                Preview.UpdatePreview();
            }

            GUILayout.Space(EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

            GUILayout.BeginVertical(GUI.skin.box);
            EditorGUI.indentLevel++;
            showCalibrationData = EditorGUILayout.Foldout(showCalibrationData, "Calibration Data");
            if (showCalibrationData) {
                EditorGUI.indentLevel++;
                try {
                    //NOTE: When a custom resolution is NOT being used, we still print out a helpbox with 2 lines of no text,
                    //For UX -- to avoid shifting the view of the text area on the user!
                    EditorGUILayout.HelpBox(hp.IsUsingCustomResolution ? "A custom resolution is being used that differs from the LKG device's native resolution.\n" +
                        "Calibration values may be different than the native device's values." : "\n", MessageType.Info);
                    EditorGUILayout.TextArea(JsonUtility.ToJson(hp.cal, true), GUI.skin.label);
                } finally {
                    EditorGUI.indentLevel--;
                }
            }
            EditorGUI.indentLevel--;
            GUILayout.EndVertical();

            if (GUILayout.Button(Preview.togglePreviewShortcut))
                Preview.TogglePreview();

            if (GUILayout.Button("Reload Calibration")) {
                hp.ReloadCalibrationByName();
                if (Preview.IsActive)
                    Preview.UpdatePreview();
            }
        }

        private bool CustomGUI(SerializedProperty property) {
            HologramCamera hp = cameras[0];
            string propertyName = property.name;

            TransformMode mode = hp.CameraProperties.TransformMode;
            switch (mode) {
                case TransformMode.Volume:
                    //If this property is for the OTHER camera mode,
                    //DON'T draw this property, and return true to tell the GUI that we did custom GUI for this property.
                    if (cameraModeProperties.Contains(propertyName))
                        return true;
                    break;
                case TransformMode.Camera:
                    if (volumeModeProperties.Contains(propertyName))
                        return true;
                    break;
            }

            switch (propertyName) {
                case nameof(HologramCamera.customRenderSettings):
                    
                    if (hp.AreRenderSettingsLockedForRecording)
                        EditorGUILayout.HelpBox("Quilt settings are being overriden for recording.", MessageType.Info);

                    using (new EditorGUI.DisabledScope(hp.AreRenderSettingsLockedForRecording)) {
                        if (hp.QuiltPreset == QuiltPreset.Custom) {
                            EditorGUILayout.PropertyField(property);
                        } else {
                            EditorGUILayoutHelper.ReadOnlyRenderSettings(hp.RenderSettings);
                        }
                    }
                    return true;
                case nameof(HologramCamera.targetDisplay):
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(property, true);

                    if (EditorGUI.EndChangeCheck()) {
                        changedTargetDisplay = true;
                        requiresPreviewUpdate = true;
                    }
                    return true;
                case nameof(HologramCamera.targetLKGName):
                    if (lkgNames.Length > 0) {
                        bool changed = false;
                        int index = Array.IndexOf(lkgNames, hp.TargetLKGName);

                        EditorGUI.BeginChangeCheck();
                        Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));

                        EditorGUI.BeginProperty(rect, new GUIContent("Target LKG Name"), property);
                        index = EditorGUI.Popup(rect, "Target LKG Name", index, lkgNames);
                        EditorGUI.EndProperty();

                        if (index < 0 || index >= lkgNames.Length) {
                            index = 0;
                            changed = true;
                        }
                        changed |= EditorGUI.EndChangeCheck();
                        if (changed) {
                            property.stringValue = lkgNames[index];
                            changedLKGName = true;
                            requiresPreviewUpdate = true;
                        }
                    }
                    return true;
                case nameof(HologramCamera.targetLKGIndex):
                    if (lkgIndices.Length > 0) {
                        bool changed = false;
                        int index = Array.IndexOf(lkgIndices, hp.TargetLKGIndex.ToString());

                        EditorGUI.BeginChangeCheck();
                        Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing));

                        EditorGUI.BeginProperty(rect, new GUIContent("Target LKG Name"), property);
                        index = EditorGUI.Popup(rect, "Target LKG Index", index, lkgIndices);
                        EditorGUI.EndProperty();

                        GUILayout.Space(EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);

                        if (index < 0 || index >= lkgIndices.Length) {
                            index = 0;
                            changed = true;
                        }
                        changed |= EditorGUI.EndChangeCheck();
                        if (changed) {
                            property.intValue = int.Parse(lkgIndices[index]);
                            changedLKGIndex = true;
                            requiresPreviewUpdate = true;
                        }
                    }
                    return true;
                case nameof(HologramCamera.emulatedDevice):
                    //NOTE: We hide this field when at least 1 LKG device (calibration) is found.
                    if (CalibrationManager.HasAnyCalibrations)
                        return true;

                    using (new EditorGUI.DisabledScope(hp.AreRenderSettingsLockedForRecording)) {
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(property);

                        if (EditorGUI.EndChangeCheck()) {
                            changedEmulatedDevice = true;
                            requiresPreviewUpdate = true;
                        }
                    }
                    return true;
                case nameof(HologramCamera.transformMode):
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(property);
                    if (EditorGUI.EndChangeCheck()) {
                        changedTransformMode = true;
                        nextTransformMode = (TransformMode) property.enumValueIndex;
                    }
                    return true;
                case nameof(HologramCamera.focalPlane):
                    using (new EditorGUI.DisabledScope(mode != TransformMode.Camera)) {
                        EditorGUILayout.PropertyField(property);
                    }
                    return true;
                case nameof(HologramCamera.size):
                    using (new EditorGUI.DisabledScope(mode == TransformMode.Camera || hp.CameraProperties.SizeMode == SizeMode.ScaleSetsSize)) {
                        EditorGUILayout.PropertyField(property);
                    }
                    return true;
                case nameof(HologramCamera.sizeMode):
                    EditorGUILayout.PropertyField(property);
                    return true;
                case nameof(HologramCamera.renderStack):
                    EditorGUILayout.PropertyField(property);
                    GUILayout.Space(20);
                    return true;
                case nameof(HologramCamera.clearFlags):
                    GUILayout.Space(20);

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.PropertyField(property);

                    if (EditorGUI.EndChangeCheck()) {
                        foreach (HologramCamera h in cameras)
                            MultiViewRendering.Clear(h.QuiltTexture, CameraClearFlags.SolidColor, Color.clear);
                    }
                    return true;
            }
            return false;
        }

        private static void UpdateUserGameViewsCallback() => UpdateUserGameViews();
        public static bool UpdateUserGameViews() {
            HologramCamera main = HologramCamera.Instance;
            if (main == null)
                return false;

            GameViewResolutionType resolutionType = main.GameViewResolutionType;
            Vector2Int resolution;
            string deviceTypeName;

            HologramRenderSettings renderSettings = main.RenderSettings;
            //switch (resolutionType) {
            //    case GameViewResolutionType.NativeScreenSize:
                    Calibration cal = main.UnmodifiedCalibration;
                    if (RenderPipelineUtil.IsBuiltIn) {
                        resolution = new Vector2Int(cal.screenWidth, cal.screenHeight);
                        deviceTypeName = main.DeviceTypeName;
                    } else {
                        resolution = new Vector2Int(renderSettings.quiltWidth, renderSettings.quiltHeight);
                        deviceTypeName = (main.QuiltPreset == QuiltPreset.Automatic) ? main.DeviceTypeName : main.QuiltPreset.ToString();
                    }
            //break;
            //    case GameViewResolutionType.QuiltSingleViewSize:
            //        resolution = new Vector2Int(renderSettings.ViewWidth, renderSettings.ViewHeight);
            //        deviceTypeName = (main.QuiltPreset == QuiltPreset.Automatic) ? main.DeviceTypeName : main.QuiltPreset.ToString();
            //        break;
            //    default:
            //        throw new NotSupportedException("Unsupported game view resolution type: " + resolutionType);
            //}
            foreach (EditorWindow gameView in GameViewExtensions.FindUnpairedGameViews())
                gameView.SetGameViewResolution(resolution.x, resolution.y, deviceTypeName);

            return true;
        }
    }
}
