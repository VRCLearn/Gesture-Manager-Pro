using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VRCLearn.GestureManagerPro.Diagnostics
{
    /// <summary>Observes the avatar after other SDK preprocessing callbacks without invoking them again.</summary>
    public sealed class ModelStatisticsPreprocessCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            // Diagnostics must never prevent an upload when a scanner or extension throws.
            ModelStatisticsService.ObservePreprocessedAvatar(avatarGameObject);
            return true;
        }
    }
}
