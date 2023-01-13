//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;

namespace LookingGlass {
    [Serializable]
    public struct HologramRenderSettings : ISerializationCallbackReceiver {
        public const int MinSize = 256;
        public const int MaxSize = 8192 * 2;
        public const int MinRowColumnCount = 1;
        public const int MaxRowColumnCount = 32;
        public const int MinViews = 1;
        public const int MaxViews = 128;

        //WARNING: There are still public-exposed fields!
        [Range(MinSize, MaxSize)]
        public int quiltWidth;

        [Range(MinSize, MaxSize)]
        public int quiltHeight;

        [Range(MinRowColumnCount, MaxRowColumnCount)]
        public int viewColumns;

        [Range(MinRowColumnCount, MaxRowColumnCount)]
        public int viewRows;

        [Range(MinViews, MaxViews)]
        public int numViews;

        [Min(-1)]
        [Tooltip("The aspect ratio (width / height) used to render the individual single views (tiles) of this quilt.\n" +
            "NOTE: This does NOT change the width or height of the single view tiles, but WILL distort and stretch the pixels in each single view tile.\n" +
            "Set this value to -1 to use the aspect given by the LKG device's calibration data instead.")]
        public float aspect;

        private int viewWidth;
        private int viewHeight;
        private int paddingHorizontal;
        private int paddingVertical;
        private float viewPortionHorizontal;
        private float viewPortionVertical;

        [NonSerialized] private float previousAspect;

        internal event Action onAspectChanged;

        public int ViewWidth => viewWidth;
        public int ViewHeight => viewHeight;
        public int PaddingHorizontal => paddingHorizontal;
        public int PaddingVertical => paddingVertical;
        public float ViewPortionHorizontal => viewPortionHorizontal;
        public float ViewPortionVertical => viewPortionVertical;

        public HologramRenderSettings(
            int quiltWidth,
            int quiltHeight,
            int viewColumns,
            int viewRows,
            int numViews,
            DeviceType deviceType
            ) : this(quiltWidth, quiltHeight, viewColumns, viewRows, numViews, DeviceSettings.Get(deviceType).nativeAspect) { }

        public HologramRenderSettings(
            int quiltWidth,
            int quiltHeight,
            int viewColumns,
            int viewRows,
            int numViews,
            float aspect) : this() {

            this.quiltWidth = quiltWidth;
            this.quiltHeight = quiltHeight;
            this.viewColumns = Mathf.Clamp(viewColumns, MinRowColumnCount, MaxRowColumnCount);
            this.viewRows = Mathf.Clamp(viewRows, MinRowColumnCount, MaxRowColumnCount);
            this.numViews = numViews;
            this.aspect = aspect;

            Setup();
        }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() {
            Setup();
            if (aspect != previousAspect) {
                previousAspect = aspect;
                onAspectChanged?.Invoke();
            }
        }

        public float GetAspectOrDefault() {
            float result = (aspect > 0) ? aspect : GetViewAspect();
            Assert.IsTrue(result > 0, nameof(GetAspectOrDefault) + " should always return a value greater than zero! Instead, it was " + result + "! (aspect = " + aspect + ")");
            return result;
        }

        public float GetViewAspect() {
            float w = ViewWidth;
            float h = ViewHeight;
            if (w <= 0 || h <= 0)
                throw new InvalidOperationException(nameof(ViewWidth) + " or " + nameof(ViewHeight) + " were zero or negative! " +
                    "Make sure the " + nameof(quiltWidth) + " and " + nameof(quiltHeight) + " are greater than zero, and that " + nameof(Setup) + " is called.");

            float viewAspect = w / h;
            Assert.IsTrue(viewAspect > 0);
            return viewAspect;
        }

        public void Setup() {
            if (viewColumns == 0 || viewRows == 0) {
                viewWidth = quiltWidth;
                viewHeight = quiltHeight;
            } else {
                viewWidth = quiltWidth / viewColumns;
                viewHeight = quiltHeight / viewRows;
            }
            paddingHorizontal = quiltWidth - viewColumns *viewWidth;
            paddingVertical = quiltHeight - viewRows * viewHeight;
            viewPortionHorizontal = (float) viewColumns * viewWidth / (float) quiltWidth;
            viewPortionVertical = (float) viewRows * viewHeight / (float) quiltHeight;
        }

        public bool Equals(HologramRenderSettings otherRenderSettings) {
            if (quiltWidth == otherRenderSettings.quiltWidth
                && quiltHeight == otherRenderSettings.quiltHeight
                && viewColumns == otherRenderSettings.viewColumns
                && viewRows == otherRenderSettings.viewRows
                && numViews == otherRenderSettings.numViews
                && aspect == otherRenderSettings.aspect)
                return true;
            return false;
        }
        
        private static readonly HologramRenderSettings[] PresetSettings = new HologramRenderSettings[] {
            new HologramRenderSettings(3360, 3360, 8, 6, 48,      DeviceType.Portrait),       //QuiltPreset.Portrait
            new HologramRenderSettings(4096, 4096, 5, 9, 45,      DeviceType._16in),          //QuiltPreset._16in
            new HologramRenderSettings(8192, 8192, 5, 9, 45,      DeviceType._32in),          //QuiltPreset._32in
            new HologramRenderSettings(8192, 8192, 8, 9, 72,      DeviceType._65in),          //QuiltPreset._65in
            new HologramRenderSettings(4096, 4096, 5, 9, 45,      DeviceType._8_9inLegacy),   //QuiltPreset._8_9inLegacy
        };

        //WARNING: The calibration data should be UNMODIFIED so that its screenWidth and screenHeight are matched exactly!
        //Otherwise, it'll incorrectly default to Portrait quilt RenderSettings.
        public static QuiltPreset CalculateAutomaticQuiltPreset(Calibration calibration) => DeviceSettings.Get(calibration.GetDeviceType()).quiltPreset;

        public static int PresetCount => PresetSettings.Length;
        public static IEnumerable<HologramRenderSettings> GetAll() {
            foreach (HologramRenderSettings QuiltPreset in PresetSettings)
                yield return QuiltPreset;
        }

        public static HologramRenderSettings Get(QuiltPreset preset) {
            if (preset == QuiltPreset.Automatic)
                throw new ArgumentException("You must supply a calibration value when using automatic QuiltPreset.");
            return PresetSettings[(int) preset];
        }

        public static HologramRenderSettings Get(QuiltPreset preset, Calibration calibration) {
            QuiltPreset actualQuiltPresetToUse = preset;
            if (preset == QuiltPreset.Automatic)
                actualQuiltPresetToUse = CalculateAutomaticQuiltPreset(calibration);

            return PresetSettings[(int) actualQuiltPresetToUse];
        }

        /// <summary>
        /// Gets a copy of the default <see cref="HologramRenderSettings"/> for a specific device.
        /// </summary>
        /// <param name="device">The type of LKG device to get the default RenderSettings of.</param>
        public static HologramRenderSettings Get(DeviceType device) {
            DeviceSettings deviceRenderSettings = DeviceSettings.Get(device);
            HologramRenderSettings renderSettings = PresetSettings[(int) deviceRenderSettings.quiltPreset];

            //NOTE: We force the aspect ratio to be that of emulated device,
            //  because it defaults to what it reads from calibration.
            renderSettings.aspect = deviceRenderSettings.nativeAspect;
            return renderSettings;
        }
    }

    
}