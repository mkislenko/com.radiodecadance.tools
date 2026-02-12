#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RadioDecadance.Tools;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace RadioDecadance.Serialization.Tools.Editor
{
    [CustomPropertyDrawer(typeof(SerializeReferenceTypePickerAttribute))]
    public class SerializeReferenceTypePickerDrawer : PropertyDrawer
    {
        private const float ButtonWidth = 200f;
        private const string NullDisplay = "<null>";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            // Pure UI Toolkit implementation (no IMGUI) so it works reliably when nested.
            var root = new VisualElement();
            root.style.flexGrow = 1f;

            if (property == null)
                return root;

            // Build the appropriate UI depending on property kind
            var ve = BuildUITKForProperty(property, (SerializeReferenceTypePickerAttribute)attribute);
            root.Add(ve);

            // Bind to the same serialized object so fields update automatically
            root.Bind(property.serializedObject);
            return root;
        }

        private static readonly Dictionary<Type, Type[]> s_cachedTypes = new();

        private VisualElement BuildUITKForProperty(SerializedProperty property, SerializeReferenceTypePickerAttribute attr)
        {
            // Arrays (non-managed): draw as UITK list
            if (property.isArray && property.propertyType != SerializedPropertyType.ManagedReference)
            {
                return BuildArrayUI(property, attr, property.displayName);
            }

            // ManagedReference holding a List<T>
            if (property.propertyType == SerializedPropertyType.ManagedReference && IsDeclaredListType(property, out var listElementType))
            {
                return BuildManagedListUI(property, attr, listElementType, property.displayName);
            }

            // Single managed reference
            return BuildManagedRefUI(property, attr, property.displayName);
        }

        private VisualElement BuildArrayUI(SerializedProperty arrayProp, SerializeReferenceTypePickerAttribute attr, string label)
        {
            var root = new VisualElement();
            root.style.flexGrow = 1f;

            var foldout = new Foldout { text = label, value = arrayProp.isExpanded };
            foldout.RegisterValueChangedCallback(evt =>
            {
                arrayProp.serializedObject.Update();
                arrayProp.isExpanded = evt.newValue;
                arrayProp.serializedObject.ApplyModifiedProperties();
            });
            root.Add(foldout);

            var body = new VisualElement();
            body.style.marginLeft = 16;
            foldout.Add(body);

            var sizeRow = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var sizeLabel = new Label("Size") { style = { unityTextAlign = TextAnchor.MiddleLeft, minWidth = 60 } };
            var sizeField = new IntegerField { value = arrayProp.arraySize };
            sizeField.style.flexGrow = 1f;
            sizeRow.Add(sizeLabel);
            sizeRow.Add(sizeField);
            body.Add(sizeRow);

            var elementsBox = new VisualElement();
            body.Add(elementsBox);

            void RebuildElements()
            {
                elementsBox.Clear();
                int count = arrayProp.arraySize;
                for (int i = 0; i < count; i++)
                {
                    var elementProp = arrayProp.GetArrayElementAtIndex(i);
                    VisualElement row;
                    if (elementProp.propertyType == SerializedPropertyType.ManagedReference)
                    {
                        row = BuildManagedRefUI(elementProp, attr, $"Element {i}");
                    }
                    else
                    {
                        row = new PropertyField(elementProp, $"Element {i}");
                    }
                    elementsBox.Add(row);
                }
            }

            sizeField.RegisterValueChangedCallback(evt =>
            {
                int newSize = Math.Max(0, evt.newValue);
                var so = arrayProp.serializedObject;
                so.Update();
                if (newSize != arrayProp.arraySize)
                {
                    Undo.RecordObjects(so.targetObjects, "Resize Array");
                    arrayProp.arraySize = newSize;
                    so.ApplyModifiedProperties();
                    RebuildElements();
                }
                else
                {
                    // keep UI in sync
                    RebuildElements();
                }
            });

            RebuildElements();
            return root;
        }

        private VisualElement BuildManagedListUI(SerializedProperty managedListProp, SerializeReferenceTypePickerAttribute attr, Type elementType, string label)
        {
            var root = new VisualElement();
            root.style.flexGrow = 1f;

            var foldout = new Foldout { text = label, value = managedListProp.isExpanded };
            foldout.RegisterValueChangedCallback(evt =>
            {
                managedListProp.serializedObject.Update();
                managedListProp.isExpanded = evt.newValue;
                managedListProp.serializedObject.ApplyModifiedProperties();
            });
            root.Add(foldout);

            var body = new VisualElement();
            body.style.marginLeft = 16;
            foldout.Add(body);

            // Ensure instance exists
            EnsureListInstance(managedListProp, elementType);

            // Find internal Array child
            var arrayChild = FindArrayChild(managedListProp);
            if (arrayChild == null)
            {
                body.Add(new HelpBox("List content not found (Array)", HelpBoxMessageType.Warning));
                return root;
            }

            // Build the array UI for the inner array
            var arrayUI = BuildArrayUI(arrayChild, attr, "Elements");
            body.Add(arrayUI);
            return root;
        }

        private void EnsureListInstance(SerializedProperty managedListProp, Type elementType)
        {
            if (managedListProp.managedReferenceValue != null) return;

            var so = managedListProp.serializedObject;
            so.Update();
            var listType = typeof(List<>).MakeGenericType(elementType);
            object instance = null;
            try { instance = Activator.CreateInstance(listType); }
            catch (Exception e) { Debug.LogError($"Failed to create list instance of {listType}: {e.Message}"); }

            if (instance != null)
            {
                Undo.RecordObjects(so.targetObjects, "Create List Instance");
                managedListProp.managedReferenceValue = instance;
                so.ApplyModifiedProperties();
            }
        }

        private VisualElement BuildManagedRefUI(SerializedProperty prop, SerializeReferenceTypePickerAttribute attr, string label)
        {
            var root = new VisualElement();
            root.style.flexGrow = 1f;

            // Header: foldout + type button on the right
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            root.Add(header);

            var foldout = new Foldout { text = label, value = prop.isExpanded };
            foldout.style.flexGrow = 1f;
            header.Add(foldout);

            var typeButton = new Button();
            typeButton.text = GetCurrentTypeDisplayName(prop);
            typeButton.style.width = ButtonWidth;
            header.Add(typeButton);

            // Body container for child properties
            var body = new VisualElement();
            body.style.marginLeft = 16;
            root.Add(body);
            body.style.display = foldout.value ? DisplayStyle.Flex : DisplayStyle.None;

            void RebuildBody()
            {
                body.Clear();
                foreach (var child in EnumerateChildren(prop))
                {
                    var propertyField = new PropertyField(child);   
                    body.Add(propertyField);
                }
            }

            // Foldout synchronization
            foldout.RegisterValueChangedCallback(evt =>
            {
                prop.serializedObject.Update();
                prop.isExpanded = evt.newValue;
                prop.serializedObject.ApplyModifiedProperties();
                body.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                // When expanding later (e.g., after selecting an array element with foldout initially closed),
                // we need to (re)build the body so the children are drawn.
               
            });

            // Type picker action
            typeButton.clicked += () =>
            {
                Type declaredType = GetDeclaredFieldOrElementType(prop) ?? typeof(object);
                Type baseType = attr?.BaseType ?? declaredType;
                var menu = new GenericMenu();

                // Null option
                menu.AddItem(new GUIContent(NullDisplay), string.IsNullOrEmpty(prop.managedReferenceFullTypename), () =>
                {
                    var so = prop.serializedObject;
                    so.Update();
                    Undo.RecordObjects(so.targetObjects, "Set Managed Reference Null");
                    prop.managedReferenceValue = null;
                    so.ApplyModifiedProperties();
                    typeButton.text = GetCurrentTypeDisplayName(prop);
                    RebuildBody();
                });

                foreach (var t in GetAssignableTypes(baseType, attr?.AllowAbstract == true))
                {
                    var captured = t;
                    menu.AddItem(new GUIContent(captured.Name.Replace('.', '/')), IsCurrentType(prop, captured), () =>
                    {
                        object instance = null;
                        try { instance = Activator.CreateInstance(captured); }
                        catch (Exception e) { Debug.LogError($"Failed to create instance of {captured.FullName}: {e.Message}"); }

                        var so = prop.serializedObject;
                        so.Update();
                        Undo.RecordObjects(so.targetObjects, "Change Managed Reference Type");
                        prop.managedReferenceValue = instance;
                        so.ApplyModifiedProperties();
                        typeButton.text = GetCurrentTypeDisplayName(prop);
                        // Expand when a new instance is created for discoverability
                        foldout.value = true;
                        RebuildBody();
                    });
                }

                menu.ShowAsContext();
            };

            // Initial build
            // if (prop.isExpanded)
            // {
                RebuildBody();
            // }

            return root;
        }

        private static IEnumerable<SerializedProperty> EnumerateChildren(SerializedProperty property)
        {
            var iterator = property.Copy();
            var end = iterator.GetEndProperty();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                yield return iterator.Copy();
                enterChildren = false;
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Determine if this is an array or a single managed reference
            if (property.isArray && property.propertyType != SerializedPropertyType.ManagedReference)
            {
                DrawArray(position, property, label);
                return;
            }

            // Special case: ManagedReference to a List<T> (e.g., [SerializeReference] List<RoomSequencer>)
            if (property.propertyType == SerializedPropertyType.ManagedReference && IsDeclaredListType(property, out var listElementType))
            {
                DrawManagedListReference(position, property, label, listElementType);
                return;
            }

            DrawManagedReferenceWithPicker(position, property, label);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (property.isArray && property.propertyType != SerializedPropertyType.ManagedReference)
            {
                return ComputeArrayHeight(property);
            }

            // ManagedReference List<> special case: header + array body when expanded
            if (property.propertyType == SerializedPropertyType.ManagedReference && IsDeclaredListType(property, out _))
            {
                float header = EditorGUIUtility.singleLineHeight;
                float spacing = EditorGUIUtility.standardVerticalSpacing;
                float total = header;
                if (property.isExpanded)
                {
                    var arrayProp = FindArrayChild(property);
                    if (arrayProp != null)
                    {
                        total += spacing;
                        total += ComputeArrayHeight(arrayProp);
                    }
                }
                return total;
            }

            // Custom height for single managed reference (non-list): header + sum of children when expanded
            float headerLine = EditorGUIUtility.singleLineHeight;
            float spacingLine = EditorGUIUtility.standardVerticalSpacing;
            float totalHeight = headerLine;

            if (property.isExpanded && property.propertyType == SerializedPropertyType.ManagedReference)
            {
                totalHeight += spacingLine;

                var iterator = property.Copy();
                var end = iterator.GetEndProperty();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                {
                    float h = EditorGUI.GetPropertyHeight(iterator, true);
                    totalHeight += h + spacingLine;
                    enterChildren = false;
                }
            }

            return totalHeight;
        }

        private void DrawArray(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            // Draw foldout
            Rect foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            float y = position.y + EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            // Size field with change handling
            Rect sizeRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
            EditorGUI.BeginChangeCheck();
            int newSize = EditorGUI.IntField(sizeRect, "Size", property.arraySize);
            if (EditorGUI.EndChangeCheck())
            {
                newSize = Mathf.Max(0, newSize);
                var so = property.serializedObject;
                so.Update();
                if (newSize != property.arraySize)
                {
                    Undo.RecordObjects(so.targetObjects, "Resize Array");
                    property.arraySize = newSize;
                    so.ApplyModifiedProperties();
                    GUIUtility.ExitGUI();
                }
            }
            y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;

            // Draw each element with a picker button
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                float elementHeight = EditorGUI.GetPropertyHeight(element, new GUIContent($"Element {i}"), true);

                Rect rowRect = new Rect(position.x, y, position.width, elementHeight);
                // If this element is a managed reference, draw with picker; otherwise, fallback
                if (element.propertyType == SerializedPropertyType.ManagedReference)
                {
                    DrawManagedReferenceWithPicker(rowRect, element, new GUIContent($"Element {i}"));
                }
                else
                {
                    EditorGUI.PropertyField(rowRect, element, true);
                }
                y += elementHeight + EditorGUIUtility.standardVerticalSpacing;
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private float ComputeArrayHeight(SerializedProperty property)
        {
            // Calculate height for array including children
            float height = EditorGUIUtility.singleLineHeight; // header line
            if (property.isExpanded)
            {
                height += EditorGUIUtility.standardVerticalSpacing;
                for (int i = 0; i < property.arraySize; i++)
                {
                    SerializedProperty element = property.GetArrayElementAtIndex(i);
                    height += EditorGUI.GetPropertyHeight(element, new GUIContent($"Element {i}"), true) + EditorGUIUtility.standardVerticalSpacing;
                }
                // space for size field
                height += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }
            return height;
        }

        private bool IsDeclaredListType(SerializedProperty property, out Type elementType)
        {
            elementType = null;
            var declared = GetDeclaredFieldOrElementType(property);
            if (declared == null) return false;
            if (declared.IsGenericType && declared.GetGenericTypeDefinition() == typeof(List<>))
            {
                elementType = declared.GetGenericArguments()[0];
                return true;
            }
            return false;
        }

        private SerializedProperty FindArrayChild(SerializedProperty property)
        {
            // Unity exposes list/array content via a child property named "Array"
            var arrayProp = property.FindPropertyRelative("Array");
            if (arrayProp != null && arrayProp.isArray) return arrayProp;

            // Fallback: iterate to find an array child
            var it = property.Copy();
            var end = it.GetEndProperty();
            bool enter = true;
            while (it.NextVisible(enter) && !SerializedProperty.EqualContents(it, end))
            {
                if (it.isArray) return it.Copy();
                enter = false;
            }
            return null;
        }

        private void DrawManagedListReference(Rect position, SerializedProperty property, GUIContent label, Type elementType)
        {
            float headerHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // Header (foldout only) for the list
            Rect headerRect = new Rect(position.x, position.y, position.width, headerHeight);
            property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, label, true);

            if (!property.isExpanded) return;

            // Ensure the List<> instance exists before accessing the Array
            if (property.managedReferenceValue == null)
            {
                // Record Undo on all targets
                var targets = property.serializedObject.targetObjects;
                Undo.RecordObjects(targets, "Create List");

                var listType = typeof(List<>).MakeGenericType(elementType);
                object instance = null;
                try { instance = Activator.CreateInstance(listType); }
                catch (Exception e) { Debug.LogError($"Failed to create list instance of {listType}: {e.Message}"); }

                property.serializedObject.Update();
                property.managedReferenceValue = instance;
                property.serializedObject.ApplyModifiedProperties();
            }

            // Draw the internal Array content using our array routine
            var arrayProp = FindArrayChild(property);
            if (arrayProp != null)
            {
                Rect bodyRect = new Rect(position.x, position.y + headerHeight + spacing, position.width, position.height - headerHeight - spacing);
                DrawArray(bodyRect, arrayProp, new GUIContent("Elements"));
            }
            else
            {
                Rect warnRect = new Rect(position.x, position.y + headerHeight + spacing, position.width, EditorGUIUtility.singleLineHeight);
                EditorGUI.HelpBox(warnRect, "List content not found (Array)", MessageType.Warning);
            }
        }

        private void DrawManagedReferenceWithPicker(Rect position, SerializedProperty property, GUIContent label)
        {
            // Header (foldout + label) on the left, type button on the right
            float headerHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect headerRect = new Rect(position.x, position.y, position.width - ButtonWidth - 4f, headerHeight);
            Rect buttonRect = new Rect(position.xMax - ButtonWidth, position.y, ButtonWidth, headerHeight);

            // Draw foldout with label in one go (prevents label overlap)
            property.isExpanded = EditorGUI.Foldout(headerRect, property.isExpanded, label, true);

            // Draw type picker button
            if (GUI.Button(buttonRect, GetCurrentTypeDisplayName(property)))
            {
                Type declaredType = GetDeclaredFieldOrElementType(property) ?? typeof(object);
                var attribute = (SerializeReferenceTypePickerAttribute)this.attribute;
                Type baseType = attribute.BaseType ?? declaredType;
                ShowTypeMenu(property, baseType, allowAbstract: attribute.AllowAbstract);
            }

            // Draw children only when expanded
            if (property.isExpanded)
            {
                float y = position.y + headerHeight + spacing;

                var iterator = property.Copy();
                var end = iterator.GetEndProperty();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                {
                    float h = EditorGUI.GetPropertyHeight(iterator, true);
                    var row = new Rect(position.x, y, position.width, h);
                    EditorGUI.PropertyField(row, iterator, true);
                    y += h + spacing;
                    enterChildren = false; // only enter children once, then siblings
                }
            }
        }


        private static string GetCurrentTypeDisplayName(SerializedProperty prop)
        {
            if (prop.propertyType != SerializedPropertyType.ManagedReference)
                return "Select Type";

            string fullName = prop.managedReferenceFullTypename;
            if (string.IsNullOrEmpty(fullName)) return NullDisplay;

            // fullName is in format: AssemblyName TypeFullName
            int spaceIndex = fullName.IndexOf(' ');
            string typeName = spaceIndex >= 0 ? fullName.Substring(spaceIndex + 1) : fullName;
            int lastDot = typeName.LastIndexOf('.') + 1;
            if (lastDot > 0 && lastDot < typeName.Length)
            {
                return typeName.Substring(lastDot);
            }
            return typeName;
        }

        private void ShowTypeMenu(SerializedProperty property, Type baseType, bool allowAbstract)
        {
            var menu = new GenericMenu();

            // Null option
            menu.AddItem(new GUIContent(NullDisplay), string.IsNullOrEmpty(property.managedReferenceFullTypename), () =>
            {
                property.serializedObject.Update();
                property.managedReferenceValue = null;
                property.serializedObject.ApplyModifiedProperties();
            });

            foreach (var t in GetAssignableTypes(baseType, allowAbstract))
            {
                string displayName = t.Name;
                menu.AddItem(new GUIContent(displayName.Replace('.', '/')), IsCurrentType(property, t), () =>
                {
                    object instance = null;
                    try
                    {
                        instance = Activator.CreateInstance(t);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to create instance of {t.FullName}: {e.Message}");
                    }

                    property.serializedObject.Update();
                    property.managedReferenceValue = instance;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }

        private static bool IsCurrentType(SerializedProperty property, Type t)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference) return false;
            var current = property.managedReferenceValue;
            return current != null && current.GetType() == t;
        }

        private static IEnumerable<Type> GetAssignableTypes(Type baseType, bool allowAbstract)
        {
            if (baseType == null) baseType = typeof(object);

            if (!s_cachedTypes.TryGetValue(baseType, out var list))
            {
                list = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Where(a => !a.IsDynamic)
                    .SelectMany(a => SafeGetTypes(a))
                    .Where(t => baseType.IsAssignableFrom(t))
                    .Where(t => allowAbstract || (!t.IsAbstract && !t.IsInterface))
                    .OrderBy(t => t.FullName)
                    .ToArray();

                s_cachedTypes[baseType] = list;
            }

            return list;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        private Type GetDeclaredFieldOrElementType(SerializedProperty property)
        {
            // If this is a managed reference element, we can try to get the declared type from field info or path
            if (fieldInfo != null)
            {
                var ft = fieldInfo.FieldType;
                // Handle arrays and lists
                if (ft.IsArray) return ft.GetElementType();
                if (IsList(ft)) return ft.GetGenericArguments().FirstOrDefault();
                return ft;
            }

            // Fallback: try parse from managedReferenceFieldTypename
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                var fieldType = GetTypeFromManagedRefFieldTypename(property.managedReferenceFieldTypename);
                return fieldType;
            }

            return null;
        }

        private static bool IsList(Type ft)
        {
            return ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>);
        }

        private static Type GetTypeFromManagedRefFieldTypename(string managedRefFieldTypename)
        {
            // Format: AssemblyName TypeFullName
            if (string.IsNullOrEmpty(managedRefFieldTypename)) return null;
            int space = managedRefFieldTypename.IndexOf(' ');
            if (space < 0) return null;
            string asmName = managedRefFieldTypename.Substring(0, space);
            string typeName = managedRefFieldTypename.Substring(space + 1);
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == asmName);
            return asm?.GetType(typeName);
        }
    }
}
#endif
