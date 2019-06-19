using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public static class DefaultSettings
    {
        static DefaultSettings()
        {
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (s_DefaultVolume != null && !s_DefaultVolume.Equals(null))
                {
                    Object.Destroy(s_DefaultVolume.gameObject);
                    s_DefaultVolume = null;
                }
            };
#endif
        }

        private static Volume s_DefaultVolume = null;

        public static HDRenderPipelineAsset hdrpAssetWithDefaultSettings
            => GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset hdrpAsset ? hdrpAsset : null;

        public static VolumeProfile defaultVolumeProfile
            => hdrpAssetWithDefaultSettings?.defaultVolumeProfile;

        public static Volume GetOrCreateDefaultVolume()
        {
            if (s_DefaultVolume == null || s_DefaultVolume.Equals(null))
            {
                var go = new GameObject("Default Volume") { hideFlags = HideFlags.HideAndDontSave };
                s_DefaultVolume = go.AddComponent<Volume>();
                s_DefaultVolume.isGlobal = true;
                s_DefaultVolume.priority = float.MinValue;
                s_DefaultVolume.profile = DefaultSettings.defaultVolumeProfile;
            }
            if (
                // In case the asset was deleted or the reference removed
                s_DefaultVolume.profile == null || s_DefaultVolume.profile.Equals(null)
                #if UNITY_EDITOR
                // In case the serialization recreated an empty volume profile
                || !UnityEditor.AssetDatabase.Contains(s_DefaultVolume.profile)
                #endif
            )
                s_DefaultVolume.profile = DefaultSettings.defaultVolumeProfile;

            return s_DefaultVolume;
        }
    }
}
