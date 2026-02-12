using System.Text.RegularExpressions;
using RadioDecadance.Tools;
using UnityEditor;
using UnityEngine;

namespace RadioDecadance.Serialization.Tools.Editor
{
    [CustomPropertyDrawer(typeof(RegexStringAttribute))]
    public class RegexStringDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            RegexStringAttribute regexAttribute = (RegexStringAttribute)attribute;
            bool isValid = Regex.IsMatch(property.stringValue ?? "", regexAttribute.Regex);

            float iconSize = EditorGUIUtility.singleLineHeight;
            Rect fieldRect = new Rect(position.x, position.y, position.width, position.height);
            
            if (!isValid)
            {
                fieldRect.width -= iconSize + 4;
            }

            Color originalColor = GUI.backgroundColor;
            if (!isValid)
            {
                GUI.backgroundColor = Color.red;
            }

            EditorGUI.PropertyField(fieldRect, property, label);

            if (!isValid)
            {
                GUI.backgroundColor = originalColor;
                Rect iconRect = new Rect(fieldRect.xMax + 4, position.y, iconSize, iconSize);
                
                GUIContent iconContent = EditorGUIUtility.IconContent("console.erroricon.sml");
                iconContent.tooltip = regexAttribute.Tooltip;
                
                GUI.Label(iconRect, iconContent);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label);
        }
    }
}
