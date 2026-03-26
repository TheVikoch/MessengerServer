using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MessengerServer.Hubs;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.stream
{
    public class StreamTransferService : IStreamTransferService, IHostedService, IDisposable
    {
        private readonly ConcurrentDictionary<Guid, StreamTransferSession> _sessions = new();
        private readonly ConcurrentDictionary<Guid, Guid> _activeByChat = new();
        private readonly IHubContext<MessengerHub> _hubContext;
        private readonly StreamTransferOptions _options;
        private PeriodicTimer? _cleanupTimer;
        private CancellationTokenSource? _cleanupCts;

        public StreamTransferService(IHubContext<MessengerHub> hubContext, IOptions<StreamTransferOptions> options)
        {
            _hubContext = hubContext;
            _options = options.Value;
        }

        public StreamTransferSession CreateTransfer(StreamTransferSession session)
        {
            if (!_activeByChat.TryAdd(session.StreamChatId, session.TransferId))
            {
                throw new InvalidOperationException("Another transfer is already active in this chat");
            }

            if (!_sessions.TryAdd(session.TransferId, session))
            {
                _activeByChat.TryRemove(session.StreamChatId, out _);
                throw new InvalidOperationException("Transfer already exists");
            }

            session.RelayTask = Task.Run(() => RelayChunksAsync(session, session.Cancellation.Token));
            for (var lane = 0; lane < session.ReceiverLaneCount; lane++)
            {
                var receiverLane = lane;
                session.ReceiverRelayTasks[receiverLane] = Task.Run(
                    () => RelayReceiverLaneAsync(session, receiverLane, session.Cancellation.Token));
            }
            return session;
        }

        public bool TryGetSession(Guid transferId, out StreamTransferSession session)
        {
            return _sessions.TryGetValue(transferId, out session!);
        }

        public void AcceptTransfer(Guid transferId, Guid receiverId)
        {
            var session = GetSessionOrThrow(transferId);
            if (session.ReceiverId != receiverId)
            {
                throw new UnauthorizedAccessException("Only receiver can accept transfer");
            }

            if (session.State != StreamTransferState.AwaitingAcceptance)
            {
                throw new InvalidOperationException("Transfer is not awaiting acceptance");
            }

            session.State = StreamTransferState.Active;
            session.LastActivityAt = DateTime.UtcNow;
        }

        public void RejectTransfer(Guid transferId, Guid receiverId)
        {
            var session = GetSessionOrThrow(transferId);
            if (session.ReceiverId != receiverId)
            {
                throw new UnauthorizedAccessException("Only receiver can reject transfer");
            }

            if (session.State != StreamTransferState.AwaitingAcceptance)
            {
                throw new InvalidOperationException("Transfer is not awaiting acceptance");
            }

            CloseSession(session, StreamTransferState.Canceled);
        }

        public async Task EnqueueChunkAsync(Guid transferId, Guid senderId, StreamTransferChunkEnvelope chunk, CancellationToken cancellationToken)
        {
            var session = GetSessionOrThrow(transferId);
            if (session.SenderId != senderId)
            {
                throw new UnauthorizedAccessException("Only sender can send chunks");
            }

            if (session.State != StreamTransferState.Active)
            {
                throw new InvalidOperationException("Transfer is not active");
            }

            session.LastActivityAt = DateTime.UtcNow;
            await session.Channel.Writer.WriteAsync(chunk, cancellationToken);
        }

        public async Task AttachSocketAsync(
            Guid transferId,
            Guid userId,
            StreamTransferSocketRole role,
            int lane,
            WebSocket socket,
            CancellationToken cancellationToken)
        {
            var session = GetSessionOrThrow(transferId);
            ValidateLane(session, role, lane);
            ValidateSocketParticipant(session, userId, role);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token, cancellationToken);
            var linkedToken = linkedCts.Token;

            WebSocket? previousSocket = null;
            lock (session.SocketSync)
            {
                if (role == StreamTransferSocketRole.Sender)
                {
                    previousSocket = session.SenderSockets[lane];
                    session.SenderSockets[lane] = socket;
                }
                else
                {
                    previousSocket = session.ReceiverSockets[lane];
                    session.ReceiverSockets[lane] = socket;
                }
            }

            if (previousSocket != null && !ReferenceEquals(previousSocket, socket))
            {
                await CloseSocketQuietlyAsync(previousSocket, WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None);
            }

            session.LastActivityAt = DateTime.UtcNow;

            try
            {
                if (role == StreamTransferSocketRole.Sender)
                {
                    await RelaySenderSocketAsync(session, lane, socket, linkedToken);
                }
                else
                {
                    await ObserveReceiverSocketAsync(session, lane, socket, linkedToken);
                }
            }
            finally
            {
                lock (session.SocketSync)
                {
                    if (role == StreamTransferSocketRole.Sender)
                    {
                        if (ReferenceEquals(session.SenderSockets[lane], socket))
                        {
                            session.SenderSockets[lane] = null;
                        }
                    }
                    else if (ReferenceEquals(session.ReceiverSockets[lane], socket))
                    {
                        session.ReceiverSockets[lane] = null;
                    }
                }

                await CloseSocketQuietlyAsync(socket, WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
        }
        public void CompleteTransfer(Guid transferId, Guid receiverId)
        {
            var session = GetSessionOrThrow(transferId);
            if (session.ReceiverId != receiverId)
            {
                throw new UnauthorizedAccessException("Only receiver can complete transfer");
            }

            if (session.State != StreamTransferState.Active)
            {
                throw new InvalidOperationException("Transfer is not active");
            }

            CloseSession(session, StreamTransferState.Completed);
        }

        public void CancelTransfer(Guid transferId, Guid userId)
        {
            var session = GetSessionOrThrow(transferId);
            if (session.SenderId != userId && session.ReceiverId != userId)
            {
                throw new UnauthorizedAccessException("Only participants can cancel transfer");
            }

            if (session.State == StreamTransferState.Completed)
            {
                throw new InvalidOperationException("Transfer already completed");
            }

            CloseSession(session, StreamTransferState.Canceled);
        }

        public void Touch(Guid transferId)
        {
            if (_sessions.TryGetValue(transferId, out var session))
            {
                session.LastActivityAt = DateTime.UtcNow;
            }
        }

        public bool IsTransferActive(Guid streamChatId)
        {
            return _activeByChat.ContainsKey(streamChatId);
        }

        public DateTime GetExpiryTime(DateTime lastActivityUtc)
        {
            return lastActivityUtc.AddMinutes(_options.SessionTtlMinutes);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _cleanupTimer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CleanupIntervalSeconds));
            _ = Task.Run(() => CleanupLoopAsync(_cleanupCts.Token));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cleanupCts?.Cancel();
            _cleanupTimer?.Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cleanupCts?.Dispose();
        }

        private StreamTransferSession GetSessionOrThrow(Guid transferId)
        {
            if (!_sessions.TryGetValue(transferId, out var session))
            {
                throw new KeyNotFoundException("Transfer not found or expired");
            }

            return session;
        }

        private void ValidateLane(StreamTransferSession session, StreamTransferSocketRole role, int lane)
        {
            var laneCount = role == StreamTransferSocketRole.Sender
                ? session.SenderLaneCount
                : session.ReceiverLaneCount;
            if (lane < 0 || lane >= laneCount)
            {
                throw new InvalidOperationException("Invalid transfer lane");
            }
        }

        private void ValidateSocketParticipant(StreamTransferSession session, Guid userId, StreamTransferSocketRole role)
        {
            if (session.State == StreamTransferState.Canceled ||
                session.State == StreamTransferState.Completed ||
                session.State == StreamTransferState.Failed)
            {
                throw new InvalidOperationException("Transfer is already closed");
            }

            if (role == StreamTransferSocketRole.Sender)
            {
                if (session.SenderId != userId)
                {
                    throw new UnauthorizedAccessException("Only sender can attach sender socket");
                }

                if (session.State != StreamTransferState.Active)
                {
                    throw new InvalidOperationException("Sender socket requires active transfer");
                }
            }
            else
            {
                if (session.ReceiverId != userId)
                {
                    throw new UnauthorizedAccessException("Only receiver can attach receiver socket");
                }
            }
        }

        private async Task RelaySenderSocketAsync(StreamTransferSession session, int lane, WebSocket senderSocket, CancellationToken cancellationToken)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(GetSocketRelayReadBufferSize(session));
            using var messageBuffer = new MemoryStream(Math.Max(256 * 1024, session.ChunkSize + 64));
            try
            {
                while (!cancellationToken.IsCancellationRequested && senderSocket.State == WebSocketState.Open)
                {
                    messageBuffer.Position = 0;
                    messageBuffer.SetLength(0);

                    while (true)
                    {
                        var result = await senderSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }

                        if (result.MessageType != WebSocketMessageType.Binary)
                        {
                            throw new InvalidOperationException("Only binary websocket frames are allowed");
                        }

                        if (result.Count > 0)
                        {
                            messageBuffer.Write(buffer, 0, result.Count);
                        }

                        if (!result.EndOfMessage)
                        {
                            continue;
                        }

                        break;
                    }

                    if (!messageBuffer.TryGetBuffer(out var payload))
                    {
                        payload = new ArraySegment<byte>(messageBuffer.ToArray());
                    }

                    if (payload.Array == null)
                    {
                        throw new InvalidOperationException("Failed to prepare websocket payload");
                    }

                    session.LastActivityAt = DateTime.UtcNow;
                    var receiverLane = GetReceiverLaneForBinaryFrame(session, payload);
                    var payloadCopy = CopyPayload(payload);
                    await session.ReceiverOutboundChannels[receiverLane].Writer.WriteAsync(payloadCopy, cancellationToken);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task RelayReceiverLaneAsync(StreamTransferSession session, int lane, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var payload in session.ReceiverOutboundChannels[lane].Reader.ReadAllAsync(cancellationToken))
                {
                    var receiverSocket = await WaitForReceiverSocketAsync(session, lane, cancellationToken);
                    if (receiverSocket == null)
                    {
                        throw new InvalidOperationException("Receiver socket unavailable");
                    }

                    var sendLock = session.ReceiverSendLocks[lane];
                    await sendLock.WaitAsync(cancellationToken);
                    try
                    {
                        if (receiverSocket.State != WebSocketState.Open)
                        {
                            throw new WebSocketException("Receiver socket is not open");
                        }

                        await receiverSocket.SendAsync(
                            new ArraySegment<byte>(payload),
                            WebSocketMessageType.Binary,
                            endOfMessage: true,
                            cancellationToken);
                    }
                    finally
                    {
                        sendLock.Release();
                    }

                    session.LastActivityAt = DateTime.UtcNow;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation during shutdown.
            }
            catch
            {
                CloseSession(session, StreamTransferState.Failed);
            }
        }

        private static int GetReceiverLaneForBinaryFrame(StreamTransferSession session, ArraySegment<byte> payload)
        {
            if (payload.Array == null || payload.Count < 5)
            {
                throw new InvalidOperationException("Binary payload is too small");
            }

            var baseOffset = payload.Offset;
            var seq = ((payload.Array[baseOffset] & 0xFF) << 24) |
                ((payload.Array[baseOffset + 1] & 0xFF) << 16) |
                ((payload.Array[baseOffset + 2] & 0xFF) << 8) |
                (payload.Array[baseOffset + 3] & 0xFF);
            return Math.Abs(seq % Math.Max(1, session.ReceiverLaneCount));
        }

        private static byte[] CopyPayload(ArraySegment<byte> payload)
        {
            if (payload.Array == null)
            {
                throw new InvalidOperationException("Binary payload is missing");
            }

            var copy = new byte[payload.Count];
            Buffer.BlockCopy(payload.Array, payload.Offset, copy, 0, payload.Count);
            return copy;
        }

        private static int GetSocketRelayReadBufferSize(StreamTransferSession session)
        {
            return Math.Max(256 * 1024, Math.Min(session.ChunkSize + 8 * 1024, 4 * 1024 * 1024));
        }

        private async Task ObserveReceiverSocketAsync(StreamTransferSession session, int lane, WebSocket receiverSocket, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            while (!cancellationToken.IsCancellationRequested && receiverSocket.State == WebSocketState.Open)
            {
                var result = await receiverSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                while (!result.EndOfMessage)
                {
                    result = await receiverSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                }

                session.LastActivityAt = DateTime.UtcNow;
            }
        }

        private async Task<WebSocket?> WaitForReceiverSocketAsync(StreamTransferSession session, int lane, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                WebSocket? receiverSocket;
                lock (session.SocketSync)
                {
                    receiverSocket = session.ReceiverSockets[lane];
                }

                if (receiverSocket != null && receiverSocket.State == WebSocketState.Open)
                {
                    return receiverSocket;
                }

                if (session.State != StreamTransferState.Active &&
                    session.State != StreamTransferState.AwaitingAcceptance)
                {
                    return null;
                }

                await Task.Delay(25, cancellationToken);
            }

            return null;
        }

        private async Task RelayChunksAsync(StreamTransferSession session, CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var chunk in session.Channel.Reader.ReadAllAsync(cancellationToken))
                {
                    var payload = new StreamTransferChunkDto
                    {
                        TransferId = chunk.TransferId,
                        Seq = chunk.Seq,
                        Data = chunk.Data,
                        ChunkHash = chunk.ChunkHash,
                        IsLast = chunk.IsLast
                    };

                    await _hubContext.Clients.User(session.ReceiverId.ToString())
                        .SendAsync("ReceiveEvent", "stream_transfer_chunk", payload, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch
            {
                CloseSession(session, StreamTransferState.Failed);
            }
        }

        private async Task CleanupLoopAsync(CancellationToken cancellationToken)
        {
            if (_cleanupTimer == null)
            {
                return;
            }

            while (await _cleanupTimer.WaitForNextTickAsync(cancellationToken))
            {
                var now = DateTime.UtcNow;
                foreach (var kvp in _sessions)
                {
                    var session = kvp.Value;
                    if (now > GetExpiryTime(session.LastActivityAt))
                    {
                        CloseSession(session, StreamTransferState.Canceled);
                    }
                }
            }
        }

        private void CloseSession(StreamTransferSession session, StreamTransferState finalState)
        {
            session.State = finalState;
            session.LastActivityAt = DateTime.UtcNow;

            var senderSockets = new List<WebSocket?>();
            var receiverSockets = new List<WebSocket?>();
            lock (session.SocketSync)
            {
                for (var lane = 0; lane < session.SenderLaneCount; lane++)
                {
                    senderSockets.Add(session.SenderSockets[lane]);
                    session.SenderSockets[lane] = null;
                }

                for (var lane = 0; lane < session.ReceiverLaneCount; lane++)
                {
                    receiverSockets.Add(session.ReceiverSockets[lane]);
                    session.ReceiverSockets[lane] = null;
                }
            }

            session.Cancellation.Cancel();
            session.Channel.Writer.TryComplete();
            foreach (var outboundChannel in session.ReceiverOutboundChannels)
            {
                outboundChannel.Writer.TryComplete();
            }

            _sessions.TryRemove(session.TransferId, out _);
            _activeByChat.TryRemove(session.StreamChatId, out _);

            var closeStatus = finalState == StreamTransferState.Completed
                ? WebSocketCloseStatus.NormalClosure
                : WebSocketCloseStatus.PolicyViolation;
            var closeDescription = finalState.ToString().ToLowerInvariant();

            foreach (var senderSocket in senderSockets)
            {
                _ = CloseSocketQuietlyAsync(senderSocket, closeStatus, closeDescription, CancellationToken.None);
            }
            foreach (var receiverSocket in receiverSockets)
            {
                _ = CloseSocketQuietlyAsync(receiverSocket, closeStatus, closeDescription, CancellationToken.None);
            }
        }

        private static async Task CloseSocketQuietlyAsync(
            WebSocket? socket,
            WebSocketCloseStatus closeStatus,
            string description,
            CancellationToken cancellationToken)
        {
            if (socket == null)
            {
                return;
            }

            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(closeStatus, description, cancellationToken);
                }
            }
            catch
            {
                // Ignore close failures during cleanup.
            }
            finally
            {
                socket.Dispose();
            }
        }
    }
}






