using System;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    using RTHandle = RTHandleSystem.RTHandle;

    public partial class HDRenderPipeline
    {
        void ExecuteWithRenderGraph(RenderRequest renderRequest, ScriptableRenderContext renderContext, CommandBuffer cmd, DensityVolumeList densityVolumes)
        {
            var hdCamera = renderRequest.hdCamera;
            var camera = hdCamera.camera;
            var cullingResults = renderRequest.cullingResults.cullingResults;
            bool msaa = hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA);
            var target = renderRequest.target;

            CreateSharedResources(m_RenderGraph, hdCamera, m_CurrentDebugDisplaySettings);

#if UNITY_EDITOR
            var showGizmos = camera.cameraType == CameraType.Game
                || camera.cameraType == CameraType.SceneView;
#endif

            RenderGraphMutableResource colorBuffer = CreateColorBuffer(m_RenderGraph, hdCamera, msaa);
            RenderGraphMutableResource diffuseLightingBuffer = CreateDiffuseLightingBuffer(m_RenderGraph, msaa);
            RenderGraphMutableResource sssBuffer = CreateSSSBuffer(m_RenderGraph, msaa);

            if (m_CurrentDebugDisplaySettings.IsDebugMaterialDisplayEnabled() || m_CurrentDebugDisplaySettings.IsMaterialValidationEnabled())
            {
                StartLegacyStereo(m_RenderGraph, hdCamera);
                RenderDebugViewMaterial(m_RenderGraph, cullingResults, hdCamera, colorBuffer);
                colorBuffer = ResolveMSAAColor(m_RenderGraph, hdCamera, colorBuffer, CreateColorBuffer(m_RenderGraph, hdCamera, false));
                StopLegacyStereo(m_RenderGraph, hdCamera);
            }
            else
            {
                var prepassOutput = RenderPrepass(m_RenderGraph, sssBuffer, cullingResults, hdCamera);

                //// Caution: We require sun light here as some skies use the sun light to render, it means that UpdateSkyEnvironment must be called after PrepareLightsForGPU.
                //// TODO: Try to arrange code so we can trigger this call earlier and use async compute here to run sky convolution during other passes (once we move convolution shader to compute).
                //UpdateSkyEnvironment(hdCamera, cmd);

                StartLegacyStereo(m_RenderGraph, hdCamera);

#if ENABLE_RAYTRACING
                bool raytracedIndirectDiffuse = m_RaytracingIndirectDiffuse.RenderIndirectDiffuse(hdCamera, cmd, renderContext, m_FrameCount);
                if(raytracedIndirectDiffuse)
                {
                    PushFullScreenDebugTexture(hdCamera, cmd, m_RaytracingIndirectDiffuse.GetIndirectDiffuseTexture(), FullScreenDebugMode.IndirectDiffuse);
                }
#endif

                //if (!hdCamera.frameSettings.SSAORunsAsync())
                //    m_AmbientOcclusionSystem.Render(cmd, hdCamera, m_SharedRTManager, renderContext, m_FrameCount);

                var stencilBufferCopy = CopyStencilBufferIfNeeded(m_RenderGraph, hdCamera, GetDepthStencilBuffer(), m_CopyStencil, m_CopyStencilForSSR);

                //// When debug is enabled we need to clear otherwise we may see non-shadows areas with stale values.
                //if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.ContactShadows) && m_CurrentDebugDisplaySettings.data.fullScreenDebugMode == FullScreenDebugMode.ContactShadows)
                //{
                //    HDUtils.SetRenderTarget(cmd, m_ContactShadowBuffer, ClearFlag.Color, Color.clear);
                //}

//#if ENABLE_RAYTRACING
//                // Update the light clusters that we need to update
//                m_RayTracingManager.UpdateCameraData(cmd, hdCamera);

//                // We only request the light cluster if we are gonna use it for debug mode
//                if (FullScreenDebugMode.LightCluster == m_CurrentDebugDisplaySettings.data.fullScreenDebugMode)
//                {
//                    var settings = VolumeManager.instance.stack.GetComponent<ScreenSpaceReflection>();
//                    HDRaytracingEnvironment rtEnv = m_RayTracingManager.CurrentEnvironment();
//                    if(settings.enableRaytracing.value && rtEnv != null)
//                    {
//                        HDRaytracingLightCluster lightCluster = m_RayTracingManager.RequestLightCluster(rtEnv.reflLayerMask);
//                        PushFullScreenDebugTexture(hdCamera, cmd, lightCluster.m_DebugLightClusterTexture, FullScreenDebugMode.LightCluster);
//                    }
//                    else if(rtEnv != null && rtEnv.raytracedObjects)
//                    {
//                        HDRaytracingLightCluster lightCluster = m_RayTracingManager.RequestLightCluster(rtEnv.raytracedLayerMask);
//                        PushFullScreenDebugTexture(hdCamera, cmd, lightCluster.m_DebugLightClusterTexture, FullScreenDebugMode.LightCluster);
//                    }
//                }
//#endif

//                // Evaluate raytraced area shadows if required
//                bool areaShadowsRendered = false;
//#if ENABLE_RAYTRACING
//                areaShadowsRendered = m_RaytracingShadows.RenderAreaShadows(hdCamera, cmd, renderContext, m_FrameCount);
//#endif
//                cmd.SetGlobalInt(HDShaderIDs._RaytracedAreaShadow, areaShadowsRendered ? 1 : 0);

                StopLegacyStereo(m_RenderGraph, hdCamera);

                BuildGPULightList(m_RenderGraph, hdCamera, GetDepthStencilBuffer(msaa), stencilBufferCopy);

                RenderShadows(m_RenderGraph, hdCamera, cullingResults);

                StartLegacyStereo(m_RenderGraph, hdCamera);

                var deferredLightingOutput = RenderDeferredLighting(m_RenderGraph, hdCamera, colorBuffer, diffuseLightingBuffer, prepassOutput.gbuffer);

                RenderForwardOpaque(m_RenderGraph, hdCamera, colorBuffer, diffuseLightingBuffer, sssBuffer, GetDepthStencilBuffer(msaa), cullingResults);

                diffuseLightingBuffer = ResolveMSAAColor(m_RenderGraph, hdCamera, diffuseLightingBuffer, CreateDiffuseLightingBuffer(m_RenderGraph, false));
                sssBuffer = ResolveMSAAColor(m_RenderGraph, hdCamera, sssBuffer, CreateSSSBuffer(m_RenderGraph, false));

                RenderSubsurfaceScattering(m_RenderGraph, hdCamera,
                    colorBuffer, diffuseLightingBuffer, sssBuffer, GetDepthStencilBuffer(msaa), GetDepthTexture());

                RenderDecalsForwardEmissive(m_RenderGraph, hdCamera, cullingResults);

                // Render pre-refraction objects
                RenderForwardTransparent(m_RenderGraph, hdCamera, colorBuffer, GetMotionVectorsBuffer(msaa), GetNormalBuffer(msaa), GetDepthStencilBuffer(msaa), cullingResults, true);

                // Color pyramid

                // Render all type of transparent forward (unlit, lit, complex (hair...)) to keep the sorting between transparent objects.
                RenderForwardTransparent(m_RenderGraph, hdCamera, colorBuffer, GetMotionVectorsBuffer(msaa), GetNormalBuffer(msaa), GetDepthStencilBuffer(msaa), cullingResults, false);

                colorBuffer = ResolveMSAAColor(m_RenderGraph, hdCamera, colorBuffer, CreateColorBuffer(m_RenderGraph, hdCamera, false));

                StopLegacyStereo(m_RenderGraph, hdCamera);

                BlitFinalCameraTexture(m_RenderGraph, hdCamera, colorBuffer, target.id);
            }

            ExecuteRenderGraph(m_RenderGraph, hdCamera, m_MSAASamples, renderContext, cmd);
        }

        static void ExecuteRenderGraph(RenderGraph renderGraph, HDCamera hdCamera, MSAASamples msaaSample, ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            var renderGraphParams = new RenderGraphExecuteParams()
            {
                renderingWidth = hdCamera.actualWidth,
                renderingHeight = hdCamera.actualHeight,
                msaaSamples = msaaSample
            };

            renderGraph.Execute(renderContext, cmd, renderGraphParams);
        }

        class FinalBlitPassData
        {
            public BlitFinalCameraTextureParameters parameters;
            public RenderGraphResource              source;
            public RenderTargetIdentifier           destination;
        }

        void BlitFinalCameraTexture(RenderGraph renderGraph, HDCamera hdCamera, RenderGraphResource source, RenderTargetIdentifier destination)
        {
            using (var builder = renderGraph.AddRenderPass<FinalBlitPassData>("Final Blit (Dev Build Only)", out var passData))
            {
                passData.parameters = PrepareFinalBlitParameters(hdCamera);
                passData.source = builder.ReadTexture(source);
                passData.destination = destination;

                builder.SetRenderFunc(
                (FinalBlitPassData data, RenderGraphContext context) =>
                {
                    var sourceTexture = context.resources.GetTexture(data.source);
                    BlitFinalCameraTexture(data.parameters, context.renderGraphPool.GetTempMaterialPropertyBlock(), sourceTexture, data.destination, context.cmd);
                });
            }

        }

        class ForwardPassData
        {
            public RenderGraphResource          rendererList;
            public RenderGraphMutableResource[] renderTarget = new RenderGraphMutableResource[3];
            public int                          renderTargetCount;
            public RenderGraphMutableResource   depthBuffer;
            public ComputeBuffer                lightListBuffer;
            public FrameSettings                frameSettings;
            public bool                         decalsEnabled;
            public bool                         renderMotionVecForTransparent;
        }

        void PrepareForwardPassData(RenderGraphBuilder builder, ForwardPassData data, bool opaque, FrameSettings frameSettings, RendererListDesc rendererListDesc, RenderGraphMutableResource depthBuffer)
        {
            bool useFptl = frameSettings.IsEnabled(FrameSettingsField.FPTLForForwardOpaque) && opaque;

            data.frameSettings = frameSettings;
            data.lightListBuffer = useFptl ? s_LightList : s_PerVoxelLightLists;
            data.depthBuffer = builder.UseDepthBuffer(depthBuffer, DepthAccess.ReadWrite);
            data.rendererList = builder.UseRendererList(builder.CreateRendererList(rendererListDesc));
            // enable d-buffer flag value is being interpreted more like enable decals in general now that we have clustered
            // decal datas count is 0 if no decals affect transparency
            data.decalsEnabled = (frameSettings.IsEnabled(FrameSettingsField.Decals)) && (DecalSystem.m_DecalDatasCount > 0);
            data.renderMotionVecForTransparent = NeedMotionVectorForTransparent(frameSettings);
        }

        // Guidelines: In deferred by default there is no opaque in forward. However it is possible to force an opaque material to render in forward
        // by using the pass "ForwardOnly". In this case the .shader should not have "Forward" but only a "ForwardOnly" pass.
        // It must also have a "DepthForwardOnly" and no "DepthOnly" pass as forward material (either deferred or forward only rendering) have always a depth pass.
        // The RenderForward pass will render the appropriate pass depends on the engine settings. In case of forward only rendering, both "Forward" pass and "ForwardOnly" pass
        // material will be render for both transparent and opaque. In case of deferred, both path are used for transparent but only "ForwardOnly" is use for opaque.
        // (Thus why "Forward" and "ForwardOnly" are exclusive, else they will render two times"
        void RenderForwardOpaque(   RenderGraph                 renderGraph,
                                    HDCamera                    hdCamera,
                                    RenderGraphMutableResource  colorBuffer,
                                    RenderGraphMutableResource  diffuseLightingBuffer,
                                    RenderGraphMutableResource  sssBuffer,
                                    RenderGraphMutableResource  depthBuffer,
                                    CullingResults              cullResults)
        {
            bool debugDisplay = m_CurrentDebugDisplaySettings.IsDebugDisplayEnabled();

            using (var builder = renderGraph.AddRenderPass<ForwardPassData>(debugDisplay ? "Forward Opaque Debug" : "Forward Opaque", out var passData, CustomSamplerId.ForwardPassName.GetSampler()))
            {
                PrepareForwardPassData(builder, passData, true, hdCamera.frameSettings, PrepareForwardOpaqueRendererList(cullResults, hdCamera), depthBuffer);

                // In case of forward SSS we will bind all the required target. It is up to the shader to write into it or not.
                if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.SubsurfaceScattering))
                {
                    passData.renderTarget[0] = builder.WriteTexture(colorBuffer); // Store the specular color
                    passData.renderTarget[1] = builder.WriteTexture(diffuseLightingBuffer);
                    passData.renderTarget[2] = builder.WriteTexture(sssBuffer);
                    passData.renderTargetCount = 3;
                }
                else
                {
                    passData.renderTarget[0] = builder.WriteTexture(colorBuffer);
                    passData.renderTargetCount = 1;
                }

                builder.SetRenderFunc(
                (ForwardPassData data, RenderGraphContext context) =>
                {
                    // TODO: replace with UseColorBuffer when removing old rendering (SetRenderTarget is called inside RenderForwardRendererList because of that).
                    var mrt = context.renderGraphPool.GetTempArray<RenderTargetIdentifier>(data.renderTargetCount);
                    for (int i = 0; i < data.renderTargetCount; ++i)
                        mrt[i] = context.resources.GetTexture(data.renderTarget[i]);

                    RenderForwardRendererList(data.frameSettings,
                            context.resources.GetRendererList(data.rendererList),
                            mrt,
                            context.resources.GetTexture(data.depthBuffer),
                            data.lightListBuffer,
                            true, context.renderContext, context.cmd);
                });
            }
        }

        void RenderForwardTransparent(  RenderGraph                 renderGraph,
                                        HDCamera                    hdCamera,
                                        RenderGraphMutableResource  colorBuffer,
                                        RenderGraphMutableResource  motionVectorBuffer,
                                        RenderGraphMutableResource  dummyBuffer,
                                        RenderGraphMutableResource  depthBuffer,
                                        CullingResults              cullResults,
                                        bool                        preRefractionPass)
        {
            // If rough refraction are turned off, we render all transparents in the Transparent pass and we skip the PreRefraction one.
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.RoughRefraction) && preRefractionPass)
                return;

            string passName;
            bool debugDisplay = m_CurrentDebugDisplaySettings.IsDebugDisplayEnabled();
            if (debugDisplay)
                passName = preRefractionPass ? "Forward PreRefraction Debug" : "Forward Transparent Debug";
            else
                passName = preRefractionPass ? "Forward PreRefraction" : "Forward Transparent";

            using (var builder = renderGraph.AddRenderPass<ForwardPassData>(passName, out var passData, CustomSamplerId.ForwardPassName.GetSampler()))
            {
                PrepareForwardPassData(builder, passData, false, hdCamera.frameSettings, PrepareForwardTransparentRendererList(cullResults, hdCamera, preRefractionPass), depthBuffer);

                bool renderMotionVecForTransparent = NeedMotionVectorForTransparent(hdCamera.frameSettings);

                passData.renderTargetCount = 2;
                passData.renderTarget[0] = builder.WriteTexture(colorBuffer);
                passData.renderTarget[1] = builder.WriteTexture(renderMotionVecForTransparent ? motionVectorBuffer :
                // It doesn't really matter what gets bound here since the color mask state set will prevent this from ever being written to. However, we still need to bind something
                // to avoid warnings about unbound render targets. The following rendertarget could really be anything if renderVelocitiesForTransparent, here the normal buffer
                // as it is guaranteed to exist and to have the same size.
                // to avoid warnings about unbound render targets.
                dummyBuffer);

                builder.SetRenderFunc(
                    (ForwardPassData data, RenderGraphContext context) =>
                {
                    // TODO: replace with UseColorBuffer when removing old rendering.
                    var mrt = context.renderGraphPool.GetTempArray<RenderTargetIdentifier>(data.renderTargetCount);
                    for (int i = 0; i < data.renderTargetCount; ++i)
                        mrt[i] = context.resources.GetTexture(data.renderTarget[i]);

                    context.cmd.SetGlobalInt(HDShaderIDs._ColorMaskTransparentVel, data.renderMotionVecForTransparent ? (int)ColorWriteMask.All : 0);
                    if (data.decalsEnabled)
                        DecalSystem.instance.SetAtlas(context.cmd); // for clustered decals

                    RenderForwardRendererList(  data.frameSettings,
                                                context.resources.GetRendererList(data.rendererList),
                                                mrt,
                                                context.resources.GetTexture(data.depthBuffer),
                                                data.lightListBuffer,
                                                false, context.renderContext, context.cmd);
                });
            }
        }

        class RenderDecalsForwardEmissivePassData
        {
            public RenderGraphResource rendererList;
        }

        void RenderDecalsForwardEmissive(RenderGraph renderGraph, HDCamera hdCamera, CullingResults cullingResults)
        {
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.Decals))
                return;

            using (var builder = renderGraph.AddRenderPass<RenderDecalsForwardEmissivePassData>("DecalsForwardEmissive", out var passData, CustomSamplerId.DecalsForwardEmissive.GetSampler()))
            {
                passData.rendererList = builder.UseRendererList(builder.CreateRendererList(PrepareForwardEmissiveRendererList(cullingResults, hdCamera)));

                builder.SetRenderFunc(
                    (RenderDecalsForwardEmissivePassData data, RenderGraphContext context) =>
                {
                    HDUtils.DrawRendererList(context.renderContext, context.cmd, context.resources.GetRendererList(data.rendererList));
                    DecalSystem.instance.RenderForwardEmissive(context.cmd);
                });
            }
        }

        RenderGraphMutableResource CreateColorBuffer(RenderGraph renderGraph, HDCamera hdCamera, bool msaa)
        {
            return renderGraph.CreateTexture(
                new TextureDesc(Vector2.one)
                {
                    colorFormat = GetColorBufferFormat(),
                    enableRandomWrite = !msaa,
                    bindTextureMS = msaa,
                    enableMSAA = msaa,
                    slices = TextureXR.slices,
                    dimension = TextureXR.dimension,
                    useDynamicScale = true,
                    clearBuffer = NeedClearColorBuffer(hdCamera),
                    clearColor = GetColorBufferClearColor(hdCamera),
                    name = "CameraColor" });
        }

        class DebugViewMaterialData
        {
            public RenderGraphMutableResource   outputColor;
            public RenderGraphMutableResource   outputDepth;
            public RenderGraphResource          opaqueRendererList;
            public RenderGraphResource          transparentRendererList;
            public Material                     debugGBufferMaterial;
            public FrameSettings                frameSettings;
        }

        void RenderDebugViewMaterial(RenderGraph renderGraph, CullingResults cull, HDCamera hdCamera, RenderGraphMutableResource output)
        {
            if (m_CurrentDebugDisplaySettings.data.materialDebugSettings.IsDebugGBufferEnabled() && hdCamera.frameSettings.litShaderMode == LitShaderMode.Deferred)
            {
                using (var builder = renderGraph.AddRenderPass<DebugViewMaterialData>("DebugViewMaterialGBuffer", out var passData, CustomSamplerId.DebugViewMaterialGBuffer.GetSampler()))
                {
                    passData.debugGBufferMaterial = m_currentDebugViewMaterialGBuffer;
                    passData.outputColor = builder.WriteTexture(output);

                    builder.SetRenderFunc(
                    (DebugViewMaterialData data, RenderGraphContext context) =>
                    {
                        var res = context.resources;
                        HDUtils.DrawFullScreen(context.cmd, data.debugGBufferMaterial, res.GetTexture(data.outputColor));
                    });
                }
            }
            else
            {
                using (var builder = renderGraph.AddRenderPass<DebugViewMaterialData>("DisplayDebug ViewMaterial", out var passData, CustomSamplerId.DisplayDebugViewMaterial.GetSampler()))
                {
                    passData.frameSettings = hdCamera.frameSettings;
                    passData.outputColor = builder.UseColorBuffer(output, 0);
                    passData.outputDepth = builder.UseDepthBuffer(GetDepthStencilBuffer(hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA)), DepthAccess.ReadWrite);

                    // When rendering debug material we shouldn't rely on a depth prepass for optimizing the alpha clip test. As it is control on the material inspector side
                    // we must override the state here.
                    passData.opaqueRendererList = builder.UseRendererList(
                        builder.CreateRendererList(CreateOpaqueRendererListDesc(cull, hdCamera.camera, m_AllForwardOpaquePassNames,
                            rendererConfiguration : m_CurrentRendererConfigurationBakedLighting,
                            stateBlock : m_DepthStateOpaque)));
                    passData.transparentRendererList= builder.UseRendererList(
                        builder.CreateRendererList(CreateTransparentRendererListDesc(cull, hdCamera.camera, m_AllTransparentPassNames,
                            rendererConfiguration : m_CurrentRendererConfigurationBakedLighting,
                            stateBlock : m_DepthStateOpaque)));

                    builder.SetRenderFunc(
                    (DebugViewMaterialData data, RenderGraphContext context) =>
                    {
                        var res = context.resources;
                        DrawOpaqueRendererList(context, data.frameSettings, res.GetRendererList(data.opaqueRendererList));
                        DrawTransparentRendererList(context, data.frameSettings, res.GetRendererList(data.transparentRendererList));
                    });
                }
            }
        }

        class ResolveColorData
        {
            public RenderGraphResource          input;
            public RenderGraphMutableResource   output;
            public Material                     resolveMaterial;
            public int                          passIndex;
        }

        RenderGraphMutableResource ResolveMSAAColor(RenderGraph renderGraph, HDCamera hdCamera, RenderGraphMutableResource input, RenderGraphMutableResource output)
        {
            if (hdCamera.frameSettings.IsEnabled(FrameSettingsField.MSAA))
            {
                using (var builder = renderGraph.AddRenderPass<ResolveColorData>("ResolveColor", out var passData))
                {
                    passData.input = builder.ReadTexture(input);
                    passData.output = builder.UseColorBuffer(output, 0);
                    passData.resolveMaterial = m_ColorResolveMaterial;
                    passData.passIndex = SampleCountToPassIndex(m_MSAASamples);

                    builder.SetRenderFunc(
                    (ResolveColorData data, RenderGraphContext context) =>
                    {
                        var res = context.resources;
                        var mpb = context.renderGraphPool.GetTempMaterialPropertyBlock();
                        mpb.SetTexture(HDShaderIDs._ColorTextureMS, res.GetTexture(data.input));
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.resolveMaterial, data.passIndex, MeshTopology.Triangles, 3, 1, mpb);
                    });

                    return passData.output;
                }
            }
            else
            {
                return input;
            }
        }

        static void DrawOpaqueRendererList(in RenderGraphContext context, in FrameSettings frameSettings, in RendererList rendererList)
        {
            DrawOpaqueRendererList(context.renderContext, context.cmd, frameSettings, rendererList);
        }

        static void DrawTransparentRendererList(in RenderGraphContext context, in FrameSettings frameSettings, RendererList rendererList)
        {
            DrawTransparentRendererList(context.renderContext, context.cmd, frameSettings, rendererList);
        }

        static int SampleCountToPassIndex(MSAASamples samples)
        {
            switch (samples)
            {
                case MSAASamples.None:
                    return 0;
                case MSAASamples.MSAA2x:
                    return 1;
                case MSAASamples.MSAA4x:
                    return 2;
                case MSAASamples.MSAA8x:
                    return 3;
            };
            return 0;
        }

        bool NeedClearColorBuffer(HDCamera hdCamera)
        {
            if (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Color ||
                // If the luxmeter is enabled, the sky isn't rendered so we clear the background color
                m_CurrentDebugDisplaySettings.data.lightingDebugSettings.debugLightingMode == DebugLightingMode.LuxMeter ||
                // If we want the sky but the sky doesn't exist, still clear with background color
                (hdCamera.clearColorMode == HDAdditionalCameraData.ClearColorMode.Sky && !m_SkyManager.IsVisualSkyValid()) ||
                m_CurrentDebugDisplaySettings.IsDebugMaterialDisplayEnabled() || m_CurrentDebugDisplaySettings.IsMaterialValidationEnabled() ||
                // Special handling for Preview we force to clear with background color (i.e black)
                HDUtils.IsRegularPreviewCamera(hdCamera.camera)
                )
            {
                return true;
            }

            return false;
        }

        Color GetColorBufferClearColor(HDCamera hdCamera)
        {
            Color clearColor = hdCamera.backgroundColorHDR;
            // We set the background color to black when the luxmeter is enabled to avoid picking the sky color
            if (debugDisplaySettings.data.lightingDebugSettings.debugLightingMode == DebugLightingMode.LuxMeter)
                clearColor = Color.black;

            return clearColor;
        }

        // XR Specific
        class StereoRenderingPassData
        {
            public Camera camera;
            public XRPass xr;
        }

        void StartLegacyStereo(RenderGraph renderGraph, HDCamera hdCamera)
        {
            if (hdCamera.xr.enabled)
            {
                using (var builder = renderGraph.AddRenderPass<StereoRenderingPassData>("StartStereoRendering", out var passData))
                {
                    passData.camera = hdCamera.camera;
                    passData.xr = hdCamera.xr;

                    builder.SetRenderFunc(
                    (StereoRenderingPassData data, RenderGraphContext context) =>
                    {
                        data.xr.StartLegacyStereo(data.camera, context.cmd, context.renderContext);
                    });
                }
            }
        }

        void StopLegacyStereo(RenderGraph renderGraph, HDCamera hdCamera)
        {
            if (hdCamera.xr.enabled && hdCamera.camera.stereoEnabled)
            {
                using (var builder = renderGraph.AddRenderPass<StereoRenderingPassData>("StopStereoRendering", out var passData))
                {
                    passData.camera = hdCamera.camera;
                    passData.xr = hdCamera.xr;

                    builder.SetRenderFunc(
                    (StereoRenderingPassData data, RenderGraphContext context) =>
                    {
                        data.xr.StopLegacyStereo(data.camera, context.cmd, context.renderContext);
                    });
                }
            }
        }
    }
}
