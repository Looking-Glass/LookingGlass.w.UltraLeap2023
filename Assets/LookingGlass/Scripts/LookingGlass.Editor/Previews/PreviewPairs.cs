using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;

namespace LookingGlass.Editor {
    public static class PreviewPairs {
        private static Dictionary<HologramCamera, PreviewWindow> all = new Dictionary<HologramCamera, PreviewWindow>();

        private static bool isEnumerating = false;
        private static List<HologramCamera> toRemove = new List<HologramCamera>();

        public static int Count => all.Count;
        public static IEnumerable<KeyValuePair<HologramCamera, PreviewWindow>> All {
            get {
                Clean();
                isEnumerating = true;
                try {
                    foreach (HologramCamera h in all.Keys) {
                        PreviewWindow preview = all[h];
                        Assert.IsNotNull(preview);
                        yield return new KeyValuePair<HologramCamera, PreviewWindow>(h, preview);
                    }
                } finally {
                    isEnumerating = false;
                }

                foreach (HologramCamera h in toRemove)
                    all.Remove(h);
                toRemove.Clear();
            }
        }

        public static bool IsPaired(EditorWindow gameView) {
            if (gameView == null)
                throw new ArgumentNullException(nameof(gameView));

            foreach (KeyValuePair<HologramCamera, PreviewWindow> pair in All) {
                EditorWindow other = pair.Value.GameView;
                if (other != null && other == gameView)
                    return true;
            }
            return false;
        }

        public static PreviewWindow GetPreview(HologramCamera hologramCamera) {
            if (all.TryGetValue(hologramCamera, out PreviewWindow preview))
                return preview;
            return null;
        }

        public static PreviewWindow Create(HologramCamera hologramCamera) {
            if (all.ContainsKey(hologramCamera))
                throw new InvalidOperationException(hologramCamera + " already has a game view created for it!");

            PreviewWindow preview = PreviewWindow.Create(hologramCamera);
            if (preview == null)
                return null;

            all.Add(hologramCamera, preview);
            return preview;
        }

        private static void Clean() {
            isEnumerating = true;
            try {
                foreach (HologramCamera h in all.Keys) {
                    PreviewWindow preview = all[h];
                    if (preview == null || preview.GameView == null) {
                        toRemove.Add(h);
                    }
                }
            } finally {
                isEnumerating = false;
            }

            foreach (HologramCamera h in toRemove) {
                //NOTE: Do NOT destroy the LookingGlass component! Destroy the preview window for it.
                PreviewWindow preview = all[h];
                if (preview != null)
                    ScriptableObject.DestroyImmediate(preview);
                all.Remove(h);
            }
            toRemove.Clear();
        }

        public static bool IsPreviewOpenForDevice(string lkgName) {
            Clean();
            foreach (HologramCamera h in all.Keys)
                if (h.TargetLKGName == lkgName)
                    return true;
            return false;
        }

        public static void Close(HologramCamera hologramCamera) {
            if (!all.TryGetValue(hologramCamera, out PreviewWindow preview)) {
                Debug.LogError("Failed to close " + hologramCamera + "'s window! It couldn't be found.");
                return;
            }
            ScriptableObject.DestroyImmediate(preview);

            if (isEnumerating) {
                toRemove.Add(hologramCamera);
            } else {
                all.Remove(hologramCamera);
            }
        }

        public static void CloseAll() {
            if (isEnumerating) {
                Debug.LogError("Failed to close all LookingGlass game views: They're currently being enumerated, so the dictionary collection cannot be modified!");
                return;
            }

            foreach (PreviewWindow preview in all.Values) {
                try {
                    ScriptableObject.DestroyImmediate(preview);
                } catch (Exception e) {
                    Debug.LogException(e);
                }
            }
            all.Clear();
            toRemove.Clear();
        }
    }
}