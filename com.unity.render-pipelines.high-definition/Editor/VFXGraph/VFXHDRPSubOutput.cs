using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering.HDPipeline;
using UnityEngine.Experimental.VFX;
using static UnityEditor.VFX.VFXAbstractRenderedOutput;
using static UnityEngine.Experimental.Rendering.HDPipeline.HDRenderQueue;

namespace UnityEditor.VFX
{
    class VFXHDRPSubOutput : VFXSRPSubOutput
    {
        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), Header("HDRP")]
        public OpaqueRenderQueue opaqueRenderQueue = OpaqueRenderQueue.Default;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector), Header("HDRP")]
        public TransparentRenderQueue transparentRenderQueue = TransparentRenderQueue.Default;

        private static readonly bool vfxManagerSupportsProcessCameraCommand = typeof(VFXManager).GetMethod("ProcessCameraCommand") != null;

        // Caps
        public override bool supportsExposure { get { return true; } } 
        public override bool supportsMotionVector { get { return vfxManagerSupportsProcessCameraCommand; } }

        protected override IEnumerable<string> filteredOutSettings
        {
            get
            {
                if (owner.isBlendModeOpaque)
                    yield return "transparentRenderQueue";
                else
                    yield return "opaqueRenderQueue";
            }
        }

        public override string GetBlendModeStr()
        {
            bool isLowRes = transparentRenderQueue == TransparentRenderQueue.LowResolution;
            bool isLit = owner is VFXAbstractParticleHDRPLitOutput;
            switch (owner.blendMode)
            {
                case BlendMode.Additive:
                    return string.Format("Blend {0} One {1}", isLit ? "One" : "SrcAlpha", isLowRes ? ", Zero One" : "");
                case BlendMode.Alpha:
                    return string.Format("Blend {0} OneMinusSrcAlpha {1}", isLit ? "One" : "SrcAlpha", isLowRes ? ", Zero OneMinusSrcAlpha" : "");
                case BlendMode.AlphaPremultiplied:
                    return string.Format("Blend One OneMinusSrcAlpha {0}", isLowRes ? ", Zero OneMinusSrcAlpha" : "");
                default:
                    return string.Empty;
            }
        }

        public override string GetRenderQueueStr()
        {
            RenderQueueType renderQueueType;
            string prefix = string.Empty;
            if (owner.isBlendModeOpaque)
            {
                prefix = "Geometry";
                renderQueueType = HDRenderQueue.ConvertFromOpaqueRenderQueue(opaqueRenderQueue);
            }
            else
            {
                prefix = "Transparent";
                renderQueueType = HDRenderQueue.ConvertFromTransparentRenderQueue(transparentRenderQueue);
            }

            int renderQueue = HDRenderQueue.ChangeType(renderQueueType, 0, owner.blendMode == BlendMode.Masked) - (int)(owner.isBlendModeOpaque ? Priority.Opaque : Priority.Transparent);
            return prefix + renderQueue.ToString("+#;-#;+0");
        }

        //TODO : extend & factorize this method
        public static void GetStencilStateForDepthOrMV(bool receiveDecals, bool receiveSSR, bool useObjectVelocity, out int stencilWriteMask, out int stencilRef)
        {
            stencilWriteMask = (int)UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline.StencilBitMask.DecalsForwardOutputNormalBuffer;
            stencilRef = receiveDecals ? (int)UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline.StencilBitMask.DecalsForwardOutputNormalBuffer : 0;

            stencilWriteMask |= (int)UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline.StencilBitMask.DoesntReceiveSSR;
            stencilRef |= !receiveSSR ? (int)UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline.StencilBitMask.DoesntReceiveSSR : 0;

            stencilWriteMask |= useObjectVelocity ? (int)UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline.StencilBitMask.ObjectMotionVectors : 0;
            stencilRef |= useObjectVelocity ? (int)UnityEngine.Experimental.Rendering.HDPipeline.HDRenderPipeline.StencilBitMask.ObjectMotionVectors : 0;
        }

        public override IEnumerable<KeyValuePair<string, VFXShaderWriter>> GetStencilStateOverridesStr()
        {
            //TODO : Add GBuffer & Distorsion stencil
            int stencilWriteMask, stencilRef;
            GetStencilStateForDepthOrMV(false, false, true, out stencilWriteMask, out stencilRef);
            var stencilForMV = new VFXShaderWriter();
            stencilForMV.WriteFormat("Stencil\n{{\n WriteMask {0}\n Ref {1}\n Comp Always\n Pass Replace\n}}", stencilWriteMask, stencilRef);
            yield return new KeyValuePair<string, VFXShaderWriter>("${VFXStencilMotionVector}", stencilForMV);
        }
    }
}
