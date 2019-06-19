using System;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.Rendering;
using UnityEditor.Experimental.Rendering.HDPipeline;
#endif
using UnityEngine.Serialization;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public partial class HDAdditionalLightData : ISerializationCallbackReceiver
    {
        // TODO: Use proper migration toolkit
        // 3. Added ShadowNearPlane to HDRP additional light data, we don't use Light.shadowNearPlane anymore
        // 4. Migrate HDAdditionalLightData.lightLayer to Light.renderingLayerMask
        // 5. Added the ShadowLayer
        private const int currentVersion = 5;

        [HideInInspector, SerializeField]
        [FormerlySerializedAs("m_Version")]
        [System.Obsolete("version is deprecated, use m_Version instead")]
        private float version = currentVersion;
        [SerializeField]
        private int m_Version = currentVersion;

        // To be able to have correct default values for our lights and to also control the conversion of intensity from the light editor (so it is compatible with GI)
        // we add intensity (for each type of light we want to manage).
        [System.Obsolete("directionalIntensity is deprecated, use intensity and lightUnit instead")]
        public float directionalIntensity = k_DefaultDirectionalLightIntensity;
        [System.Obsolete("punctualIntensity is deprecated, use intensity and lightUnit instead")]
        public float punctualIntensity = k_DefaultPunctualLightIntensity;
        [System.Obsolete("areaIntensity is deprecated, use intensity and lightUnit instead")]
        public float areaIntensity = k_DefaultAreaLightIntensity;

        [Obsolete("Use Light.renderingLayerMask instead")]
        public LightLayerEnum lightLayers = LightLayerEnum.LightLayerDefault;

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            // Note: the field version is deprecated but we keep it for retro-compatibility reasons, you should use m_Version instead
#pragma warning disable 618
            if (version <= 1.0f)
#pragma warning restore 618
            {
                // Note: We can't access to the light component in OnAfterSerialize as it is not init() yet,
                // so instead we use a boolean to do the upgrade in OnEnable().
                // However OnEnable is not call when the light is disabled, so the HDLightEditor also call
                // the UpgradeLight() code in this case
                needsIntensityUpdate_1_0 = true;
            }
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            UpdateBounds();
        }

        void OnEnable()
        {
            UpgradeLight();

            // Null check, the first time we create the light there is no shadowData attached
            if (m_ShadowData?.shadowUpdateMode == ShadowUpdateMode.OnEnable)
                m_ShadowMapRenderedSinceLastRequest = false;
        }

        public void UpgradeLight()
        {
// Disable the warning generated by deprecated fields (areaIntensity, directionalIntensity, ...)
#pragma warning disable 618

            // If we are deserializing an old version, convert the light intensity to the new system
            if (needsIntensityUpdate_1_0)
            {
                switch (lightTypeExtent)
                {
                    case LightTypeExtent.Punctual:
                        switch (legacyLight.type)
                        {
                            case LightType.Directional:
                                lightUnit = LightUnit.Lux;
                                intensity = directionalIntensity;
                                break;
                            case LightType.Spot:
                            case LightType.Point:
                                lightUnit = LightUnit.Lumen;
                                intensity = punctualIntensity;
                                break;
                        }
                        break;
                    case LightTypeExtent.Tube:
                    case LightTypeExtent.Rectangle:
                        lightUnit = LightUnit.Lumen;
                        intensity = areaIntensity;
                        break;
                }
                needsIntensityUpdate_1_0 = false;
            }
            if (m_Version <= 2)
            {
                // ShadowNearPlane have been move to HDRP as default legacy unity clamp it to 0.1 and we need to be able to go below that
                shadowNearPlane = legacyLight.shadowNearPlane;
            }
            if (m_Version <= 3)
            {
                legacyLight.renderingLayerMask = LightLayerToRenderingLayerMask((int)lightLayers, legacyLight.renderingLayerMask);
            }
            if (m_Version <= 4)
            {
                // When we upgrade the option to decouple light and shadow layers will be disabled
                // so we can sync the shadow layer mask (from the legacyLight) and the new light layer mask
                lightlayersMask = (LightLayerEnum)RenderingLayerMaskToLightLayer(legacyLight.renderingLayerMask);
            }

            m_Version = currentVersion;
            version = currentVersion;

#pragma warning restore 0618
        }
    }
}
