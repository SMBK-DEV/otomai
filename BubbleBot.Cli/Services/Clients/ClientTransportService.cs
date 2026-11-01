using System.Diagnostics;
using Bubble.Core.Extensions;
using Bubble.Shared.Protocol;
using ProtoBuf;

namespace BubbleBot.Cli.Services.Clients;

internal sealed class ClientTransportService
{
    private readonly BotClientContextBase _context;
    private IClientMessageRouter? _router;
    private readonly Contracts.IClientLogger _logger;
    private readonly Func<byte[], bool> _sendAsync;
    private readonly PacketCaptureStore _packetCapture;

    public ClientTransportService(BotClientContextBase           context,
                                  Contracts.IClientLogger        logger,
                                  Func<byte[], bool>             sendAsync,
                                  IClientMessageRouter?          router = null,
                                  PacketCaptureStore?             packetCapture = null)
    {
        _context = context;
        _logger = logger;
        _sendAsync = sendAsync;
        _router = router;
        _packetCapture = packetCapture ?? new PacketCaptureStore();
    }

    public void AttachRouter(IClientMessageRouter router)
    {
        _router = router;
    }

    public void HandleReceived(byte[] buffer, long offset, long size)
    {
        _context.Buffer.Write(buffer.AsMemory()[(int)offset..(int)(offset + size)]);

        while (_context.Buffer.Size > 0)
        {
            if (!_context.IsWaitingForAnotherPacket)
            {
                (_context.ExpectedLength, _context.ExpectedLengthSize) = ReadVarInt32();
            }

            if (_context.ExpectedLength == null || _context.ExpectedLength > _context.Buffer.Size)
            {
                _context.IsWaitingForAnotherPacket = true;
                return;
            }

            var messageData = new byte[_context.ExpectedLength.Value];
            _context.Buffer.Read(messageData, 0, _context.ExpectedLength.Value);

            _logger.LogInfo("RCV framed message: {Length} bytes", messageData.Length);

            GameMessage message;
            try
            {
                using var stream = new MemoryStream(messageData);
                message = Serializer.Deserialize<GameMessage>(stream);
            }
            catch (Exception ex)
            {
                _logger.LogInfo("GameMessage deserialization failed: {Error}", ex.Message);
                _context.IsWaitingForAnotherPacket = false;
                if (_context.Buffer.Seek(_context.ExpectedLength.Value + _context.ExpectedLengthSize))
                    continue;
                ResetState();
                continue;
            }

            _logger.LogInfo("RCV GameMessage ContentCase={Case}", message.ContentCase);

            var typeName = message.ContentCase switch
            {
                GameMessage.ContentOneofCase.Request => message.Request.Content.TypeUrl,
                GameMessage.ContentOneofCase.Response => message.Response.Content.TypeUrl,
                GameMessage.ContentOneofCase.Event => message.Event.Content.TypeUrl,
                _ => "Unknown"
            };

            var content = message.ContentCase switch
            {
                GameMessage.ContentOneofCase.Request => message.Request.Content.Value,
                GameMessage.ContentOneofCase.Response => message.Response.Content.Value,
                GameMessage.ContentOneofCase.Event => message.Event.Content.Value,
                _ => null
            };

            _packetCapture.TryCapture("incoming", typeName, content);
            var type = typeName.GetLastSegment('/');
            var data = MessageFactory.Create(type, content);
            var typeFullName = ProtoService.Instance.GetMapping(type);

            if (content == null && message.Response == null && message.Event == null && message.Request == null)
            {
                Dispatch(data as IProtoMessage, typeFullName);

                if (_context.Buffer.Seek(_context.ExpectedLength.Value + _context.ExpectedLengthSize))
                {
                    continue;
                }

                ResetState();
                continue;
            }

            if (message.Request != null)
            {
                _context.LastRequestUid = message.Request.Uid;
            }

            Dispatch(data as IProtoMessage, typeFullName);
            _context.IsWaitingForAnotherPacket = false;

            if (_context.Buffer.Seek(_context.ExpectedLength.Value + _context.ExpectedLengthSize))
            {
                continue;
            }

            if (_context.Buffer.Size == 0)
            {
                ResetState();
                return;
            }
        }
    }

    public async Task SendRequestWithDelay(IProtoMessage             message,
                                           string                    typeUrl,
                                           int                       delay,
                                           Predicate<IProtoMessage>? predicate = null)
    {
        await Task.Run(async () =>
        {
            await Task.Delay(delay);

            if (predicate != null && !predicate(message))
            {
                return;
            }

            SendRequest(message, typeUrl);
        });
    }

    public void SendRequest(IProtoMessage message, string typeUrl, bool setUid = false)
    {
        SendRequestCore(message, typeUrl, setUid ? _context.Uid++ : -1);
    }

    public void SendRequest(IProtoMessage message, string typeUrl, int uid)
    {
        SendRequestCore(message, typeUrl, uid);
    }

    public void ResetState()
    {
        _context.ExpectedLength = null;
        _context.ExpectedLengthSize = 0;
        _context.IsWaitingForAnotherPacket = false;
        _context.Buffer.Clear();
    }

    public (int, int) ReadVarInt32()
    {
        var nbBytes = 0;
        var result = 0;
        var shift = 0;
        byte currentByte;

        do
        {
            currentByte = _context.Buffer.Peek();
            _context.Buffer.Read(new byte[1], 0, 1);
            result |= (currentByte & 0x7F) << shift;
            shift += 7;
            nbBytes++;
        } while ((currentByte & 0x80) != 0);

        return (result, nbBytes);
    }

    public static (byte[], int) WriteVarInt32(int value)
    {
        var buffer = new byte[5];
        var index = 0;

        do
        {
            var currentByte = value & 0x7F;
            value >>= 7;

            if (value is not 0)
            {
                currentByte |= 0x80;
            }

            buffer[index++] = (byte)currentByte;
        } while (value is not 0);

        return (buffer, index);
    }

    private void Dispatch(IProtoMessage? message, string? typeFullName)
    {
        lock (_context.SyncRoot)
        {
            _router?.OnMessageReceived(message, typeFullName);
        }
    }

    private void SendRequestCore(IProtoMessage message, string typeUrl, int uid)
    {
        _context.LastMessageSent = Stopwatch.GetTimestamp();
        _logger.LogInfo("SND: {TypeUrl}", message.GetType().Name);

        using var bodyStream = new MemoryStream();
        Serializer.Serialize(bodyStream, message);
        var payload = bodyStream.ToArray();
        var request = new GameMessage
        {
            Request = new Request
            {
                Uid = uid,
                Content = new Any
                {
                    TypeUrl = $"type.ankama.com/{typeUrl}",
                    Value = payload
                }
            },
            ResponseFake = null
        };

        using var envelopeStream = new MemoryStream();
        Serializer.Serialize(envelopeStream, request);

        var messageContent = envelopeStream.ToArray();
        var header = WriteVarInt32(messageContent.Length);
        var finalContent = new byte[header.Item2 + messageContent.Length];
        var headerSpan = new Span<byte>(finalContent);
        header.Item1.CopyTo(headerSpan);
        messageContent.CopyTo(headerSpan[header.Item2..]);

        if (_sendAsync(finalContent))
            _packetCapture.TryCapture("outgoing", typeUrl, payload);
    }
}
