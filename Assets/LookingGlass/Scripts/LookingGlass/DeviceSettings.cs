//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LookingGlass{
    [Serializable]
    public struct DeviceSettings {
        public string name;
        public int screenWidth;
        public int screenHeight;
        public float nativeAspect;
        public float nearClip;
        public QuiltPreset quiltPreset;

        public DeviceSettings(string name, int screenWidth, int screenHeight, float nearClip, QuiltPreset quiltPreset) {
            this.name = name;
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            nativeAspect = (float) screenWidth / screenHeight;
            this.nearClip = nearClip;
            this.quiltPreset = quiltPreset;
        }
        private static readonly DeviceSettings[] Presets = new DeviceSettings[] {
            new DeviceSettings("Looking Glass - Portrait",          1536, 2048, 0.5f, QuiltPreset.Portrait),
            new DeviceSettings("Looking Glass - 16\"",              3840, 2160, 1.5f, QuiltPreset._16in),
            new DeviceSettings("Looking Glass - 32\"",              7680, 4320, 1.5f, QuiltPreset._32in),
            new DeviceSettings("Looking Glass - 65\"",              7680, 4320, 1.5f, QuiltPreset._65in),
            new DeviceSettings("Looking Glass - 8.9\" (Legacy)",    2560, 1600, 1.5f, QuiltPreset._8_9inLegacy)
        };

        public static DeviceSettings DefaultSettings => Presets[0];

        public static int PresetCount => Presets.Length;
        public static IEnumerable<DeviceSettings> GetAll() {
            foreach (DeviceSettings preset in Presets)
                yield return preset;
        }

        public static DeviceSettings Get(DeviceType deviceType) {
            return Presets[(int) deviceType];
        }

        public static string GetName(Calibration cal) {
            foreach (DeviceSettings preset in Presets)
                if (cal.screenWidth == preset.screenWidth && cal.screenHeight == preset.screenHeight)
                    return preset.name;
            return DefaultSettings.name;
        }
    }


}