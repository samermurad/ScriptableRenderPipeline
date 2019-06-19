using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    public class DefaultSettingsPanelProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("Project/HDRP Default Settings", SettingsScope.Project)
            {
                activateHandler = (searchContext, rootElement) =>
                {
                    HDEditorUtils.AddStyleSheets(rootElement);

                    var panel = new DefaultSettingsPanel(searchContext);
                    panel.style.flexGrow = 1;

                    rootElement.Add(panel);
                },
                keywords = new [] { "default", "hdrp" }
            };
        }

        class DefaultSettingsPanel : VisualElement
        {
            VolumeComponentListEditor m_ComponentList;
            Editor m_Cached;

            public DefaultSettingsPanel(string searchContext)
            {
                {
                    var title = new Label
                    {
                        text = "Volume Components"
                    };
                    title.AddToClassList("h1");
                    Add(title);
                }
                {
                    var inspectorContainer = new IMGUIContainer(Draw_VolumeInspector);
                    inspectorContainer.style.flexGrow = 1;
                    inspectorContainer.style.flexDirection = FlexDirection.Row;
                    Add(inspectorContainer);
                }
            }

            private static GUIContent k_DefaultVolumeProfileLabel = new GUIContent("Default Volume Profile Asset");
            private static GUIContent k_DefaultHDRPAsset = new GUIContent("Asset with the default settings");
            void Draw_VolumeInspector()
            {
                var hdrpAsset = DefaultSettings.hdrpAssetWithDefaultSettings;
                if (hdrpAsset == null)
                {
                    EditorGUILayout.HelpBox("Base SRP Asset is not an HDRenderPipelineAsset.", MessageType.Warning);
                    return;
                }

                GUI.enabled = false;
                EditorGUILayout.ObjectField(k_DefaultHDRPAsset, hdrpAsset, typeof(HDRenderPipelineAsset), false);
                GUI.enabled = true;

                var asset = EditorDefaultSettings.GetOrAssignDefaultVolumeProfile();

                var newAsset = (VolumeProfile)EditorGUILayout.ObjectField(k_DefaultVolumeProfileLabel, asset, typeof(VolumeProfile), false);
                if (newAsset != null && newAsset != asset)
                {
                    asset = newAsset;
                    hdrpAsset.defaultVolumeProfile = asset;
                }

                Editor.CreateCachedEditor(asset,
                    Type.GetType("UnityEditor.Rendering.VolumeProfileEditor"), ref m_Cached);
                m_Cached.OnInspectorGUI();
            }
        }
    }
}
