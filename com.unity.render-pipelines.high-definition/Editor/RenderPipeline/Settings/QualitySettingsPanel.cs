using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace UnityEditor.Experimental.Rendering.HDPipeline
{
    public class QualitySettingsPanel
    {
        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider("HDRP/Quality", SettingsScope.Project)
            {
                activateHandler = (searchContext, rootElement) =>
                {
                    HDEditorUtils.AddStyleSheets(rootElement);

                    var panel = new QualitySettingsPanelVisualElement(searchContext);
                    panel.style.flexGrow = 1;

                    rootElement.Add(panel);
                },
                keywords = new [] { "quality", "hdrp" }
            };
        }

        class HDRPAssetHeaderEntry : VisualElement
        {
            TextElement m_Element;

            public HDRPAssetHeaderEntry()
            {
                m_Element = new Label();

                m_Element.style.paddingLeft = 5;
                m_Element.style.flexGrow = 1;
                m_Element.style.flexDirection = FlexDirection.Row;
                m_Element.style.unityTextAlign = TextAnchor.MiddleLeft;

                Add(m_Element);
            }

            public void Bind(int index, HDRenderPipelineAsset asset)
            {
                m_Element.text = asset.name;

                RemoveFromClassList("even");
                RemoveFromClassList("odd");
                AddToClassList((index & 1) == 1 ? "odd" : "even");
            }
        }

        class QualitySettingsPanelVisualElement : VisualElement
        {
            HDRenderPipelineAsset[] m_HDRPAssets;
            Label m_InspectorTitle;
            ListView m_HDRPAssetList;
            Editor m_Cached;

            public QualitySettingsPanelVisualElement(string searchContext)
            {
                m_HDRPAssets = GraphicsSettings.allConfiguredRenderPipelines
                    .OfType<HDRenderPipelineAsset>()
                    .Distinct()
                    .ToArray();

                // title
                var title = new Label()
                {
                    text = "Quality"
                };
                title.AddToClassList("h1");

                // Header
                var headerBox = new VisualElement();
                headerBox.style.height = 200;

                m_HDRPAssetList = new ListView()
                {
                    bindItem = (el, i) => ((HDRPAssetHeaderEntry)el).Bind(i, m_HDRPAssets[i]),
                    itemHeight = (int)EditorGUIUtility.singleLineHeight,
                    selectionType = SelectionType.Single,
                    itemsSource = m_HDRPAssets,
                    makeItem = () => new HDRPAssetHeaderEntry(),
                };
                m_HDRPAssetList.AddToClassList("unity-quality-header-list");
                m_HDRPAssetList.onSelectionChanged += OnSelectionChanged;

                headerBox.Add(m_HDRPAssetList);

                m_InspectorTitle = new Label();
                m_InspectorTitle.text = "No asset selected";
                m_InspectorTitle.AddToClassList("h1");

                // Inspector
                var inspector = new IMGUIContainer(DrawInspector);
                inspector.style.flexGrow = 1;
                inspector.style.flexDirection = FlexDirection.Row;

                var inspectorBox = new ScrollView();
                inspectorBox.style.flexGrow = 1;
                inspectorBox.style.flexDirection = FlexDirection.Row;
                inspectorBox.contentContainer.Add(inspector);

                Add(title);
                Add(headerBox);
                Add(m_InspectorTitle);
                Add(inspectorBox);
            }

            void OnSelectionChanged(List<object> obj)
            {
                m_InspectorTitle.text = m_HDRPAssets[m_HDRPAssetList.selectedIndex].name;
            }

            void DrawInspector()
            {
                var selected = m_HDRPAssetList.selectedIndex;
                if (selected >= 0)
                {
                    Editor.CreateCachedEditor(m_HDRPAssets[selected], typeof(HDRenderPipelineEditor), ref m_Cached);
                    m_Cached.OnInspectorGUI();
                }
            }
        }
    }
}
