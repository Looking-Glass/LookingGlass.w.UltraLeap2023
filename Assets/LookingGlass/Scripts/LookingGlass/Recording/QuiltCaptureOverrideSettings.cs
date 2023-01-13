using System;
using UnityEngine;

namespace LookingGlass {
    /// <summary>
    /// Represents the settings that a <see cref="QuiltCapture"/> may use when recording, instead of using the values on its <see cref="HologramCamera"/> component.
    /// </summary>
    [Serializable]
    public struct QuiltCaptureOverrideSettings {
        [Tooltip("The quilt settings to use during recording.")]
        public HologramRenderSettings renderSettings;

        [Tooltip("The near clip factor to use during recording.")]
        public float nearClipFactor;

        public QuiltCaptureOverrideSettings(DeviceType type) {
            renderSettings = HologramRenderSettings.Get(type);
            nearClipFactor = DeviceSettings.Get(type).nearClip;
        }

        public QuiltCaptureOverrideSettings(HologramCamera source) {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            renderSettings = source.RenderSettings;
            nearClipFactor = source.CameraProperties.NearClipFactor;  //TODO: We need to handle the case of TransformMode.Camera using nearClipPlane instead!
        }

        public bool Equals(QuiltCaptureOverrideSettings source) {
            if (renderSettings.Equals(source.renderSettings)
                && nearClipFactor == source.nearClipFactor)
                return true;
            return false;
        }
    }
}
