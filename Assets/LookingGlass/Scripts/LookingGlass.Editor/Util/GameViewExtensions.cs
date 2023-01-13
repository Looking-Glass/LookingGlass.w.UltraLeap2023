using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace LookingGlass.Editor {
    public static class GameViewExtensions {
        public static EditorWindow[] FindUnpairedGameViews() => FindUnpairedGameViewsInternal().ToArray();

        private static IEnumerable<EditorWindow> FindUnpairedGameViewsInternal() {
            foreach (EditorWindow gameView in LookingGlass.GameViewExtensions.FindAllGameViews())
                if (!PreviewPairs.IsPaired(gameView))
                    yield return gameView;
        }
    }
}
