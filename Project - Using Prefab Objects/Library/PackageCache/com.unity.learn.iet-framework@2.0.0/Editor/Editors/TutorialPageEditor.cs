using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.Tutorials.Core.Editor
{
    using static Localization;

    [CustomEditor(typeof(TutorialPage))]
    class TutorialPageEditor : UnityEditor.Editor
    {
        static readonly bool k_IsAuthoringMode = ProjectMode.IsAuthoringMode();

        // NOTE: the order here will be used for the UI
        static readonly string[] k_EventPropertyPaths =
        {
            nameof(TutorialPage.Showing),
            nameof(TutorialPage.Shown),
            nameof(TutorialPage.Staying),
            nameof(TutorialPage.m_OnBeforeTutorialQuit), // This deprecated event cannot be migrated automatically so display it for the user
            nameof(TutorialPage.CriteriaValidated),
            // MaskingSettingsChanged & NonMaskingSettingsChanged exist but are hidden in the simplified view
        };

        static readonly string[] k_PropertiesToHide = new[]
            {
                "m_Script",
                nameof(TutorialPage.m_Paragraphs),
                k_AutoAdvancePropertyPath,
                nameof(TutorialPage.MaskingSettingsChanged),
                nameof(TutorialPage.NonMaskingSettingsChanged),
            }
            .Concat(k_EventPropertyPaths)
            .ToArray();

        const string k_ParagraphPropertyPath = nameof(TutorialPage.m_Paragraphs) + ".m_Items";
        const string k_ParagraphMaskingSettingsRelativeProperty = "m_MaskingSettings";
        const string k_ParagraphVideoRelativeProperty = "m_Video";
        const string k_ParagraphImageRelativeProperty = "m_Image";
        const string k_ParagraphTypeProperty = "m_Type";

        const string k_ParagraphNarrativeTitleProperty = "Title.m_Untranslated";
        const string k_ParagraphNarrativeDescriptionProperty = "Text.m_Untranslated";

        const string k_ParagraphIntructionTitleProperty = "Title.m_Untranslated";
        const string k_ParagraphInstructionDescriptionProperty = "Text.m_Untranslated";

        const string k_ParagraphCriteriaTypePropertyPath = "m_CriteriaCompletion";
        const string k_ParagraphCriteriaPropertyPath = "m_Criteria";
        const string k_AutoAdvancePropertyPath = nameof(TutorialPage.m_AutoAdvance);

        // NOTE TutorialSwitch doesn't have title yet, body used for the button text.
        const string k_ParagraphNextTutorialButtonTextPropertyPath = "Text.m_Untranslated";
        const string k_ParagraphNextTutorialPropertyPath = "m_Tutorial";

        static readonly Regex s_MatchMaskingSettingsPropertyPath =
            new Regex(
                string.Format(
                    "(^{0}\\.Array\\.size)|(^({0}\\.Array\\.data\\[\\d+\\]\\.{1}\\.))",
                    k_ParagraphPropertyPath, k_ParagraphMaskingSettingsRelativeProperty
                )
            );

        static GUIContent s_EventsSectionTitle;

        static bool ShowEvents
        {
            get => SessionState.GetBool("TutorialPageEditor.ShowEvents", false);
            set => SessionState.SetBool("TutorialPageEditor.ShowEvents", value);
        }
        // Non-null/empty if we have created a callback script and waiting for a scriptable object instance to be created for it.
        static string CallbackAssetPath
        {
            get { return SessionState.GetString("iet_creating_SO", string.Empty); }
            set { SessionState.SetString("iet_creating_SO", value); }
        }

        TutorialPage tutorialPage { get { return (TutorialPage)target; } }

        [NonSerialized]
        string m_WarningMessage;

        class EventPropertyData
        {
            public SerializedProperty Property;
            public GUIContent Content;
        }

        List<EventPropertyData> m_Events = new List<EventPropertyData>();

        SerializedProperty m_MaskingSettings;
        SerializedProperty m_Type;
        SerializedProperty m_Video;
        SerializedProperty m_Image;

        SerializedProperty m_NarrativeTitle;
        SerializedProperty m_NarrativeDescription;
        SerializedProperty m_InstructionTitle;
        SerializedProperty m_InstructionDescription;

        SerializedProperty m_CriteriaCompletion;
        SerializedProperty m_Criteria;
        SerializedProperty m_AutoAdvance;

        SerializedProperty m_TutorialButtonText;
        SerializedProperty m_NextTutorial;

        HeaderMediaType m_HeaderMediaType;

        enum HeaderMediaType
        {
            Image = ParagraphType.Image,
            Video = ParagraphType.Video
        }

        Texture s_HelpIcon;

        protected virtual void OnEnable()
        {
            s_HelpIcon = EditorGUIUtility.IconContent("console.infoicon.sml").image;
            InitializeSerializedProperties();

            Undo.postprocessModifications += OnPostprocessModifications;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
        }

        protected virtual void OnDisable()
        {
            Undo.postprocessModifications -= OnPostprocessModifications;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
        }

        void OnUndoRedoPerformed()
        {
            if (tutorialPage == null) { return; }
            tutorialPage.RaiseMaskingSettingsChanged();
        }

        UndoPropertyModification[] OnPostprocessModifications(UndoPropertyModification[] modifications)
        {
            if (tutorialPage == null) { return modifications; }

            bool targetModified = false;
            bool maskingChanged = false;

            foreach (var modification in modifications)
            {
                if (modification.currentValue.target != target) { continue; }

                targetModified = true;
                var propertyPath = modification.currentValue.propertyPath;
                if (s_MatchMaskingSettingsPropertyPath.IsMatch(propertyPath))
                {
                    maskingChanged = true;
                    break;
                }
            }

            if (maskingChanged)
            {
                tutorialPage.RaiseMaskingSettingsChanged();
            }
            else if (targetModified)
            {
                tutorialPage.RaiseNonMaskingSettingsChanged();
            }
            return modifications;
        }

        void InitializeSerializedProperties()
        {
            // TODO: confirm that localization works for these
            string tooltip = Tr(
                "You can only assign public, non-static methods here. It is recommended that you define a ScriptableObject class " +
                "that exposes all the methods you'd like to call, create an instance of that and assign it to these events in order to access the callbacks."
            );
            s_EventsSectionTitle = new GUIContent(Tr("Custom Callbacks"), s_HelpIcon, tooltip);

            k_EventPropertyPaths.ToList().ForEach(prop => CreateEventProperty(prop));

            SerializedProperty paragraphs = serializedObject.FindProperty(k_ParagraphPropertyPath);
            if (paragraphs == null)
            {
                m_WarningMessage = string.Format(
                    Tr("Unable to locate property path {0} on this object. Automatic masking updates will not work."),
                    k_ParagraphPropertyPath
                );
            }
            else if (paragraphs.arraySize > 0)
            {
                SerializedProperty firstParagraph = paragraphs.GetArrayElementAtIndex(0);

                m_MaskingSettings = firstParagraph.FindPropertyRelative(k_ParagraphMaskingSettingsRelativeProperty);
                if (m_MaskingSettings == null)
                    m_WarningMessage = string.Format(
                        Tr("Unable to locate property path {0}.Array.data[0].{1} on this object. Automatic masking updates will not work."),
                        k_ParagraphPropertyPath,
                        k_ParagraphMaskingSettingsRelativeProperty
                    );

                m_Type = firstParagraph.FindPropertyRelative(k_ParagraphTypeProperty);
                m_HeaderMediaType = (HeaderMediaType)m_Type.intValue;
                var headerMediaParagraphType = (ParagraphType)m_Type.intValue;
                // Only Image and Video are allowed for the first paragraph which is always the header media in the new fixed tutorial page layout.
                if (headerMediaParagraphType != ParagraphType.Image && headerMediaParagraphType != ParagraphType.Video)
                {
                    m_Type.intValue = (int)ParagraphType.Image;
                }

                m_Video = firstParagraph.FindPropertyRelative(k_ParagraphVideoRelativeProperty);
                m_Image = firstParagraph.FindPropertyRelative(k_ParagraphImageRelativeProperty);

                switch (paragraphs.arraySize)
                {
                    case 2: SetupNarrativeOnlyPage(paragraphs); break;
                    case 4: SetupSwitchTutorialPage(paragraphs); break;
                    case 3:
                    default:
                        SetupNarrativeAndInstructivePage(paragraphs); break;
                }
            }
        }

        void CreateEventProperty(string propertyPath)
        {
            // TODO: confirm that localization works for these
            var property = serializedObject.FindProperty(propertyPath);
            Debug.Assert(property != null, $"Property path {propertyPath} not valid.");
            if (property == null)
                return;

            string tooltip = GetSerializedPropertyTooltip<TutorialPage>(property);
            var eventData = new EventPropertyData
            {
                Property = property,
                Content = new GUIContent(Tr(property.displayName), Tr(tooltip))
            };
            m_Events.Add(eventData);
        }

        static string GetSerializedPropertyTooltip<Type>(SerializedProperty serializedProperty)
        {
            const BindingFlags bindedTypes = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var field = typeof(Type).GetField(serializedProperty.name, bindedTypes);
            var attributes = field.GetCustomAttributes(typeof(TooltipAttribute), inherit: true) as TooltipAttribute[];
            return attributes.Length > 0 ? attributes[0].tooltip : string.Empty;
        }

        void SetupNarrativeParagraph(SerializedProperty paragraphs)
        {
            if (paragraphs.arraySize < 2)
            {
                m_NarrativeTitle = null;
                m_NarrativeDescription = null;
                return;
            }

            SerializedProperty narrativeParagraph = paragraphs.GetArrayElementAtIndex(1);
            m_NarrativeTitle = narrativeParagraph.FindPropertyRelative(k_ParagraphNarrativeTitleProperty);
            m_NarrativeDescription = narrativeParagraph.FindPropertyRelative(k_ParagraphNarrativeDescriptionProperty);
            // TODO refactoring, support the old name of the property for a while still. Drop this in 2.0.
            if (m_NarrativeDescription == null)
                m_NarrativeDescription = narrativeParagraph.FindPropertyRelative("m_description1");
        }

        void SetupNarrativeOnlyPage(SerializedProperty paragraphs)
        {
            SetupNarrativeParagraph(paragraphs);
        }

        void SetupNarrativeAndInstructivePage(SerializedProperty paragraphs)
        {
            SetupNarrativeParagraph(paragraphs);
            if (paragraphs.arraySize > 2)
            {
                SerializedProperty instructionParagraph = paragraphs.GetArrayElementAtIndex(2);
                m_InstructionTitle = instructionParagraph.FindPropertyRelative(k_ParagraphIntructionTitleProperty);
                m_InstructionDescription = instructionParagraph.FindPropertyRelative(k_ParagraphInstructionDescriptionProperty);
                m_CriteriaCompletion = instructionParagraph.FindPropertyRelative(k_ParagraphCriteriaTypePropertyPath);
                m_Criteria = instructionParagraph.FindPropertyRelative(k_ParagraphCriteriaPropertyPath);
                m_AutoAdvance = serializedObject.FindProperty(k_AutoAdvancePropertyPath);
                return;
            }
            m_InstructionTitle = null;
            m_InstructionDescription = null;
            m_CriteriaCompletion = null;
            m_Criteria = null;
            m_AutoAdvance = null;
        }

        void SetupSwitchTutorialPage(SerializedProperty paragraphs)
        {
            SetupNarrativeAndInstructivePage(paragraphs);
            if (paragraphs.arraySize > 3)
            {
                SerializedProperty tutorialSwitchParagraph = paragraphs.GetArrayElementAtIndex(3);
                m_NextTutorial = tutorialSwitchParagraph.FindPropertyRelative(k_ParagraphNextTutorialPropertyPath);
                m_TutorialButtonText = tutorialSwitchParagraph.FindPropertyRelative(k_ParagraphNextTutorialButtonTextPropertyPath);
            }
            else
            {
                m_NextTutorial = null;
                m_TutorialButtonText = null;
            }
        }

        public override void OnInspectorGUI()
        {
            if (!string.IsNullOrEmpty(m_WarningMessage))
            {
                EditorGUILayout.HelpBox(m_WarningMessage, MessageType.Warning);
            }

            if (SerializedTypeDrawer.UseDefaultEditors)
            {
                base.OnInspectorGUI();
            }
            else
            {
                DrawSimplifiedInspector();
            }
        }

        void DrawSimplifiedInspector()
        {
            EditorGUILayout.BeginVertical();

            if (m_Type != null)
            {
                EditorGUILayout.LabelField(Tr("Header Media Type"));
                m_HeaderMediaType = (HeaderMediaType)EditorGUILayout.EnumPopup(GUIContent.none, m_HeaderMediaType);
                m_Type.intValue = (int)m_HeaderMediaType;

                EditorGUILayout.Space(10);
            }

            RenderProperty(Tr("Media"), m_HeaderMediaType == HeaderMediaType.Image ? m_Image : m_Video);

            EditorGUILayout.Space(10);

            RenderProperty(Tr("Narrative Title"), m_NarrativeTitle);

            EditorGUILayout.Space(10);

            RenderTextAreaProperty(Tr("Narrative Description"), m_NarrativeDescription);

            EditorGUILayout.Space(10);

            RenderProperty(Tr("Instruction Title"), m_InstructionTitle);

            EditorGUILayout.Space(10);

            RenderTextAreaProperty(Tr("Instruction Description"), m_InstructionDescription);

            if (m_CriteriaCompletion != null)
            {
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField(Tr("Completion Criteria"), EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(m_AutoAdvance);
                EditorGUILayout.PropertyField(m_CriteriaCompletion, new GUIContent(Tr("Completion Type")));
                EditorGUILayout.PropertyField(m_Criteria, new GUIContent(Tr("Criteria")));
            }

            if (m_NextTutorial != null)
            {
                EditorGUILayout.Space(10);
                RenderProperty(Tr("Next Tutorial"), m_NextTutorial);
                RenderProperty(Tr("Next Tutorial button text"), m_TutorialButtonText);
            }

            EditorStyles.label.wordWrap = true;

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();

            ShowEvents = EditorGUILayout.Foldout(ShowEvents, s_EventsSectionTitle);
            if (k_IsAuthoringMode && GUILayout.Button(Tr("Create Callback Handler")))
            {
                CreateCallbackHandlerScript("TutorialCallbacks.cs");
                m_Events.ForEach(data => InitializeEventWithDefaultData(data.Property));
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            if (ShowEvents)
            {
                if (m_Events.Any(e => TutorialEditorUtils.EventIsNotInState(e.Property, UnityEventCallState.EditorAndRuntime)))
                {
                    TutorialEditorUtils.RenderEventStateWarning();
                }

                // TODO unwanted "Callbacks" header is shown here
                m_Events.ForEach(data => RenderEventProperty(data.Content, data.Property));
            }

            RenderProperty(Tr("Enable Masking"), m_MaskingSettings);

            EditorGUILayout.EndVertical();

            DrawPropertiesExcluding(serializedObject, k_PropertiesToHide);

            serializedObject.ApplyModifiedProperties();
        }

        static void RenderProperty(string name, SerializedProperty property)
        {
            if (property == null) { return; }
            EditorGUILayout.LabelField(name);
            EditorGUILayout.PropertyField(property, GUIContent.none);
        }

        static void RenderTextAreaProperty(string name, SerializedProperty property)
        {
            if (property == null)
                return;
            EditorGUILayout.LabelField(name);
            property.stringValue = EditorGUILayout.TextArea(property.stringValue);
        }

        /// <summary>
        /// Renders an event property in the inspector
        /// </summary>
        /// <param name="headerContent">Content shown in the header area.</param>
        /// <param name="property">The property to render</param>
        static void RenderEventProperty(GUIContent headerContent, SerializedProperty property)
        {
            EditorGUILayout.PropertyField(property, headerContent);
        }

        void InitializeEventWithDefaultData(SerializedProperty eventProperty)
        {
            var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/IET/TutorialCallbacks.asset"); // TODO check this
            //[TODO] Add listeners here if they are empty (?)
            ForceCallbacksListenerTarget(eventProperty, so);
            ForceCallbacksListenerState(eventProperty, UnityEventCallState.EditorAndRuntime);
        }

        /// <summary>
        /// Forces all callbacks of a UnityEvent (or derived class) to use a specific state
        /// </summary>
        /// <param name="eventProperty">A UnityEvent (or derived class) property</param>
        /// <param name="state"></param>
        void ForceCallbacksListenerState(SerializedProperty eventProperty, UnityEventCallState state)
        {
            SerializedProperty persistentCalls = eventProperty.FindPropertyRelative("m_PersistentCalls.m_Calls");
            for (int i = 0; i < persistentCalls.arraySize; i++)
            {
                persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_CallState").intValue = (int)state;
                serializedObject.ApplyModifiedProperties();
            }
        }

        void ForceCallbacksListenerTarget(SerializedProperty eventProperty, UnityEngine.Object target)
        {
            SerializedProperty persistentCalls = eventProperty.FindPropertyRelative("m_PersistentCalls.m_Calls");
            for (int i = 0; i < persistentCalls.arraySize; i++)
            {
                persistentCalls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target").objectReferenceValue = target;
                serializedObject.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// Creates an example callback handler script from a template script.
        /// </summary>
        /// <param name="templateFile">
        /// Template file name, must exist in "Packages/com.unity.learn.iet-framework.authoring/.TemplateAssets" folder.
        /// </param>
        /// <param name="targetDir">Use null to open a dialog for choosing the destination.</param>
        internal static void CreateCallbackHandlerScript(string templateFile, string targetDir = null)
        {
            // TODO preferably these template assets should reside in the authoring package
            var templatePath = $"Packages/com.unity.learn.iet-framework/.TemplateAssets/{templateFile}";
            if (!File.Exists(templatePath))
            {
                Debug.LogError($"Template file '{templateFile}' does not exist.");
                return;
            }

            if (targetDir == null)
            {
                targetDir =  EditorUtility.OpenFolderPanel(
                    Tr("Choose Folder for the Callback Handler Files"),
                    Application.dataPath,
                    string.Empty
                );
                if (targetDir.IsNullOrEmpty())
                    return; // user cancelled
            }

            try
            {
                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                var destFileName = Path.Combine(targetDir, templateFile);
                CallbackAssetPath = Path.ChangeExtension(destFileName, ".asset")
                    .Replace(@"\", "/")
                    .Replace(Application.dataPath, "Assets");

                // TODO preferably use the following which would allow renaming the file immediately to user's liking
                // and utilising template script features.
                //ProjectWindowUtil.CreateScriptAssetFromTemplateFile(templatePath, templateFile);
                File.Copy(templatePath, destFileName);
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                CallbackAssetPath = string.Empty;
            }
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        static void OnScriptsReloaded()
        {
            if (CallbackAssetPath.IsNullOrEmpty())
                return;

            var destFileName = CallbackAssetPath;
            CallbackAssetPath = string.Empty;

            const string errorMsg1 = "Could not create TutorialCallbacks instance automatically";
            const string errorMsg2 = "Create the instance using Assets > Create > Tutorials > TutorialCallbacks Instance";

            // TODO If the user creates the asset/script to a folder with asmdef this doesn't work.
            const string className = "TutorialCallbacks";
            var type = Assembly.Load("Assembly-CSharp").GetType(className);
            if (type == null)
            {
                Debug.LogError($"{errorMsg1}: {className} class not found from Assembly-CSharp. {errorMsg2}.");
                return;
            }

            const string methodName = "CreateAndShowAsset";
            var method = type.GetMethod(methodName);
            if (method == null)
            {
                Debug.LogError($"{errorMsg1}: {methodName} not found from {className} class. {errorMsg2}.");
                return;
            }

            method.Invoke(null, new [] { destFileName });
        }
    }
}
