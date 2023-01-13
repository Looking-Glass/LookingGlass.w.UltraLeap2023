using System;

namespace LookingGlass {
    public static class QuiltScreenshot {
        public static QuiltCaptureOverrideSettings GetSettings(QuiltScreenshotPreset preset) {
            switch (preset) {
                case QuiltScreenshotPreset.Portrait: return QuiltRecordingSettings.GetSettings(QuiltRecordingPreset.LookingGlassPortraitStandardQuality).cameraOverrideSettings;
                case QuiltScreenshotPreset.LookingGlass16: return QuiltRecordingSettings.GetSettings(QuiltRecordingPreset.LookingGlass16StandardQuality).cameraOverrideSettings;
                case QuiltScreenshotPreset.LookingGlass32: return QuiltRecordingSettings.GetSettings(QuiltRecordingPreset.LookingGlass32StandardQuality).cameraOverrideSettings;
                case QuiltScreenshotPreset.LookingGlass65: return QuiltRecordingSettings.GetSettings(QuiltRecordingPreset.LookingGlass65StandardQuality).cameraOverrideSettings;
            }
            throw new NotSupportedException("Unsupported preset type: " + preset);
        }
    }
}
