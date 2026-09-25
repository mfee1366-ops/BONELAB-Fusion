using LabFusion.Network.Serialization;
using LabFusion.SDK.Modules;

using System.Buffers.Binary;
using System.Buffers;
using System.Runtime.InteropServices;

namespace LabFusion.Network;

public unsafe class NetMessage : IDisposable
{
    private byte[] _managedBuffer;
    private GCHandle _pin;
    private byte* _buffer;
    private int _size;

    private bool _disposed;

    public int Length
    {
        get
        {
            return _size;
        }
    }

    public byte Tag { get; private set; }
    public ushort? EntityID { get; private set; }

    public byte* Buffer
    {
        get
        {
            return _buffer;
        }
    }

    private static NetMessage Create(int size, byte tag)
    {
        var rented = ArrayPool<byte>.Shared.Rent(size);
        var pin = GCHandle.Alloc(rented, GCHandleType.Pinned);
        return new NetMessage()
        {
            _managedBuffer = rented,
            _pin = pin,
            _buffer = (byte*)pin.AddrOfPinnedObject(),
            _size = size,
            Tag = tag,
            _disposed = false,
        };
    }

    private static NetMessage CreateOwned(byte[] rented, int size, byte tag)
    {
        var pin = GCHandle.Alloc(rented, GCHandleType.Pinned);
        return new NetMessage { _managedBuffer = rented, _pin = pin, _buffer = (byte*)pin.AddrOfPinnedObject(), _size = size, Tag = tag };
    }

    public static NetMessage Create(byte tag, NetWriter writer, MessageRoute route, byte? sender = null)
    {
        return Create(tag, writer.Buffer, route, sender);
    }

    public static NetMessage Create(byte tag, ArraySegment<byte> buffer, MessageRoute route, byte? sender = null)
    {
        var prefix = new MessagePrefix()
        {
            Tag = tag,
            Route = route,
            Sender = sender,
        };

        using var writer = NetWriter.Create(prefix.GetSize().Value + buffer.Count + sizeof(int));

        writer.SerializeValue(ref prefix);
        writer.Write(buffer);

        var owned = writer.DetachBuffer(out int size);
        var message = CreateOwned(owned, size, tag);
        if (tag == NativeMessageTag.EntityPoseUpdate && buffer.Count >= sizeof(ushort))
            message.EntityID = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(0, sizeof(ushort)));
        return message;
    }

    public static NetMessage Create(byte tag, ReceivedMessage received)
    {
        var prefix = new MessagePrefix()
        {
            Tag = tag,
            Route = received.Route,
            Sender = received.Sender,
        };

        int payloadLength = received.Length;
        using var writer = NetWriter.Create(prefix.GetSize().Value + payloadLength + sizeof(int));

        writer.SerializeValue(ref prefix);
        writer.Write(new ArraySegment<byte>(received.Bytes, 0, payloadLength));

        var owned = writer.DetachBuffer(out int size);
        var message = CreateOwned(owned, size, tag);
        if (tag == NativeMessageTag.EntityPoseUpdate && payloadLength >= sizeof(ushort))
            message.EntityID = BinaryPrimitives.ReadUInt16BigEndian(received.Bytes.AsSpan(0, sizeof(ushort)));
        return message;
    }

    public static NetMessage ModuleCreate<TMessage>(NetWriter writer, MessageRoute route, byte? sender = null) where TMessage : ModuleMessageHandler
    {
        return ModuleCreate(typeof(TMessage), writer, route, sender);
    }

    public static NetMessage ModuleCreate<TMessage>(byte[] buffer, MessageRoute route, byte? sender = null) where TMessage : ModuleMessageHandler
    {
        return ModuleCreate(typeof(TMessage), buffer, route, sender);
    }

    public static NetMessage ModuleCreate(Type type, NetWriter writer, MessageRoute route, byte? sender = null)
    {
        return ModuleCreate(type, writer.Buffer, route, sender);
    }

    public static NetMessage ModuleCreate(Type type, ArraySegment<byte> buffer, MessageRoute route, byte? sender = null)
    {
        // Assign the module type
        var tag = ModuleMessageManager.GetHandlerTagByType(type);

        if (!tag.HasValue)
        {
            return null;
        }

        var value = tag.Value;

        var prefix = new MessagePrefix()
        {
            Tag = NativeMessageTag.Module,
            Route = route,
            Sender = sender,
        };

        using var writer = NetWriter.Create(prefix.GetSize().Value + buffer.Count + sizeof(long) + sizeof(int));

        writer.SerializeValue(ref prefix);

        writer.Write(buffer.Count + sizeof(long));
        writer.Write(value);
        writer.WriteRaw(buffer);

        var owned = writer.DetachBuffer(out int size);
        var message = CreateOwned(owned, size, NativeMessageTag.Module);

        return message;
    }

    public byte[] ToByteArray()
    {
        var bytes = new byte[Length];

        Marshal.Copy((IntPtr)_buffer, bytes, 0, Length);

        return bytes;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        GC.SuppressFinalize(this);
        _buffer = null;
        if (_pin.IsAllocated)
            _pin.Free();
        if (_managedBuffer != null)
            ArrayPool<byte>.Shared.Return(_managedBuffer);
        _managedBuffer = null;

        _disposed = true;
    }
}
