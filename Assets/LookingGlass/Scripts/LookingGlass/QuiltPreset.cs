//Copyright 2017-2021 Looking Glass Factory Inc.
//All rights reserved.
//Unauthorized copying or distribution of this file, and the source code contained herein, is strictly prohibited.
using System;
using UnityEngine;

namespace LookingGlass {
    [Serializable]
    public enum QuiltPreset {
        Custom = -2,
        Automatic = -1,

        [InspectorName("LKG Portrait")] Portrait = 0,
        [InspectorName("LKG 16\"")] _16in = 1,
        [InspectorName("LKG 32\"")] _32in = 2,
        [InspectorName("LKG 65\"")] _65in = 3,
        [InspectorName("LKG 8.9\" (Legacy)")] _8_9inLegacy = 4
    }

}