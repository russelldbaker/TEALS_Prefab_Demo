using UnityEditor;
using UnityEngine;

namespace Unity.Tutorials.Core.Editor
{
    [CustomPropertyDrawer(typeof(GuiControlSelector))]
    class GuiControlSelectorDrawer : PropertyDrawer
    {
        private const string k_SelectorModePath = "m_SelectorMode";
        private const string k_GUIContentPath = "m_GUIContent";
        private const string k_ControlNamePath = "m_ControlName";
        private const string k_PropertyPathPath = "m_PropertyPath";
        private const string k_TargetTypePath = "m_TargetType";
        private const string k_GUIStyleNamePath = "m_GUIStyleName";
        private const string k_ObjectReferencePath = "m_ObjectReference";

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var selectorMode = property.FindPropertyRelative(k_SelectorModePath);
            var height = EditorGUI.GetPropertyHeight(selectorMode);
            switch ((GuiControlSelector.Mode)selectorMode.intValue)
            {
                case GuiControlSelector.Mode.GuiContent:
                    height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(k_GUIContentPath), true);
                    break;
                case GuiControlSelector.Mode.NamedControl:
                    height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(k_ControlNamePath), true);
                    break;
                case GuiControlSelector.Mode.GuiStyleName:
                    height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(k_GUIStyleNamePath), true);
                    break;
                case GuiControlSelector.Mode.Property:
                    height +=
                        EditorGUIUtility.standardVerticalSpacing +
                        EditorGUI.GetPropertyHeight(property.FindPropertyRelative(k_TargetTypePath), true) +
                        EditorGUI.GetPropertyHeight(property.FindPropertyRelative(k_PropertyPathPath), true);
                    break;
                case GuiControlSelector.Mode.ObjectReference:
                    height += EditorGUIUtility.standardVerticalSpacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative(k_ObjectReferencePath), true);
                    break;
                default:
                    height += EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight;
                    break;
            }
            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var selectorMode = property.FindPropertyRelative(k_SelectorModePath);
            position.height = EditorGUI.GetPropertyHeight(selectorMode);
            EditorGUI.PropertyField(position, selectorMode);

            position.y += position.height + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty selectorData = null;
            switch ((GuiControlSelector.Mode)selectorMode.intValue)
            {
                case GuiControlSelector.Mode.GuiContent:
                    selectorData = property.FindPropertyRelative(k_GUIContentPath);
                    break;
                case GuiControlSelector.Mode.NamedControl:
                    selectorData = property.FindPropertyRelative(k_ControlNamePath);
                    break;
                case GuiControlSelector.Mode.Property:
                    var targetType = property.FindPropertyRelative(k_TargetTypePath);
                    position.height = EditorGUI.GetPropertyHeight(targetType);
                    EditorGUI.PropertyField(position, targetType);
                    position.y += position.height + EditorGUIUtility.standardVerticalSpacing;

                    selectorData = property.FindPropertyRelative(k_PropertyPathPath);
                    break;
                case GuiControlSelector.Mode.GuiStyleName:
                    selectorData = property.FindPropertyRelative(k_GUIStyleNamePath);
                    break;
                case GuiControlSelector.Mode.ObjectReference:
                    selectorData = property.FindPropertyRelative(k_ObjectReferencePath);
                    break;
            }
            if (selectorData != null)
            {
                position.height = EditorGUI.GetPropertyHeight(selectorData, true);
                EditorGUI.PropertyField(position, selectorData, true);
            }
            else
            {
                position.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.HelpBox(
                    position,
                    string.Format("No drawing implemented yet for selector mode {0}", (GuiControlSelector.Mode)selectorMode.intValue),
                    MessageType.Error
                );
            }
        }
    }
}
