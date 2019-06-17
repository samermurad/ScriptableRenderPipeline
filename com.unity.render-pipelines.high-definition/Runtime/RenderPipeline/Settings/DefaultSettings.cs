using UnityEngine.Rendering;

namespace UnityEngine.Experimental.Rendering.HDPipeline
{
    public class DefaultSettings
    {
        public const string defaultVolumeProfileFileStem = "DefaultSettingsVolumeProfile";

        public static VolumeProfile defaultVolumeProfile => Resources.Load<VolumeProfile>(defaultVolumeProfileFileStem);
    }
}
