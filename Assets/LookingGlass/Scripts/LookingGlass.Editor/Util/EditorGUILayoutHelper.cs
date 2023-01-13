// Thanks to https://github.com/needle-mirror/com.unity.recorder/blob/master/Editor/Sources/Helpers/UIElementHelper.cs
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;

namespace LookingGlass.Editor {
    internal static class EditorGUILayoutHelper {
        public static bool MultiIntField(GUIContent label, GUIContent[] subLabels, int[] values) {
            Rect r = EditorGUILayout.GetControlRect();

            Rect rLabel = r;
            rLabel.width = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(rLabel, label);

            Rect rContent = r;
            rContent.xMin = rLabel.xMax;

            float width = subLabels.Select(l => GUI.skin.label.CalcSize(l).x).Max();

            EditorGUI.BeginChangeCheck();
            MultiIntField(rContent, subLabels, values, width);
            return EditorGUI.EndChangeCheck();
        }

        public static bool MultiFloatField(GUIContent label, GUIContent[] subLabels, float[] values) {
            Rect r = EditorGUILayout.GetControlRect();

            Rect rLabel = r;
            rLabel.width = EditorGUIUtility.labelWidth;
            EditorGUI.LabelField(rLabel, label);

            Rect rContent = r;
            rContent.xMin = rLabel.xMax;

            float width = subLabels.Select(l => GUI.skin.label.CalcSize(l).x).Max();

            EditorGUI.BeginChangeCheck();
            MultiFloatField(rContent, subLabels, values, width);
            return EditorGUI.EndChangeCheck();
        }

        private static void MultiIntField(Rect position, IList<GUIContent> subLabels, IList<int> values, float labelWidth) {
            int length = values.Count;
            float num = (position.width - (float) (length - 1) * 2f) / (float) length;
            Rect position1 = new Rect(position) {
                width = num
            };
            float labelWidth1 = EditorGUIUtility.labelWidth;
            int indentLevel = EditorGUI.indentLevel;

            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = 0;
            for (int index = 0; index < values.Count; ++index) {
                values[index] = EditorGUI.IntField(position1, subLabels[index], values[index]);
                position1.x += num + 2f;
            }
            EditorGUIUtility.labelWidth = labelWidth1;
            EditorGUI.indentLevel = indentLevel;
        }

        private static void MultiFloatField(Rect position, IList<GUIContent> subLabels, IList<float> values, float labelWidth) {
            int length = values.Count;
            float num = (position.width - (float) (length - 1) * 2f) / (float) length;
            Rect position1 = new Rect(position) {
                width = num
            };
            float labelWidth1 = EditorGUIUtility.labelWidth;
            int indentLevel = EditorGUI.indentLevel;
            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUI.indentLevel = 0;
            for (int index = 0; index < values.Count; ++index) {
                values[index] = EditorGUI.FloatField(position1, subLabels[index], values[index]);
                position1.x += num + 2f;
            }
            EditorGUIUtility.labelWidth = labelWidth1;
            EditorGUI.indentLevel = indentLevel;
        }

        public static void ReadOnlyRenderSettings(HologramRenderSettings renderSettings) {
            EditorGUILayout.LabelField("Quilt Size: ", renderSettings.quiltWidth + " x " + renderSettings.quiltHeight);
            EditorGUILayout.LabelField("View Size: ", renderSettings.ViewWidth + " x " + renderSettings.ViewHeight);
            EditorGUILayout.LabelField("Tiling: ", renderSettings.viewColumns + " x " + renderSettings.viewRows);
            EditorGUILayout.LabelField("Views Total: ", renderSettings.numViews.ToString());
            EditorGUILayout.LabelField("Aspect: ", renderSettings.aspect.ToString());
        }
    }
}
