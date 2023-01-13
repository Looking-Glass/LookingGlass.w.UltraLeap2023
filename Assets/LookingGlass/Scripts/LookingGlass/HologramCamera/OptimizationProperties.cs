using System;
using UnityEngine;

namespace LookingGlass {
    [Serializable]
    public class OptimizationProperties : PropertyGroup {
        public HologramCamera.ViewInterpolationType ViewInterpolation {
            get { return hologramCamera.viewInterpolation; }
            set { hologramCamera.viewInterpolation = value; }
        }

        //TODO: Better document what this means.. the API isn't that self-descriptive.
        public int GetViewInterpolation(int numViews) {
            switch (hologramCamera.viewInterpolation) {
                case HologramCamera.ViewInterpolationType.None:
                default:
                    return 1;
                case HologramCamera.ViewInterpolationType.EveryOther:
                    return 2;
                case HologramCamera.ViewInterpolationType.Every4th:
                    return 4;
                case HologramCamera.ViewInterpolationType.Every8th:
                    return 8;
                case HologramCamera.ViewInterpolationType._4Views:
                    return numViews / 3;
                case HologramCamera.ViewInterpolationType._2Views:
                    return numViews;
            }
        }

        public bool ReduceFlicker {
            get { return hologramCamera.reduceFlicker; }
            set { hologramCamera.reduceFlicker = value; }
        }

        public bool FillGaps {
            get { return hologramCamera.fillGaps; }
            set { hologramCamera.fillGaps = value; }
        }

        public bool BlendViews {
            get { return hologramCamera.blendViews; }
            set { hologramCamera.blendViews = value; }
        }
    }
}
