using System;

namespace LookingGlass.Blocks {
    /// <summary>
    /// <para>Defines whether a hologram image or video is made from a quilt or an RGB-D format.</para>
    /// <para>`</para>
    /// </summary>
    [Serializable]
    public enum HologramType {
        QUILT,
        RGBD
    }
}