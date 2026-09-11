using System;

namespace DualSouls.Skins.Silksong.Runtime
{
    public static class SilksongProcessStartup
    {
        public static bool Run(Action ensureProcessOwner, Func<bool> shouldStartDualScreen,
            Action startDualScreen)
        {
            if (ensureProcessOwner == null) throw new ArgumentNullException(nameof(ensureProcessOwner));
            if (shouldStartDualScreen == null) throw new ArgumentNullException(nameof(shouldStartDualScreen));
            if (startDualScreen == null) throw new ArgumentNullException(nameof(startDualScreen));
            ensureProcessOwner();
            if (!shouldStartDualScreen()) return false;
            startDualScreen();
            return true;
        }
    }
}
