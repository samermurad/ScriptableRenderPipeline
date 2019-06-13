using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;

namespace UnityEngine.Rendering.LWRP
{
    [Serializable]
    [FormerName("UnityEngine.Experimental.Rendering.LightweightPipeline.LightWeightUnlitSubShader")]
    [FormerName("UnityEditor.ShaderGraph.LightWeightUnlitSubShader")]
    class LightWeightUnlitSubShader : IUnlitSubShader
    {
        Pass m_UnlitPass = new Pass
        {
            Name = "Pass",
            TemplatePath = "lightweightUnlitPass.template",
            PixelShaderSlots = new List<int>
            {
                UnlitMasterNode.ColorSlotId,
                UnlitMasterNode.AlphaSlotId,
                UnlitMasterNode.AlphaThresholdSlotId
            },
            VertexShaderSlots = new List<int>()
            {
                UnlitMasterNode.PositionSlotId
            },
            Requirements = new ShaderGraphRequirements()
            {
                requiresNormal = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresTangent = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresBitangent = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresPosition = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresViewDir = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresMeshUVs = new List<UVChannel>() { UVChannel.UV1 },
            },
            ExtraDefines = new List<string>(),
            OnGeneratePassImpl = (IMasterNode node, ref Pass pass, ref ShaderGraphRequirements requirements) =>
            {
                var masterNode = node as UnlitMasterNode;

                if (masterNode.IsSlotConnected(PBRMasterNode.AlphaThresholdSlotId))
                    pass.ExtraDefines.Add("#define _AlphaClip 1");
                if (masterNode.surfaceType == SurfaceType.Transparent && masterNode.alphaMode == AlphaMode.Premultiply)
                    pass.ExtraDefines.Add("#define _ALPHAPREMULTIPLY_ON 1");
                if (requirements.requiresDepthTexture)
                    pass.ExtraDefines.Add("#define REQUIRE_DEPTH_TEXTURE");
                if (requirements.requiresCameraOpaqueTexture)
                    pass.ExtraDefines.Add("#define REQUIRE_OPAQUE_TEXTURE");
            }
        };

        Pass m_DepthShadowPass = new Pass()
        {
            Name = "",
            TemplatePath = "lightweightUnlitExtraPasses.template",
            PixelShaderSlots = new List<int>()
            {
                PBRMasterNode.AlphaSlotId,
                PBRMasterNode.AlphaThresholdSlotId
            },
            VertexShaderSlots = new List<int>()
            {
                PBRMasterNode.PositionSlotId
            },
            Requirements = new ShaderGraphRequirements()
            {
                requiresNormal = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresTangent = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresBitangent = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresPosition = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresViewDir = LWRPSubShaderUtilities.k_PixelCoordinateSpace,
                requiresMeshUVs = new List<UVChannel>() { UVChannel.UV1 },
            },
            ExtraDefines = new List<string>(),
            OnGeneratePassImpl = (IMasterNode node, ref Pass pass, ref ShaderGraphRequirements requirements) =>
            {
                var masterNode = node as UnlitMasterNode;

                if (masterNode.IsSlotConnected(PBRMasterNode.AlphaThresholdSlotId))
                    pass.ExtraDefines.Add("#define _AlphaClip 1");
                if (masterNode.surfaceType == SurfaceType.Transparent && masterNode.alphaMode == AlphaMode.Premultiply)
                    pass.ExtraDefines.Add("#define _ALPHAPREMULTIPLY_ON 1");
                if (requirements.requiresDepthTexture)
                    pass.ExtraDefines.Add("#define REQUIRE_DEPTH_TEXTURE");
                if (requirements.requiresCameraOpaqueTexture)
                    pass.ExtraDefines.Add("#define REQUIRE_OPAQUE_TEXTURE");
            }
        };
        
        public int GetPreviewPassIndex() { return 0; }

        public string GetSubshader(IMasterNode masterNode, GenerationMode mode, List<string> sourceAssetDependencyPaths = null)
        {
            if (sourceAssetDependencyPaths != null)
            {
                // LightWeightPBRSubShader.cs
                sourceAssetDependencyPaths.Add(AssetDatabase.GUIDToAssetPath("3ef30c5c1d5fc412f88511ef5818b654"));
            }

            // Master Node data
            var unlitMasterNode = masterNode as UnlitMasterNode;
            var tags = ShaderGenerator.BuildMaterialTags(unlitMasterNode.surfaceType);
            var options = ShaderGenerator.GetMaterialOptions(unlitMasterNode.surfaceType, unlitMasterNode.alphaMode, unlitMasterNode.twoSided.isOn);

            // Passes
            var passes = new Pass[] { m_UnlitPass, m_DepthShadowPass };

            return LWRPSubShaderUtilities.GetSubShader<UnlitMasterNode>(unlitMasterNode, tags, options, 
                passes, mode, sourceAssetDependencyPaths: sourceAssetDependencyPaths);
        }

        public bool IsPipelineCompatible(RenderPipelineAsset renderPipelineAsset)
        {
            return renderPipelineAsset is LightweightRenderPipelineAsset;
        }
    }
}
