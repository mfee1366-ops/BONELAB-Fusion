using UnityEngine;

using LabFusion.Extensions;
using LabFusion.Network.Serialization;

namespace LabFusion.Data;

public class SerializedLocalTransform : INetSerializable
{
    public const int Size = SerializedShortVector3.Size + SerializedSmallQuaternion.Size;
    public static readonly SerializedLocalTransform Default = new(Vector3Extensions.zero, QuaternionExtensions.identity);

    public Vector3 position;
    public Quaternion rotation;

    private SerializedShortVector3 _compressedPosition;
    private SerializedSmallQuaternion _compressedRotation;

    public void Serialize(INetSerializer serializer)
    {
        if (serializer is NetWriter writer)
        {
            WriteShortVector(writer, position);
            WriteSmallQuaternion(writer, rotation);
            return;
        }

        if (serializer is NetReader reader)
        {
            position = ReadShortVector(reader);
            rotation = ReadSmallQuaternion(reader);
            return;
        }

        if (serializer.IsReader)
        {
            serializer.SerializeValue(ref _compressedPosition);
            serializer.SerializeValue(ref _compressedRotation);

            position = _compressedPosition.Expand();
            rotation = _compressedRotation.Expand();
        }
        else
        {
            _compressedPosition = SerializedShortVector3.Compress(position);
            _compressedRotation = SerializedSmallQuaternion.Compress(rotation);

            serializer.SerializeValue(ref _compressedPosition);
            serializer.SerializeValue(ref _compressedRotation);
        }
    }

    private static void WriteShortVector(NetWriter writer, Vector3 value)
    {
        float magnitude = value.magnitude;
        Vector3 normalized = magnitude > 0f ? value / magnitude : Vector3.zero;
        writer.Write((short)(normalized.x * 30000f));
        writer.Write((short)(normalized.y * 30000f));
        writer.Write((short)(normalized.z * 30000f));
        writer.Write(magnitude);
    }

    private static Vector3 ReadShortVector(NetReader reader)
    {
        var normalized = new Vector3(reader.ReadInt16() / 30000f, reader.ReadInt16() / 30000f, reader.ReadInt16() / 30000f).normalized;
        return normalized * reader.ReadSingle();
    }

    private static void WriteSmallQuaternion(NetWriter writer, Quaternion value)
    {
        writer.Write(value.x.ToSByte());
        writer.Write(value.y.ToSByte());
        writer.Write(value.z.ToSByte());
        writer.Write(value.w.ToSByte());
    }

    private static Quaternion ReadSmallQuaternion(NetReader reader) => new Quaternion(
        reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle(), reader.ReadSByte().ToSingle()).normalized;

    public SerializedLocalTransform() { }

    public SerializedLocalTransform(Transform transform)
        : this(transform.localPosition, transform.localRotation) { }

    public SerializedLocalTransform(Vector3 localPosition, Quaternion localRotation)
    {
        this.position = localPosition;
        this.rotation = localRotation;
    }
}
