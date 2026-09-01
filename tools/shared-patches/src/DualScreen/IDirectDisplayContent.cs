namespace DualSouls.DualScreen
{
    public interface IDirectDisplayContent : System.IDisposable
    {
        void SetTransportActive(bool active);
        void OnPanelGeometry(float width, float height);
    }
}
