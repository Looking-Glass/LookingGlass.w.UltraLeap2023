using System;
using UnityEngine;

namespace LookingGlass {
    /// <summary>
    /// Contains several options, useful in the inspector, for debugging a <see cref="HologramCamera"/> component.
    /// </summary>
    [Serializable]
    public class HologramCameraDebugging : PropertyGroup {
        [NonSerialized] private bool wasShowingObjects = false;

        internal event Action onShowAllObjectsChanged;

        public bool ShowAllObjects {
            get { return hologramCamera.showAllObjects; }
            set {
                wasShowingObjects = hologramCamera.showAllObjects = value;
                onShowAllObjectsChanged?.Invoke();
            }
        }

        public int OnlyShowView {
            get { return hologramCamera.onlyShowView; }
            set { hologramCamera.onlyShowView = Mathf.Clamp(value, -1, hologramCamera.RenderSettings.numViews - 1); }
        }

        public bool OnlyRenderOneView {
            get { return hologramCamera.onlyRenderOneView; }
            set { hologramCamera.onlyRenderOneView = value; }
        }

        protected internal override void OnValidate() {
            if (ShowAllObjects != wasShowingObjects)
                ShowAllObjects = ShowAllObjects;

            OnlyShowView = OnlyShowView;
        }
    }
}