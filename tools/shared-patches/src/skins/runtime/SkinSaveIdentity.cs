using System;

namespace DualSouls.Skins.Runtime
{
    // Both exact supported depots expose GameManager.profileID and Platform slots 0..4.
    // A default-initialized profileID is NOT admission: bind only after the existing typed
    // PLAYING + positioned hero lifecycle. Pause, death and scene loads retain the slot.
    public sealed class SkinSaveIdentity
    {
        public const int Unbound = -1;
        public int Slot { get; private set; } = Unbound;
        object manager;
        public int Sample(object currentManager, int profileId, bool positionedPlayingHero, bool menu)
        {
            if (menu || currentManager == null || profileId < 0 || profileId > 4)
                Slot = Unbound;
            else if (!ReferenceEquals(manager, currentManager) || (Slot != Unbound && profileId != Slot))
                Slot = positionedPlayingHero ? profileId : Unbound;
            else if (Slot == Unbound && positionedPlayingHero)
                Slot = profileId;
            manager = currentManager;
            return Slot;
        }
    }
}
