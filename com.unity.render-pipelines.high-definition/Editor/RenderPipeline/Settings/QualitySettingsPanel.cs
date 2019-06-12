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

            public void Bind(int index, HDRPAssetLocations asset)
            {
                var names = asset.indices.Select(i => QualitySettings.names[i]);
                if (asset.isDefault)
                    names = Enumerable.Repeat("default", 1).Union(names);

                m_Element.text = $"{asset.asset.name} in {string.Join(",", names.ToArray())}";

                RemoveFromClassList("even");
                RemoveFromClassList("odd");
                AddToClassList((index & 1) == 1 ? "odd" : "even");
            }
        }

        struct HDRPAssetLocations
        {
            public readonly bool isDefault;
            public readonly List<int> indices;
            public readonly HDRenderPipelineAsset asset;

            public HDRPAssetLocations(bool isDefault, HDRenderPipelineAsset asset)
            {
                this.asset = asset;
                this.isDefault = isDefault;
                this.indices = new List<int>();
            }
        }

        class QualitySettingsPanelVisualElement : VisualElement
        {
            List<HDRPAssetLocations> m_HDRPAssets;
            Label m_InspectorTitle;
            ListView m_HDRPAssetList;
            Editor m_Cached;

            public QualitySettingsPanelVisualElement(string searchContext)
            {
                m_HDRPAssets = new List<HDRPAssetLocations>();
                if (GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset hdrp)
                    m_HDRPAssets.Add(new HDRPAssetLocations(true, hdrp));

                var qualityLevelCount = QualitySettings.names.Length;
                for (var i = 0; i < qualityLevelCount; ++i)
                {
                    if (!(QualitySettings.GetRenderPipelineAssetAt(i) is HDRenderPipelineAsset hdrp2))
                        continue;

                    var index = m_HDRPAssets.FindIndex(a => a.asset == hdrp2);
                    if (index >= 0)
                        m_HDRPAssets[index].indices.Add(i);
                    else
                        m_HDRPAssets.Add(new HDRPAssetLocations(false, hdrp2));
                }

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
                m_InspectorTitle.text = m_HDRPAssets[m_HDRPAssetList.selectedIndex].asset.name;
            }

            void DrawInspector()
            {
                var selected = m_HDRPAssetList.selectedIndex;
                if (selected >= 0)
                {
                    Editor.CreateCachedEditor(m_HDRPAssets[selected].asset, typeof(HDRenderPipelineEditor), ref m_Cached);
                    m_Cached.OnInspectorGUI();
                }
            }
        }
    }
}
