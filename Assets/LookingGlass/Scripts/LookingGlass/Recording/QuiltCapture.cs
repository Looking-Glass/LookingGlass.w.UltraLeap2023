//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

// Based on MIT licensed FFmpegOut - FFmpeg video encoding plugin for Unity
// https://github.com/keijiro/FFmpegOut

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Video;
using FFmpegOut;
using Hjg.Pngcs;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LookingGlass {
    /// <summary>
    /// Provides a way to record quilt videos from within Unity scenes.
    /// </summary>
    [HelpURL("https://look.glass/unitydocs")]
    [RequireComponent(typeof(HologramCamera))]
    public sealed class QuiltCapture : MonoBehaviour {
        [Serializable]
        private struct ShortcutKeys {
            public KeyCode screenshot2D;
            public KeyCode screenshot3D;
        }

        /// <summary>
        /// The key name corresponding to the take number value stored in <see cref="PlayerPrefs"/>.
        /// </summary>
        internal const string TakeNumberKey = "takeNumber";
        internal const string TakeNumberTooltip = "Counting the index of current video. Will be used for naming if file name includes '" + TakeVariablePattern + "'";

        private const string TakeVariablePattern = "${take}";
        private static readonly Lazy<Regex> DefaultFileNamePattern = new Lazy<Regex>(() =>
            new Regex("^((Screenshot)|(Recording))(\\${take})$"));

        /// <summary>
        /// Contains extra metadata that is added to captured videos through FFmpeg.
        /// </summary>
        private static MediaMetadataPair[] GetMediaMetadata() {
            return new MediaMetadataPair[] {
                new MediaMetadataPair("CAPTURED_BY", "Unity"),
                new MediaMetadataPair("UNITY_VERSION", Application.unityVersion),
                new MediaMetadataPair("LOOKINGGLASS_UNITY_PLUGIN_VERSION", HologramCamera.Version.ToString())
            };
        }

        [Tooltip("File name of the output video. If it's empty, it will be set to the default (see QuiltCapture.DefaultFileName).")]
        [SerializeField] internal string fileName = "Recording" + TakeVariablePattern;
        [SerializeField] internal OutputFolder folderPath = new OutputFolder();

        [SerializeField] internal QuiltCaptureMode captureMode = QuiltCaptureMode.SingleFrame;
        [SerializeField] internal QuiltScreenshotPreset screenshotPreset;
        [SerializeField] internal QuiltRecordingPreset recordingPreset;

        [SerializeField, HideInInspector] internal int startFrame;
        [SerializeField, HideInInspector] internal int endFrame = 30;
        [SerializeField, HideInInspector] internal float startTime;
        [SerializeField, HideInInspector] internal float endTime = 1;

        [SerializeField, HideInInspector] internal bool recordOnStart = false;
        [Tooltip("When set to true, play mode will exit when the recording is stopped.")]
        [SerializeField, HideInInspector] internal bool exitPlayModeOnStop = true;

        [SerializeField] internal QuiltRecordingSettings customRecordingSettings = QuiltRecordingSettings.GetSettings(QuiltRecordingPreset.LookingGlassPortraitStandardQuality);

        [Tooltip("A collection of settings that may be applied to single frame capture/screenshots.")]
        [SerializeField] internal QuiltCaptureOverrideSettings customScreenshotSettings = new QuiltCaptureOverrideSettings();

        [Tooltip("Set this to reference a VideoPlayer if you wish to end the recording immediately when a given VideoPlayer finishes playback.")]
        [SerializeField] internal VideoPlayer syncedVideoPlayer;

        [SerializeField] private ShortcutKeys playmodeShortcuts = new ShortcutKeys {
            screenshot2D = KeyCode.F9,
            screenshot3D = KeyCode.F10
        };

        //NOTE: Duplicate logic with LookingGlass.initialized
        /// <summary>
        /// Allows us to initialize immediately during Awake,
        /// and re-initialize on every subsequence OnEnable call after being disabled and re-enabled.
        /// </summary>
        private bool initialized = false;

        private HologramCamera hologramCamera;
        private SyncedVideoPlayerCollection syncedCollection;
        public VideoPlayer[] SycnedVideoPlayers => syncedCollection.GetAll().ToArray();
        public void AddVideoPlayerToSync(VideoPlayer videoPlayer, bool freezeOnAdd = false) => syncedCollection.AddVideoPlayer(videoPlayer, freezeOnAdd);
        public void AddVideoPlayersToSync(IEnumerable<VideoPlayer> videoPlayers, bool freezeOnAdd = false) => syncedCollection.AddVideoPlayers(videoPlayers, freezeOnAdd);
        public void RemoveVideoPlayerFromSync(VideoPlayer videoPlayer, bool restoreOnRemove = true) => syncedCollection.RemoveVideoPlayer(videoPlayer, restoreOnRemove);
        public void RemoveVideoPlayersFromSync(IEnumerable<VideoPlayer> videoPlayers, bool restoreOnRemove = true) => syncedCollection.RemoveVideoPlayers(videoPlayers, restoreOnRemove);

        private FFmpegSession session;
        private QuiltCaptureState state;

        internal bool overridesAreInEffect = false;
        private int previousCaptureFramerate;
        private QuiltPreset previousPreset;
        private HologramRenderSettings previousCustom;
        private bool previousPreviewSettings;
        private float previousAspect;
        private float previousNearClip;

        private RecorderTiming timing;
#if UNITY_EDITOR
        private bool alreadyImportedFFmpegShader = false;
#endif

        public event Action<QuiltCaptureState> onStateChanged;
        internal event Action<RenderTexture> onBeforePushFrame;

        public string FileName {
            get { return fileName; }
            set { fileName = value; }
        }

        internal OutputFolder FolderPath {
            get { return folderPath; }
        }

        public int TakeNumber {
            get { return PlayerPrefs.GetInt(TakeNumberKey, 0); }
            set { PlayerPrefs.SetInt(TakeNumberKey, Mathf.Max(0, value)); }
        }

        public QuiltCaptureMode CaptureMode {
            get { return captureMode; }
            set {
                if (state != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change capture mode during recording!");
                captureMode = value;
            }
        }
        public QuiltScreenshotPreset ScreenshotPreset {
            get { return screenshotPreset; }
            set {
                if (State != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change screenshot preset during recording!");
                screenshotPreset = value;
            }
        }
        public QuiltRecordingPreset RecordingPreset {
            get { return recordingPreset; }
            set {
                if (State != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change recording preset during recording!");
                recordingPreset = value;
            }
        }

        public int StartFrame {
            get { return startFrame; }
            set {
                if (State != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change start frame during recording!");
                startFrame = Mathf.Max(0, value);
            }
        }

        public int EndFrame {
            get { return endFrame; }
            set {
                if (State != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change end frame during recording!");
                Mathf.Max(1, endFrame = value);
            }
        }

        public float StartTime {
            get { return startTime; }
            set {
                if (State != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change start time during recording!");
                startTime = value;
            }
        }

        public float EndTime {
            get { return endTime; }
            set {
                if (State != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change end time during recording!");
                endTime = value;
            }
        }

        public bool RecordOnStart {
            get { return recordOnStart; }
            set { recordOnStart = value; }
        }

        public bool ExitPlayModeOnStop {
            get { return exitPlayModeOnStop; }
            set { exitPlayModeOnStop = value; }
        }

        public QuiltRecordingSettings CustomRecordingSettings {
            get { return customRecordingSettings; }
            set {
                if (state != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change settings during recording!");
                customRecordingSettings = value;
            }
        }

        public QuiltRecordingSettings RecordingSettings {
            get {
                QuiltRecordingSettings settings;

                switch (recordingPreset) {
                    case QuiltRecordingPreset.Custom:
                        settings = customRecordingSettings;
                        break;
                    case QuiltRecordingPreset.Automatic:
                        QuiltRecordingPreset partial;
                        switch (HologramCamera.UnmodifiedCalibration.GetDeviceType()) {
                            case DeviceType.Portrait:
                            default:
                                partial = QuiltRecordingPreset.LookingGlassPortraitStandardQuality;
                                break;
                            case DeviceType._16in:
                                partial = QuiltRecordingPreset.LookingGlass16StandardQuality;
                                break;
                            case DeviceType._32in:
                                partial = QuiltRecordingPreset.LookingGlass32StandardQuality;
                                break;
                            case DeviceType._65in:
                                partial = QuiltRecordingPreset.LookingGlass65StandardQuality;
                                break;
                        }
                        settings = QuiltRecordingSettings.GetSettings(partial);
                        settings.cameraOverrideSettings = new QuiltCaptureOverrideSettings(HologramCamera);
                        break;
                    default:
                        settings = QuiltRecordingSettings.GetSettings(recordingPreset);
                        break;
                }

                if (captureMode == QuiltCaptureMode.ClipLength && syncedVideoPlayer != null)
                    settings.frameRate = (float) Math.Round(syncedVideoPlayer.frameRate * 100) / 100;

                return settings;
            }
        }

        public QuiltCaptureOverrideSettings CustomScreenshotSettings {
            get { return customScreenshotSettings; }
            set {
                if (state != QuiltCaptureState.NotRecording)
                    throw new InvalidOperationException("Cannot change settings during recording!");
                customScreenshotSettings = value;
            }
        }

        public QuiltCaptureOverrideSettings ScreenshotSettings {
            get {
                if (screenshotPreset == QuiltScreenshotPreset.Custom)
                    return customScreenshotSettings;
                if (screenshotPreset == QuiltScreenshotPreset.Automatic)
                    return new QuiltCaptureOverrideSettings(HologramCamera);
                return QuiltScreenshot.GetSettings(screenshotPreset);
            }
        }

        public QuiltCaptureOverrideSettings OverrideSettings
            => (CaptureMode == QuiltCaptureMode.SingleFrame) ? ScreenshotSettings : RecordingSettings.cameraOverrideSettings;

        private bool MatchVideoDuration => captureMode == QuiltCaptureMode.ClipLength;

        public QuiltCaptureState State {
            get { return state; }
            private set {
                if (state == value)
                    return;
                QuiltCaptureState prevState = state;

                state = value;
                switch (state) {
                    case QuiltCaptureState.Recording:
                        timing = new RecorderTiming(RecordingSettings.frameRate);
                        UseOverrideSettings();
                        break;
                    default:
                        if (state == QuiltCaptureState.NotRecording)
                            TakeNumber++;
                        timing = new RecorderTiming();
                        ReleaseOverrideSettings();
                        break;
                }
                onStateChanged?.Invoke(state);
            }
        }

        /// <summary>
        /// <para>Contains runtime data about the current frame's timing in the recording.
        /// NOTE: This is only valid during multi-frame recordings, and will be zeroed out otherwise.</para>
        /// <para>See also: <seealso cref="RecorderTiming"/></para>
        /// </summary>
        public RecorderTiming Timing {
            get {
                if (State == QuiltCaptureState.NotRecording || CaptureMode == QuiltCaptureMode.SingleFrame)
                    Debug.LogWarning(this + " is not currently multi-frame recording. The timing data will be all default values.");
                return timing;
            }
        }
        public HologramCamera HologramCamera {
            get {
                if (hologramCamera == null)
                    hologramCamera = GetComponent<HologramCamera>();
                return hologramCamera;
            }
        }

        #region Unity Messages
        private void OnValidate() {
            CheckToLogClipLengthWarning();

            if (IsDefaultFileName(fileName))
                fileName = GetDefaultFileName(CaptureMode);
        }

        private void Awake() {
            initialized = true;
            Initialize();
        }

        private void OnEnable() {
            if (initialized)
                return;
            initialized = true;
            Initialize();
        }

        private void OnDisable() {
            initialized = false;
            StopRecording();
        }

        private void Update() {
            QuiltCaptureState state = State;
            if (state == QuiltCaptureState.NotRecording) {
                if (Input.GetKeyDown(playmodeShortcuts.screenshot2D))
                    _ = Screenshot2D();
                else if (Input.GetKeyDown(playmodeShortcuts.screenshot3D))
                    _ = Screenshot3D();
            }

            if (state == QuiltCaptureState.NotRecording || CaptureMode == QuiltCaptureMode.SingleFrame)
                return;

            HologramCamera hologramCamera = HologramCamera;
            RenderTexture quilt = hologramCamera.QuiltTexture;

            if (!hologramCamera) {
                Debug.LogWarning("[LookingGlass] Failed to record because no LookingGlass Capture instance exists.");
                return;
            }

            float gap = Time.time - timing.FrameTime;
            float delta = 1 / RecordingSettings.frameRate;
            bool pushedThisFrame = true;

            if (gap < 0 || state == QuiltCaptureState.Paused) {
                pushedThisFrame = false;
                // Update without frame data.
                session.PushFrame(null);
            } else if (gap < delta) {
                // Single-frame behind from the current time:
                // Push the current frame to FFmpeg.
                onBeforePushFrame?.Invoke(quilt);
                session.PushFrame(quilt);
                timing.OnFramePushed();
            } else if (gap < delta * 2) {
                // Two-frame behind from the current time:
                // Push the current frame twice to FFmpeg. Actually this is not
                // an efficient way to catch up. We should think about
                // implementing frame duplication in a more proper way. #fixme

                onBeforePushFrame?.Invoke(quilt);
                session.PushFrame(quilt);
                timing.OnFramePushed();

                onBeforePushFrame?.Invoke(quilt);
                session.PushFrame(quilt);
                timing.OnFramePushed();
            } else {
                // Show a warning message about the situation.
                timing.OnFrameDropped();

                // Push the current frame to FFmpeg.
                onBeforePushFrame?.Invoke(quilt);
                session.PushFrame(quilt);

                // Compensate the time delay.
                timing.CatchUp(gap);
            }

            if (pushedThisFrame)
                syncedCollection.StepAll();
        }
        #endregion

        private void Initialize() {
            //Store all video players and their playback speeds
            syncedCollection = new SyncedVideoPlayerCollection(FindObjectsOfType<VideoPlayer>());

            // To begin record, make component enable
            if (captureMode == QuiltCaptureMode.Manual && recordOnStart)
                StartRecordingInternal();
            else {
                if (captureMode == QuiltCaptureMode.FrameInterval) {
                    _ = StartRecordingFrames(startFrame, endFrame);
                } else if (captureMode == QuiltCaptureMode.TimeInterval) {
                    _ = StartRecording(startTime, endTime);
                } else if (MatchVideoDuration) {
                    if (CheckToLogClipLengthWarning()) {
#if UNITY_EDITOR
                        if (ExitPlayModeOnStop)
                            EditorApplication.isPlaying = false;
#endif
                        return;
                    }
                    _ = StartRecordingFrames(0, (int) syncedVideoPlayer.clip.frameCount);
                }
            }

            StartCoroutine(SyncFFmpegCoroutine());
        }

        private IEnumerator SyncFFmpegCoroutine() {
            YieldInstruction wait = new WaitForEndOfFrame();

            yield return wait;
            while (isActiveAndEnabled) {
                if (session != null)
                    session.CompletePushFrames();
                yield return wait;
            }
        }

        private bool CheckToLogClipLengthWarning() {
            if (MatchVideoDuration && (syncedVideoPlayer == null || syncedVideoPlayer.clip == null)) {
                Debug.LogWarning("No synced video player or video clip referenced. Cannot match recording duration.");
                return true;
            }
            return false;
        }

        #region File Naming
        public string GetDefaultPrefix(QuiltCaptureMode captureMode) => captureMode == QuiltCaptureMode.SingleFrame ? "Screenshot" : "Recording";
        public string GetDefaultFileName(QuiltCaptureMode captureMode) => GetDefaultPrefix(captureMode) + TakeVariablePattern;
        public bool IsDefaultFileName(string fileName) => DefaultFileNamePattern.Value.IsMatch(fileName);

        public string CalculateAutoCorrectPath() => CalculateAutoCorrectPath(OverrideSettings.renderSettings);
        private string CalculateAutoCorrectPath(HologramRenderSettings renderSettings) {
            float finalAspect = renderSettings.aspect;

            //NOTE: We don't just grab from HologramCamera.Aspect here, because we might be calling
            //CalculateAutoCorrectPath(...) BEFORE even being in the recording state and applying state to the LookingGlass object.
            if (finalAspect <= 0)
                finalAspect = HologramCamera.UnmodifiedCalibration.GetAspect();

            return CalculateAutoCorrectPath(renderSettings.viewColumns, renderSettings.viewRows, finalAspect, TakeNumber);
        }
        private string CalculateAutoCorrectPath(int viewColumns, int viewRows, float finalAspect, int takeNumber) {
            Assert.IsTrue(finalAspect > 0);
            string quiltSuffix = "_qs" + viewColumns + "x" + viewRows + "a" + finalAspect.ToString("F2"); //NOTE: The aspect here is of the LKG device's native screen resolution.
            string fileExtension = captureMode == QuiltCaptureMode.SingleFrame ? ".png" : RecordingSettings.codec.GetFileExtension();
            string fileName = this.fileName.Replace(TakeVariablePattern, takeNumber.ToString()) + quiltSuffix + fileExtension;

            string outputPath = Path.Combine(folderPath.GetFullPath(), fileName).Replace("\\", "/");
            return outputPath;
        }
        #endregion

        #region Recording Methods
        private void ValidateIsManualOrCanStartManual() {
            if (CaptureMode != QuiltCaptureMode.Manual && State != QuiltCaptureState.NotRecording)
                throw new InvalidOperationException("Cannot use manual recording methods! You must stop recording, or already be in the manual recording mode.");
        }

        public void StartRecording() {
            ValidateIsManualOrCanStartManual();
            if (CaptureMode != QuiltCaptureMode.Manual)
                CaptureMode = QuiltCaptureMode.Manual;
            StartRecordingInternal();
        }

        public void StartRecording(string outputFilePath) {
            ValidateIsManualOrCanStartManual();
            if (CaptureMode != QuiltCaptureMode.Manual)
                CaptureMode = QuiltCaptureMode.Manual;
            StartRecordingInternal(outputFilePath);
        }

        public void PauseRecording() {
            ValidateIsManualOrCanStartManual();
            if (CaptureMode != QuiltCaptureMode.Manual)
                CaptureMode = QuiltCaptureMode.Manual;
            PauseRecordingInternal();
        }

        public void ResumeRecording() {
            ValidateIsManualOrCanStartManual();
            if (CaptureMode != QuiltCaptureMode.Manual)
                CaptureMode = QuiltCaptureMode.Manual;
            ResumeRecordingInternal();
        }

        public void StopRecording() {
            if (State == QuiltCaptureState.NotRecording)
                return;

            // set the playback speed back to original
            syncedCollection.RestoreAll();

            StopAllCoroutines();
            if (session != null) {
                Debug.Log("Closing FFmpegSession after " + timing.FrameCount + " frames.");
                session.Close();
                session.Dispose();
                session = null;
            }

            if (GetComponent<FrameRateController>() == null)
                Time.captureFramerate = previousCaptureFramerate;

            State = QuiltCaptureState.NotRecording;

#if UNITY_EDITOR
            if (ExitPlayModeOnStop)
                EditorApplication.isPlaying = false;
#endif
        }

        public void UseOverrideSettings() {
            if (overridesAreInEffect)
                return;

            HologramCamera hologramCamera = HologramCamera;
            if (!hologramCamera) {
                Debug.LogWarning("[LookingGlass] Failed to set up quilt settings because no LookingGlass Capture instance exists");
                return;
            }

            overridesAreInEffect = true;
            previousPreset = hologramCamera.QuiltPreset;
            previousCustom = hologramCamera.CustomRenderSettings;
            previousPreviewSettings = hologramCamera.Preview2D;
            previousAspect = hologramCamera.cal.aspect;
            previousNearClip = hologramCamera.CameraProperties.NearClipFactor;

            QuiltCaptureOverrideSettings overrideSettings = OverrideSettings;
            hologramCamera.SetQuiltPresetAndSettings(QuiltPreset.Custom, overrideSettings.renderSettings);
            hologramCamera.Preview2D = false;
            hologramCamera.CameraProperties.NearClipFactor = overrideSettings.nearClipFactor; //TODO: We need to handle the case of LookingGlassTransformMode.Camera using nearClipPlane instead!
            hologramCamera.cal.aspect = overrideSettings.renderSettings.aspect;
            hologramCamera.LockRenderSettingsForRecording(this, true);
            hologramCamera.RenderQuilt(true);
        }

        public void ReleaseOverrideSettings() {
            if (!overridesAreInEffect)
                return;

            HologramCamera hologramCamera = HologramCamera;
            if (!hologramCamera) {
                Debug.LogWarning("[LookingGlass] Failed to restore quilt settings because no LookingGlass Capture instance exists");
                return;
            }

            overridesAreInEffect = false;
            hologramCamera.UnlockRenderSettingsFromRecording(this);
            hologramCamera.Preview2D = previousPreviewSettings;
            hologramCamera.SetQuiltPresetAndSettings(previousPreset, previousCustom);
            hologramCamera.CameraProperties.NearClipFactor = previousNearClip;
            hologramCamera.cal.aspect = previousAspect;
        }

        /// <summary>
        /// <para>Starts a recording session that will output a video file to a file at the default path.</para>
        /// <para>See also: <seealso cref="CalculateAutoCorrectPath"/></para>
        /// </summary>
        public void StartRecordingInternal() => StartRecordingInternal(CalculateAutoCorrectPath());

        /// <summary>
        /// Starts a recording session that will output a video file to a file at the given <paramref name="outputFilePath"/>.
        /// </summary>
        public void StartRecordingInternal(string outputFilePath) {
            if (!Application.isPlaying)
                throw new InvalidOperationException("You can only call " + nameof(StartRecording) + " in playmode.");
            if (outputFilePath == null)
                throw new ArgumentNullException(nameof(outputFilePath));

            CheckToLogClipLengthWarning();

            syncedCollection.FreezeAll();

            HologramCamera hologramCamera = HologramCamera;
            if (!hologramCamera) {
                Debug.LogWarning("[LookingGlass] Failed to start recorder because no LookingGlass Capture instance exists.");
            }

            if (session != null)
                session.Dispose();

            string fullpath = Path.GetFullPath(outputFilePath);
            string dir = Path.GetDirectoryName(fullpath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            State = QuiltCaptureState.Recording;

            RenderTexture quilt = hologramCamera.QuiltTexture;
            Debug.Log("Creating FFmpeg session with size "
                + quilt.width + "x" + quilt.height + ", will be saved at " + fullpath);

            QuiltRecordingSettings recordingSettings = RecordingSettings;
            string extraFFmpegOptions = "-b:v " + recordingSettings.targetBitrateInMegabits + "M";

#if !UNITY_EDITOR_OSX && !UNITY_STANDALONE_OSX
            switch (recordingSettings.codec) {
                case FFmpegPreset.H264Nvidia:
                case FFmpegPreset.HevcNvidia:
                    extraFFmpegOptions += " -cq:v ";
                    break;
                default:
                    extraFFmpegOptions += " -crf ";
                    break;
            }
            extraFFmpegOptions += recordingSettings.compression;
#endif

#if UNITY_EDITOR
            //This fixes FFmpegSession using Shader.Find("Hidden/FFmpegOut/Preprocess") returning null!
            if (!alreadyImportedFFmpegShader) {
                alreadyImportedFFmpegShader = true;
                string resourcesFolderGuid = "0c36e64b6a30f4a43abc488dc63a3323";
                string resourcesFolderPath = AssetDatabase.GUIDToAssetPath(resourcesFolderGuid);
                AssetDatabase.ImportAsset(resourcesFolderPath, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceSynchronousImport);
            }
#endif

            session = FFmpegSession.CreateWithOutputPath(outputFilePath, quilt.width, quilt.height, timing.FrameRate, recordingSettings.codec, extraFFmpegOptions, GetMediaMetadata());

            if (GetComponent<FrameRateController>() == null) {
                previousCaptureFramerate = Time.captureFramerate;
                Time.captureFramerate = Mathf.RoundToInt(timing.FrameRate);
            }
        }

        public void PauseRecordingInternal() {
            if (State == QuiltCaptureState.Recording)
                State = QuiltCaptureState.Paused;
            else
                Debug.LogWarning("[LookingGlass] Can't pause recording when it's not started.");
        }

        public void ResumeRecordingInternal() {
            if (State == QuiltCaptureState.Paused)
                State = QuiltCaptureState.Recording;
            else
                Debug.LogWarning("[LookingGlass] Can't resume recording when it's not paused.");
        }

        public async Task StartRecordingFrames(int startFrame, int endFrame) => await StartRecordingFrames(startFrame, endFrame, false);
        public async Task StartRecordingFrames(int startFrame, int endFrame, bool enforceState) {
            if (enforceState) {
                StartFrame = startFrame;
                EndFrame = endFrame;
            }

            int frameCount = endFrame - startFrame;
            if (frameCount <= 0)
                throw new ArgumentException("The total number of frames must be greater than zero!");

            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            StartCoroutine(FrameIntervalRecordingCoroutine(startFrame, endFrame, enforceState, tcs));
            try {
                await tcs.Task;
            } catch (Exception e) {
                Debug.LogException(e);
                throw;
            }
        }

        public async Task StartRecording(float startTime, float endTime) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            StartCoroutine(TimeIntervalRecordingCoroutine(startTime, endTime, tcs));
            try {
                await tcs.Task;
            } catch (Exception e) {
                Debug.LogException(e);
                throw;
            }
        }

        public async Task<Texture2D> Screenshot2D(bool awaitPNGMetadata = false) => await Screenshot2D(CalculateAutoCorrectPath(), awaitPNGMetadata);
        public async Task<Texture2D> Screenshot3D(bool awaitPNGMetadata = false) => await Screenshot3D(CalculateAutoCorrectPath(), awaitPNGMetadata);

        public async Task<Texture2D> Screenshot2D(string outputFilePath, bool awaitPNGMetadata = false) {
            return await PerformAfterOverrideSettingsAreInEffect(QuiltCaptureMode.SingleFrame, async () => {
                //WARNING: We may want to save the final quilt mix instead of just the quilt texture!
                Util.EncodeToPNGBytes(HologramCamera.RenderPreview2D(true), out Texture2D screenshot, out byte[] bytes);
                await Task.Run(() => SaveScreenshot(outputFilePath, bytes));

                Task metadataTask = AddPNGMetadata(outputFilePath);
                if (awaitPNGMetadata)
                    await metadataTask;

                return screenshot;
            });
        }
        public async Task<Texture2D> Screenshot3D(string outputFilePath, bool awaitPNGMetadata = false) {
            return await PerformAfterOverrideSettingsAreInEffect(QuiltCaptureMode.SingleFrame, async () => {
                Util.EncodeToPNGBytes(HologramCamera.RenderStack.QuiltMix, out Texture2D screenshot, out byte[] bytes);
                await Task.Run(() => SaveScreenshot(outputFilePath, bytes));

                Task metadataTask = AddPNGMetadata(outputFilePath);
                if (awaitPNGMetadata)
                    await metadataTask;

                return screenshot;
            });
        }

        //TODO: Make this delay logic more standardized and documented
        //For existing explanation, search slack in #software-unity-plugin for "This is a demo of me spam-clicking the SingleFrame capture"
        private async Task<T> PerformAfterOverrideSettingsAreInEffect<T>(QuiltCaptureMode captureMode, Func<Task<T>> callback) {
            try {
                TaskCompletionSource<T> tcs = new TaskCompletionSource<T>();

                CaptureMode = captureMode;
                State = QuiltCaptureState.Recording;
                HologramCamera.RenderQuilt(true);

#if UNITY_EDITOR
                GameViewExtensions.RepaintAllViewsImmediately();
                EditorApplication.delayCall += () => {
                    EditorUpdates.Delay(1, async () => {
#else
                await Task.Delay(100);
#endif
                        if (CaptureMode != captureMode) {
                            tcs.SetException(new TaskCanceledException());
                        } else {
                            //But we NEED to re-render the quilt, or else our quilt texture would be outdated below when we save it to a screenshot!
                            HologramCamera.RenderQuilt(true);
#if UNITY_EDITOR
                            GameViewExtensions.RepaintAllViewsImmediately();
    #endif
                            T result = default;
                            Exception exception = null;
                            try {
                                result = await callback();
                            } finally {
                                State = QuiltCaptureState.NotRecording;
                                HologramCamera.RenderQuilt(true);

                                if (exception != null)
                                    tcs.SetException(exception);
                                else
                                    tcs.SetResult(result);
                            }
                        }
#if UNITY_EDITOR
                    });
                };
#endif
                return await tcs.Task;
            } catch (Exception e) {
                Debug.LogException(e);
                throw;
            }
        }

        private void SaveScreenshot(string filePath, byte[] bytes) {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllBytes(filePath, bytes);
        }

        private IEnumerator FrameIntervalRecordingCoroutine(int startFrame, int endFrame, bool enforceState, TaskCompletionSource<bool> tcs) {
            QuiltCaptureMode captureMode = (enforceState) ? QuiltCaptureMode.FrameInterval : CaptureMode;
            if (enforceState)
                CaptureMode = captureMode;

            int frameCount = endFrame - startFrame;
            Debug.Log("Recording will start after " + startFrame + " frame(s).");
            for (int i = 0; i < startFrame; i++)
                yield return new WaitForEndOfFrame();

            if (enforceState && CaptureMode != captureMode) {
                tcs.SetException(new TaskCanceledException());
                yield break;
            }
            StartRecordingInternal();

            Debug.Log("Recording will end after " + frameCount + " frame(s).");
            for (int i = 0; timing.FrameCount < frameCount; i++)
                yield return new WaitForEndOfFrame();

            if (enforceState && CaptureMode != captureMode) {
                tcs.SetException(new TaskCanceledException());
                yield break;
            }
            StopRecording();

            tcs.SetResult(true);
        }

        private IEnumerator TimeIntervalRecordingCoroutine(float startTime, float endTime, TaskCompletionSource<bool> tcs) {
            QuiltCaptureMode captureMode = QuiltCaptureMode.TimeInterval;
            CaptureMode = captureMode;

            Debug.Log("Recording will start after " + startTime + " second(s).");
            yield return new WaitForSeconds(startTime);

            if (CaptureMode != captureMode) {
                tcs.SetException(new TaskCanceledException());
                yield break;
            }
            StartRecordingInternal();

            float duration = endTime - startTime;
            Debug.Log("Recording will end after " + duration + " second(s).");
            for (int i = 0; timing.FrameCount < duration * timing.FrameRate; i++)
                yield return null;

            if (CaptureMode != captureMode) {
                tcs.SetException(new TaskCanceledException());
                yield break;
            }
            StopRecording();

            tcs.SetResult(true);
        }
        #endregion

        /// <summary>
        /// <para>Adds <see cref="QuiltCapture"/> metadata to the PNG file at the given <paramref name="pngFilePath"/>.</para>
        /// 
        /// <remarks>
        /// <para>
        /// PNG file metadata is unique to its own binary format, differs from media metadata encoded with FFmpeg, and is incompatible.<br />
        /// Use <a href="https://products.groupdocs.app/metadata/png">this website</a> to inspect PNG file metadata!
        /// </para>
        /// <para>
        /// It seems you can't view the metadata in Windows OS natively without some custom library or program.
        /// </para>
        /// </remarks>
        /// </summary>
        /// <param name="pngFilePath"></param>
        private async Task AddPNGMetadata(string pngFilePath) {
#if UNITY_EDITOR
            int progressId = Progress.Start("Add PNG Metadata", Path.GetFileName(pngFilePath), Progress.Options.Indefinite | Progress.Options.Synchronous);
#endif
            try {
                Task metadataTask = Task.Run(() => {
                    PngReader reader = FileHelper.CreatePngReader(pngFilePath);
                    PngWriter writer = null;
                    try {
                        ImageInfo info = reader.ImgInfo;
                        ImageLines line = reader.ReadRowsByte();
                        writer = FileHelper.CreatePngWriter(pngFilePath, info, true);

                        //NOTE: We might want to add extra standard metadata keys using the constants at PngChunkTextVar.KEY_XXX

                        MediaMetadataPair[] metadata = GetMediaMetadata();
                        for (int i = 0; i < metadata.Length; i++)
                            writer.GetMetadata().SetText(metadata[i].key, metadata[i].value);

                        writer.WriteRowsByte(line.ScanlinesB);
                    } finally {
                        if (writer != null)
                            writer.End();
                        if (reader != null)
                            reader.End();
                    }
                });

#if UNITY_EDITOR
                while (!metadataTask.IsCompleted) {
                    await Task.Delay(30);
                    Progress.Report(progressId, 0, 1, null);
                }
#else
                await metadataTask;
#endif

            } catch (Exception e) {
                Debug.LogException(e);
            } finally {
#if UNITY_EDITOR
                Progress.Finish(progressId);
#endif
            }

        }
    }
}
