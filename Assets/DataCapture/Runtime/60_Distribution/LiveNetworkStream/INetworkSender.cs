namespace DataCapture.Networking
{
    public interface INetworkSender
    {
        bool IsReady { get; }
        bool TrySend(byte[] payload);
    }
}
