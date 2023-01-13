using System;

namespace LookingGlass {
    [Serializable]
    public enum QuiltRecordingPreset {
        Custom = -2,
        Automatic = -1,
        LookingGlassPortraitStandardQuality = 0,
        LookingGlassPortraitHighQuality = 1,
        LookingGlass16StandardQuality,
        LookingGlass16HighQuality,
        LookingGlass32StandardQuality,
        LookingGlass32HighQuality,
        LookingGlass65StandardQuality,
        LookingGlass65HighQuality
    }
}
