using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    using RTHandle = RTHandleSystem.RTHandle;

    public partial class HDShadowManager
    {
        public void RenderShadows(RenderGraph renderGraph, HDCamera hdCamera, CullingResults cullResults)
        {
            // Avoid to do any commands if there is no shadow to draw
            if (m_ShadowRequestCount == 0)
                return;

            m_Atlas.RenderShadows(renderGraph, cullResults, hdCamera.frameSettings, "Punctual Lights Shadows rendering");
            m_CascadeAtlas.RenderShadows(renderGraph, cullResults, hdCamera.frameSettings, "Directional Light Shadows rendering");
            m_AreaLightShadowAtlas.RenderShadows(renderGraph, cullResults, hdCamera.frameSettings, "Area Light Shadows rendering");
        }
    }



    public partial class HDShadowAtlas
    {
        class RenderShadowsPassData
        {
            public RenderGraphMutableResource atlasTexture;
            public RenderGraphMutableResource momentAtlasTexture1;
            public RenderGraphMutableResource momentAtlasTexture2;
            public RenderGraphMutableResource intermediateSummedAreaTexture;
            public RenderGraphMutableResource summedAreaTexture;

            public RenderShadowsParameters parameters;
            public ShadowDrawingSettings shadowDrawSettings;
        }

        public struct RenderShadowResult
        {
            public RenderGraphResource atlasTexture;
            public RenderGraphResource momentAtlasTexture;
        }

        RenderGraphMutableResource AllocateMomentAtlas(RenderGraphBuilder builder, string name, int shaderID = 0)
        {
            return builder.CreateTexture(new TextureDesc(width / 2, height / 2)
                    { colorFormat = GraphicsFormat.R32G32_SFloat, useMipMap = true, autoGenerateMips = false, name = name, enableRandomWrite = true }, shaderID);
        }

        public RenderShadowResult RenderShadows(RenderGraph renderGraph, CullingResults cullResults, FrameSettings frameSettings, string shadowPassName)
        {
            RenderShadowResult result = new RenderShadowResult();

            if (m_ShadowRequests.Count == 0)
                return result;

            using (var builder = renderGraph.AddRenderPass<RenderShadowsPassData>(shadowPassName, out var passData, CustomSamplerId.RenderShadowMaps.GetSampler()))
            {
                passData.parameters = PrepareRenderShadowsParameters();
                // TODO: Get rid of this and refactor to use the same kind of API than RendererList
                passData.shadowDrawSettings = new ShadowDrawingSettings(cullResults, 0);
                passData.shadowDrawSettings.useRenderingLayerMaskTest = frameSettings.IsEnabled(FrameSettingsField.LightLayers);
                passData.atlasTexture = builder.WriteTexture(
                        builder.CreateTexture(  new TextureDesc(width, height)
                            { filterMode = m_FilterMode, depthBufferBits = m_DepthBufferBits, isShadowMap = true, name = m_Name, clearBuffer = passData.parameters.debugClearAtlas }, passData.parameters.atlasShaderID));

                if (passData.parameters.blurAlgorithm == BlurAlgorithm.EVSM)
                {
                    passData.momentAtlasTexture1 = builder.WriteTexture(AllocateMomentAtlas(builder, string.Format("{0}Moment", m_Name), passData.parameters.momentAtlasShaderID));
                    passData.momentAtlasTexture2 = builder.WriteTexture(AllocateMomentAtlas(builder, string.Format("{0}MomentCopy", m_Name)));
                }
                else if (passData.parameters.blurAlgorithm == BlurAlgorithm.IM)
                {
                    passData.momentAtlasTexture1 = builder.WriteTexture(builder.CreateTexture(new TextureDesc(width, height)
                                                    { colorFormat = GraphicsFormat.R32G32B32A32_SFloat, name = string.Format("{0}Moment", m_Name), enableRandomWrite = true }, passData.parameters.momentAtlasShaderID));
                    passData.intermediateSummedAreaTexture = builder.WriteTexture(builder.CreateTexture(new TextureDesc(width, height)
                                                    { colorFormat = GraphicsFormat.R32G32B32A32_SInt, name = string.Format("{0}IntermediateSummedArea", m_Name), enableRandomWrite = true }, passData.parameters.momentAtlasShaderID));
                    passData.summedAreaTexture = builder.WriteTexture(builder.CreateTexture(new TextureDesc(width, height)
                                                    { colorFormat = GraphicsFormat.R32G32B32A32_SInt, name = string.Format("{0}SummedArea", m_Name), enableRandomWrite = true }, passData.parameters.momentAtlasShaderID));
                }

                result.atlasTexture = passData.atlasTexture;
                result.momentAtlasTexture = passData.momentAtlasTexture1;

                builder.SetRenderFunc(
                (RenderShadowsPassData data, RenderGraphContext context) =>
                {
                    RTHandle atlasTexture = context.resources.GetTexture(data.atlasTexture);
                    RenderShadows(  data.parameters,
                                    atlasTexture,
                                    data.shadowDrawSettings,
                                    context.renderContext, context.cmd);

                    if (data.parameters.blurAlgorithm == BlurAlgorithm.EVSM)
                    {
                        RTHandle[] momentTextures = context.renderGraphPool.GetTempArray<RTHandle>(2);
                        momentTextures[0] = context.resources.GetTexture(data.momentAtlasTexture1);
                        momentTextures[1] = context.resources.GetTexture(data.momentAtlasTexture2);

                        EVSMBlurMoments(data.parameters, atlasTexture, momentTextures, context.cmd);
                    }
                    else if (data.parameters.blurAlgorithm == BlurAlgorithm.IM)
                    {
                        RTHandle momentAtlas = context.resources.GetTexture(data.momentAtlasTexture1);
                        RTHandle intermediateSummedArea = context.resources.GetTexture(data.intermediateSummedAreaTexture);
                        RTHandle summedArea = context.resources.GetTexture(data.summedAreaTexture);
                        IMBlurMoment(data.parameters, atlasTexture, momentAtlas, intermediateSummedArea, summedArea, context.cmd);
                    }
                });

                return result;
            }
        }
    }
}
