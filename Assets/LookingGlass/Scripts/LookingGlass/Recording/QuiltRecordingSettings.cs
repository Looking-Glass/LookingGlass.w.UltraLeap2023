using System;
using FFmpegOut;
using UnityEngine;

namespace LookingGlass {
    [Serializable]
    public struct QuiltRecordingSettings {
        internal static QuiltRecordingSettings Default => new QuiltRecordingSettings() {
            codec = FFmpegPreset.VP8Default,
            frameRate = 30,
            compression = 18,
            targetBitrateInMegabits = 60,
            cameraOverrideSettings = new QuiltCaptureOverrideSettings(DeviceType.Portrait)
        };

        public FFmpegPreset codec;
        public float frameRate;
        public int compression;
        public int targetBitrateInMegabits;
        public QuiltCaptureOverrideSettings cameraOverrideSettings;

        public QuiltRecordingSettings(FFmpegPreset preset, float frameRate, int compression, int targetBitrateInMegabits, QuiltCaptureOverrideSettings renderSettings) {
            this.codec = preset;
            this.frameRate = frameRate;
            this.compression = compression;
            this.targetBitrateInMegabits = targetBitrateInMegabits;
            this.cameraOverrideSettings = renderSettings;
        }

        public bool Equals(QuiltRecordingSettings source) {
            if (codec == source.codec &&
                frameRate == source.frameRate &&
                compression == source.compression &&
                targetBitrateInMegabits == source.targetBitrateInMegabits &&
                cameraOverrideSettings.Equals(source.cameraOverrideSettings))
                return true;
            return false;
        }

        private static readonly QuiltRecordingSettings[] PresetSettings = new QuiltRecordingSettings[] {
            new QuiltRecordingSettings(FFmpegPreset.VP8Default, 30, 20, 60, new QuiltCaptureOverrideSettings(DeviceType.Portrait)),
            new QuiltRecordingSettings(FFmpegPreset.VP8Default, 30, 15, 80, new QuiltCaptureOverrideSettings(DeviceType.Portrait)),
            new QuiltRecordingSettings(FFmpegPreset.VP8Default, 30, 20, 90, new QuiltCaptureOverrideSettings(DeviceType._16in)),
            new QuiltRecordingSettings(FFmpegPreset.VP8Default, 30, 15, 110, new QuiltCaptureOverrideSettings(DeviceType._16in)),
            new QuiltRecordingSettings(FFmpegPreset.VP9Default, 30, 20, 150, new QuiltCaptureOverrideSettings(DeviceType._32in)),
            new QuiltRecordingSettings(FFmpegPreset.VP9Default, 30, 15, 200, new QuiltCaptureOverrideSettings(DeviceType._32in)),
            new QuiltRecordingSettings(FFmpegPreset.VP9Default, 30, 20, 150, new QuiltCaptureOverrideSettings(DeviceType._65in)),
            new QuiltRecordingSettings(FFmpegPreset.VP9Default, 30, 15, 200, new QuiltCaptureOverrideSettings(DeviceType._65in))
        };

        public static QuiltRecordingSettings GetSettings(QuiltRecordingPreset preset) {
            if (preset == QuiltRecordingPreset.Custom)
                return PresetSettings[0];

            int index = (int) preset;
            return PresetSettings[index];
        }
    }
}
