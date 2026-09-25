using LabFusion.Entities;
using LabFusion.Network.Serialization;

namespace LabFusion.Network;

public class EntityPoseUpdateData : INetSerializable
{
    public NetworkEntityReference Entity;
    public EntityPose Pose;

    public int? GetSize() => NetworkEntityReference.Size + Pose.GetSize();

    public void Serialize(INetSerializer serializer)
    {
        serializer.SerializeValue(ref Entity);
        serializer.SerializeValue(ref Pose);
    }

    public NetworkEntity GetEntity() => Entity.GetEntity();
}

[Net.SkipHandleWhileLoading]
public class EntityPoseUpdateMessage : NativeMessageHandler
{
    public override byte Tag => NativeMessageTag.EntityPoseUpdate;

    protected override void OnHandleMessage(ReceivedMessage received)
    {
        var data = received.ReadData<EntityPoseUpdateData>();

        // Clients can skip distant props, but the host is authoritative and must keep every pose current
        if (!NetworkInfo.IsHost && data.Pose?.Bodies != null && data.Pose.Bodies.Length > 0 &&
            !NetworkRelevance.ShouldProcessIncoming(data.Pose.Bodies[0].Position,
                NetworkOptimizationState.ClientPropRange))
            return;

        // Find the network entity
        var entity = data.GetEntity();

        // Validate the entity
        if (entity == null || !entity.IsRegistered || entity.OwnerID == null || entity.OwnerID != received.Sender)
        {
            return;
        }

        // Get the network prop so we can update its pose
        var networkProp = entity.GetExtender<NetworkProp>();

        if (networkProp == null)
        {
            return;
        }

        networkProp.OnReceivePose(data.Pose);
    }
}
