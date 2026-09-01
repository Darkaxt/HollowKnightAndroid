#if UNITY_ANDROID && !UNITY_EDITOR
namespace DualSouls.Mods.HollowKnight
{
    public sealed class HollowKnightGameTweakApi : IHollowKnightTweakApi
    {
        bool _captured;

        public bool IsReady => true;

        public void CaptureBaseline()
        {
            _captured = true;
        }

        public void RestoreBaseline()
        {
            if (!_captured) return;
            global::HkStageHooks.ClearPresentationOverrides();
        }

        public void SetCompanionBackdropBlack(bool black)
        {
            global::HkStageHooks.SetBackdropOverride(black);
        }

        public void SetLifebloodFlash(HollowKnightFlashMode mode)
        {
            global::HkStageHooks.SetFlashOverride(mode);
        }
    }
}
#endif
