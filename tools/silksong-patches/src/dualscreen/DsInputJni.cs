// Silksong polls its activity's JNI touch batch for the second screen.

#if UNITY_ANDROID && !UNITY_EDITOR
public partial class DsInput
{
    public void Poll()
    {
        DsTouch.CollectSecondScreen(_events);
        Poll(_events, DsTouch.Generation);
    }
}
#endif
