//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Assertions;

namespace LookingGlass {
    [Serializable]
    public struct Calibration {
        public const int XPOS_DEFAULT = 0;
        public const int YPOS_DEFAULT = 0;
        public const string DEFAULT_NAME = "PORT";
        public const float DEFAULT_VIEWCONE = 40;

        //NOTE: (?i) is removed with (?-i)
        internal static readonly Dictionary<Regex, DeviceType> AutomaticSerialPatterns = new Dictionary<Regex, DeviceType>() {
            { new Regex("(?i)(Looking Glass - Portrait)|(PORT)|(Portrait)"),    DeviceType.Portrait },
            { new Regex("(?i)(Looking Glass - 16\")|(LKG-A)|(LKG-4K)"),         DeviceType._16in },
            { new Regex("(?i)(Looking Glass - 32\")|(LKG-B)|(LKG-8K)"),         DeviceType._32in },
            { new Regex("(?i)(Looking Glass - 65\")|(LKG-D)"),                  DeviceType._65in },
            { new Regex("(?i)(Looking Glass - 8.9\")|(LKG-2K)"),                DeviceType._8_9inLegacy },
        };

        [Tooltip("The device index used by LookingGlass Service (HoPS).")]
        public int index;

        public int unityIndex;
        public int screenWidth;
        public int screenHeight;
        public float subp;
        public float viewCone;
        
        [Tooltip("The device's native aspect ratio (screenWidth / screenHeight).\n" +
            "This does NOT necessarily match the aspect ratio for the single-view tiles in quilts that can be rendered to/from the device.\n" +
            "When these aspect ratios differ, ")]
        public float aspect;

        public float pitch;
        public float slope;
        public float center;
        public float fringe;
        public string serial;
        public string LKGname;

        public int xpos;
        public int ypos;

        public float rawSlope;
        public float flipImageX;
        public float dpi;

        public bool IsValid =>  screenWidth > 0 && screenHeight > 0 && !string.IsNullOrWhiteSpace(LKGname);
        //public bool IsPortrait => string.IsNullOrEmpty(serial) || serial.Contains("PORT") || serial.Contains("Portrait");
        public DeviceType GetDeviceType() {
            if (string.IsNullOrEmpty(serial))
                return DeviceType.Portrait;

            foreach (KeyValuePair<Regex, DeviceType> pair in AutomaticSerialPatterns)
                if (pair.Key.IsMatch(serial))
                    return pair.Value;
            
            Debug.LogError("Unknown LKG device by serial field! (serial = \"" + serial + "\")");
            return DeviceType.Portrait;
        }

        /// <summary>
        /// The actual, calculated aspect ratio of the renderer, using <see cref="screenWidth"/> and <see cref="screenHeight"/>.<br />
        /// Use this if the <see cref="aspect"/> field was modified or -1, to re-calculate the actual aspect ratio.
        /// </summary>
        public float DefaultAspect => RecalculateAspect(screenWidth, screenHeight);

        public Calibration(
            int index,
            int unityIndex,
            int screenWidth,
            int screenHeight,
            float subp,
            float viewCone,
            float aspect,
            float pitch,
            float slope,
            float center,
            float fringe,
            string serial,
            string LKGname,
            int xpos,
            int ypos,
            float rawSlope,
            float flipImageX,
            float dpi
            ) {

            this.index = index;
            this.unityIndex = unityIndex;
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            this.subp = subp;
            this.viewCone = viewCone;
            this.aspect = aspect;
            this.pitch = pitch;
            this.slope = slope;
            this.center = center;
            this.fringe = fringe;
            this.serial = serial;
            this.LKGname = LKGname;
            this.xpos = xpos;
            this.ypos = ypos;
            this.rawSlope = rawSlope;
            this.flipImageX = flipImageX;
            this.dpi = dpi;
        }

        public Calibration(int index) {
            this.index = index;

            DeviceType defaultType = DeviceType.Portrait;
            DeviceSettings settings = DeviceSettings.Get(defaultType);
            screenWidth = settings.screenWidth;
            screenHeight = settings.screenHeight;
            subp = 0;
            viewCone = 0;
            aspect = (float) this.screenWidth / this.screenHeight;
            pitch = 10;
            slope = 1;
            center = 0;
            fringe = 0;
            serial = DEFAULT_NAME;
            LKGname = "";
            unityIndex = 0;
            xpos = XPOS_DEFAULT;
            ypos = YPOS_DEFAULT;
            rawSlope = 0;
            flipImageX = 0;
            dpi = 0;
        }

        public Calibration(int index, int screenWidth, int screenHeight)
            : this(index, 0, screenWidth, screenHeight, 0, 0, (float) screenWidth / screenHeight,
            1, 1, 0, 0, DEFAULT_NAME, "", XPOS_DEFAULT, YPOS_DEFAULT, 0, 0, 0
            ) { }

        /// <summary>
        /// A helper method for getting the aspect ratio of the renderer.
        /// </summary>
        /// <returns>
        /// The <see cref="aspect"/> field if it is greater than zero, or <see cref="DefaultAspect"/> otherwise.
        /// </returns>
        public float GetAspect() {
            if (aspect > 0)
                return aspect;
            return DefaultAspect;
        }

        public Calibration CopyWithCustomResolution(int xpos, int ypos, int renderWidth, int renderHeight) {
            Assert.IsTrue(typeof(Calibration).IsValueType, "The copy below assumes that "
                + nameof(Calibration) + " is a value type (struct), so the single equals operator creates a deep copy!");

            Calibration copy = this;
            copy.xpos = xpos;
            copy.ypos = ypos;
            copy.screenWidth = renderWidth;
            copy.screenHeight = renderHeight;

            //Some properties like slope and pitch must be adjusted according to the new width and height we want to render to on the LKG display!
            copy.subp = RecalculateSubpixelSize(renderWidth, flipImageX);
            copy.aspect = RecalculateAspect(renderWidth, renderHeight);
            copy.pitch = RecalculatePitch(index, renderWidth, dpi, rawSlope);
            copy.slope = RecalculateSlope(renderWidth, renderHeight, rawSlope, flipImageX);
            return copy;
        }

        //TODO: The C++ implementation in HoloPlayCore could be improved to contain this functionality,
        //then we could remove these methods to avoid subverting the calibration provided by Bridge in this UnityPlugin!
        //See: https://github.com/Looking-Glass/HoloPlayCore/blob/56bc5c08e55dfd3742d3b745045cfb3b370190b8/libHoloPlayCore.cpp
        #region Calibration Recalculations
        public static float GetFlipMultiplier(float flipImageX) => (flipImageX > 0.5f ? -1 : 1);

        private static float RecalculateSubpixelSize(int renderWidth, float flipImageX) {
            float subpixelSize = 1 / ((float) renderWidth * 3) * GetFlipMultiplier(flipImageX);
            return subpixelSize;
        }

        public static float RecalculateAspect(int renderWidth, int renderHeight) => (renderHeight == 0) ? 0 : (float) renderWidth / renderHeight;

        private static float RecalculatePitch(int deviceIndex, float renderWidth, float dpi, float rawSlope) {
            float pitch = PluginCore.hpc_GetDevicePropertyFloat(deviceIndex, "/calibration/pitch/value");
            pitch *= renderWidth / dpi * Mathf.Cos(Mathf.Atan(1 / rawSlope));
            return pitch;
        }

        private static float RecalculateSlope(int renderWidth, int renderHeight, float rawSlope, float flipImageX) {
            float slope = renderHeight / (renderWidth * rawSlope) * GetFlipMultiplier(flipImageX);
            return slope;
        }
        #endregion
    }
}
