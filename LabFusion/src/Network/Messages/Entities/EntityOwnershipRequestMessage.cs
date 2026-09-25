namespace LabFusion.Network;

public class EntityOwnershipRequestMessage : NativeMessageHandler
{
    public override byte Tag => NativeMessageTag.EntityOwnershipRequest;

    public override ExpectedReceiverType ExpectedReceiver => ExpectedReceiverType.ServerOnly;

    protected override void OnHandleMessage(ReceivedMessage received)
    {
        // Read request
        var data = received.ReadData<EntityPlayerData>();

        var entity = Entities.NetworkEntityManager.IDManager.RegisteredEntities.GetEntity(data.Entity.ID);
        if (data.PlayerID != received.Sender.Value || entity == null || !entity.IsRegistered ||
            entity.IsOwnerLocked || entity.GetExtender<Entities.NetworkProp>() == null ||
            !ServerTrafficPolicy.CanOwnAnotherProp(received.Sender.Value, entity))
            return;

        // Send response
        var response = new EntityPlayerData()
        {
            PlayerID = data.PlayerID,
            Entity = new(data.Entity.ID),
        };

        MessageRelay.RelayNative(response, NativeMessageTag.EntityOwnershipResponse, CommonMessageRoutes.ReliableToClients);
    }
}
