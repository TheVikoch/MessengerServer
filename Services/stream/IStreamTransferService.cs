using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerServer.Services.stream
{
    public interface IStreamTransferService
    {
        StreamTransferSession CreateTransfer(StreamTransferSession session);
        bool TryGetSession(Guid transferId, out StreamTransferSession session);
        void AcceptTransfer(Guid transferId, Guid receiverId);
        void RejectTransfer(Guid transferId, Guid receiverId);
        Task EnqueueChunkAsync(Guid transferId, Guid senderId, StreamTransferChunkEnvelope chunk, CancellationToken cancellationToken);
        void CompleteTransfer(Guid transferId, Guid receiverId);
        void CancelTransfer(Guid transferId, Guid userId);
        void Touch(Guid transferId);
        bool IsTransferActive(Guid streamChatId);
        DateTime GetExpiryTime(DateTime lastActivityUtc);
    }
}
