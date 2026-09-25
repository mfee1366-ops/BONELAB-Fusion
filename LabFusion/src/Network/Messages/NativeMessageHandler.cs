using System.Reflection;

using LabFusion.Exceptions;
using LabFusion.Network.Serialization;
using LabFusion.Player;
using LabFusion.Utilities;

namespace LabFusion.Network;

public abstract class NativeMessageHandler : MessageHandler
{
    public abstract byte Tag { get; }

    // Handlers are created up front, they're not static
    public static void RegisterHandlersFromAssembly(Assembly targetAssembly)
    {
        if (targetAssembly == null) throw new NullReferenceException("Can't register from a null assembly!");

#if DEBUG
        FusionLogger.Log($"Populating MessageHandler list from {targetAssembly.GetName().Name}!");
#endif

        AssemblyUtilities.LoadAllValid<NativeMessageHandler>(targetAssembly, RegisterHandler);
    }

    public static void RegisterHandler<T>() where T : NativeMessageHandler => RegisterHandler(typeof(T));

    protected static void RegisterHandler(Type type)
    {
        NativeMessageHandler handler = Activator.CreateInstance(type) as NativeMessageHandler;

        handler.NetAttributes = type.GetCustomAttributes<Net.NetAttribute>().ToArray();

        byte index = handler.Tag;

        if (Handlers[index] != null) 
        { 
            throw new Exception($"{type.Name} has the same index as {Handlers[index].GetType().Name}, we can't replace handlers!"); 
        }

#if DEBUG
        FusionLogger.Log($"Registered {type.Name}");
#endif

        Handlers[index] = handler;
    }

    public static unsafe void ReadMessage(ReadableMessage message)
    {
        bool isServerHandled = message.IsServerHandled;

        int size = message.Buffer.Length;
        NetworkInfo.BytesDown += size;

        byte tag = 0;

        try
        {
            var buffer = message.Buffer;
            int position = 0;
            tag = buffer[position++];
            var route = new MessageRoute
            {
                Type = (RelayType)buffer[position++],
                Channel = (NetworkChannel)buffer[position++],
                Targets = ArraySegment<byte>.Empty,
            };

            if (route.Type == RelayType.ToTarget)
                route.Target = buffer[position++] == 1 ? buffer[position++] : null;
            else if (route.Type == RelayType.ToTargets)
            {
                int targetCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(position, sizeof(int)));
                position += sizeof(int);
                route.Targets = new ArraySegment<byte>(buffer.Slice(position, targetCount).ToArray());
                position += targetCount;
            }

            byte? sender = null;
            if (route.Type != RelayType.None)
                sender = buffer[position++] == 1 ? buffer[position++] : null;
            ulong? platformID = message.PlatformID;

            // Prevent ID spoofing
            if (isServerHandled && !ValidateReceivedID(route.Type, ref sender, ref platformID))
            {
                NetworkConnectionManager.DisconnectUser(platformID.Value);
                return;
            }

            int payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(buffer.Slice(position, sizeof(int)));
            position += sizeof(int);
            if (payloadLength < 0 || payloadLength > buffer.Length - position)
                throw new InvalidDataException("Network payload length exceeds the received packet.");
            var bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(System.Math.Max(payloadLength, 1));
            buffer.Slice(position, payloadLength).CopyTo(bytes);
            NetworkMetrics.RecordReceive(tag, size);

            if (Handlers[tag] != null)
            {
                var payload = new ReceivedMessage()
                {
                    Route = route,
                    Sender = sender,
                    PlatformID = platformID,
                    Bytes = bytes,
                    PayloadLength = payloadLength,
                    IsPooled = true,
                    IsServerHandled = message.IsServerHandled,
                };

                long handlerStart = System.Diagnostics.Stopwatch.GetTimestamp();
                Handlers[tag].StartHandlingMessage(payload);
                NetworkMetrics.RecordHandler(tag, System.Diagnostics.Stopwatch.GetTimestamp() - handlerStart);
            }
            else
            {
#if DEBUG
                FusionLogger.Warn($"Received message with invalid tag {tag}!");
#endif
                System.Buffers.ArrayPool<byte>.Shared.Return(bytes);
            }
        }
        catch (Exception e)
        {
            FusionLogger.Error($"Failed handling network message of tag {tag} with reason: {e.Message}\nTrace:{e.StackTrace}");
        }
    }

    private static bool ValidateReceivedID(RelayType relayType, ref byte? sender, ref ulong? platformID)
    {
        // If we weren't given a PlatformID, there is nothing to validate
        if (!platformID.HasValue)
        {
            return true;
        }

        var playerID = PlayerIDManager.GetPlayerID(platformID.Value);
        
        // No existing PlayerID, nothing to validate
        if (playerID == null)
        {
            // If this isn't a relay message, then we can allow the message to go through as its normal for the PlayerID to not be established
            // However, relay messages should NOT go through! Otherwise, the user can impact the lobby without visibly being in it.
            if (relayType != RelayType.None)
            {
                return false;
            }

            // Sender has a PlayerID but the PlatformID doesn't! User is spoofing!
            if (sender.HasValue && PlayerIDManager.HasPlayerID(sender.Value))
            {
                return false;
            }

            sender = null;
            return true;
        }

        byte existingSmallID = playerID.SmallID;

        // Sender doesn't have a value, just assign it the existing value
        if (!sender.HasValue)
        {
            sender = existingSmallID;
            return true;
        }

        // Received SmallID does not match the actual SmallID! User is spoofing!
        if (existingSmallID != sender.Value)
        {
            return false;
        }

        return true;
    }

    public sealed override void Handle(ReceivedMessage received)
    {
        CheckExpectedConditions(received);

        if (received.IsServerHandled && !OnPreRelayMessage(received))
        {
            return;
        }

        var route = received.Route;
        var type = route.Type;
        var channel = route.Channel;

        switch (type)
        {
            case RelayType.ToServer:
                if (!received.IsServerHandled)
                {
                    throw new MessageExpectedServerException();
                }
                break;
            case RelayType.ToClients:
                if (received.IsServerHandled)
                {
                    using var message = NetMessage.Create(Tag, received);

                    MessageSender.BroadcastMessage(channel, message);

                    return;
                }
                break;
            case RelayType.ToOtherClients:
                if (received.IsServerHandled)
                {
                    using var message = NetMessage.Create(Tag, received);

                    MessageSender.BroadcastMessageExcept(received.Sender.Value, channel, message, false);

                    return;
                }
                break;
            case RelayType.ToTarget:
                if (received.IsServerHandled)
                {
                    using var message = NetMessage.Create(Tag, received);

                    MessageSender.SendFromServer(route.Target.Value, channel, message);

                    return;
                }
                break;
            case RelayType.ToTargets:
                if (received.IsServerHandled)
                {
                    using var message = NetMessage.Create(Tag, received);

                    foreach (var target in route.Targets)
                    {
                        MessageSender.SendFromServer(target, channel, message);
                    }

                    return;
                }
                break;
        }

        OnHandleMessage(received);
    }

    public static readonly NativeMessageHandler[] Handlers = new NativeMessageHandler[byte.MaxValue];
}
