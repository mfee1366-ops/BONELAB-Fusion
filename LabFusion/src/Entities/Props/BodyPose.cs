using LabFusion.Data;
using LabFusion.Network.Serialization;
using LabFusion.Extensions;

using UnityEngine;

namespace LabFusion.Entities;

public class BodyPose : INetSerializable
{
    public const int Size = SerializedShortVector3.Size + SerializedSmallQuaternion.Size + SerializedSmallVector3.Size * 2;

    public Vector3 Position = Vector3.zero;
    public Quaternion Rotation = Quaternion.identity;

    public Vector3 Velocity = Vector3.zero;
    public Vector3 AngularVelocity = Vector3.zero;

    private Vector3 _positionPrediction = Vector3.zero;

    public Vector3 PredictedPosition => Position + _positionPrediction;

    public int? GetSize() => Size;

    public void ReadFrom(Rigidbody rigidbody)
    {
        Position = rigidbody.position;
        Rotation = rigidbody.rotation;
        Velocity = rigidbody.velocity;
        AngularVelocity = rigidbody.angularVelocity;
    }

    public void CopyTo(BodyPose target)
    {
        target.Position = Position;
        target.Rotation = Rotation;
        target.Velocity = Velocity;
        target.AngularVelocity = AngularVelocity;

        target.ResetPrediction();
    }

    public void ResetPrediction()
    {
        _positionPrediction = Vector3.zero;
    }

    public void PredictPosition(float deltaTime)
    {
        _positionPrediction += Velocity * deltaTime;
    }

    public void Serialize(INetSerializer serializer)
    {
        if (serializer is NetWriter writer)
        {
            WriteVector(writer, Position, false);
            WriteQuaternion(writer, Rotation);
            WriteVector(writer, Velocity, true);
            WriteVector(writer, AngularVelocity, true);
            return;
        }

        if (serializer is NetReader reader)
        {
            Position = ReadVector(reader, false);
            Rotation = ReadQuaternion(reader);
            Velocity = ReadVector(reader, true);
            AngularVelocity = ReadVector(reader, true);
            return;
        }

        SerializedShortVector3 position = null;
        SerializedSmallQuaternion rotation = null;
        SerializedSmallVector3 velocity = null;
        SerializedSmallVector3 angularVelocity = null;

        if (!serializer.IsReader)
        {
            position = SerializedShortVector3.Compress(this.Position);
            rotation = SerializedSmallQuaternion.Compress(this.Rotation);
            velocity = SerializedSmallVector3.Compress(this.Velocity);
            angularVelocity = SerializedSmallVector3.Compress(this.AngularVelocity);
        }

        serializer.SerializeValue(ref position);
        serializer.SerializeValue(ref rotation);
        serializer.SerializeValue(ref velocity);
        serializer.SerializeValue(ref angularVelocity);

        if (serializer.IsReader)
        {
            this.Position = position.Expand();
            this.Rotation = rotation.Expand();
            this.Velocity = velocity.Expand();
            this.AngularVelocity = angularVelocity.Expand();
        }
    }

    private static void WriteVector(NetWriter writer, Vector3 value, bool small)
    {
        float magnitude = value.magnitude;
        Vector3 normalized = magnitude > 0f ? value / magnitude : Vector3.zero;
        if (small)
        {
            writer.Write(normalized.x.ToSByte()); writer.Write(normalized.y.ToSByte()); writer.Write(normalized.z.ToSByte());
        }
        else
        {
            writer.Write((short)(normalized.x * 30000f)); writer.Write((short)(normalized.y * 30000f)); writer.Write((short)(normalized.z * 30000f));
        }
        writer.Write(magnitude);
    }

    private static Vector3 ReadVector(NetReader reader, bool small)
    {
        Vector3 normalized = small
            ? new Vector3(reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle()).normalized
            : new Vector3(reader.ReadInt16() / 30000f, reader.ReadInt16() / 30000f, reader.ReadInt16() / 30000f).normalized;
        return normalized * reader.ReadSingle();
    }

    private static void WriteQuaternion(NetWriter writer, Quaternion value)
    {
        writer.Write(value.x.ToSByte()); writer.Write(value.y.ToSByte()); writer.Write(value.z.ToSByte()); writer.Write(value.w.ToSByte());
    }

    private static Quaternion ReadQuaternion(NetReader reader) => new Quaternion(
        reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle()).normalized;
}
