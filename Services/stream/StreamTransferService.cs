using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

            session.Cancellation.Cancel();
            session.Channel.Writer.TryComplete();

            _sessions.TryRemove(session.TransferId, out _);
            _activeByChat.TryRemove(session.StreamChatId, out _);
        }
    }
}

