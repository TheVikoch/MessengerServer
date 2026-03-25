using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace MessengerServer.Services.stream
{
    public enum StreamTransferState
    {
        AwaitingAcceptance,
        Active,
        Completed,
        Canceled,
        Failed
    }

    public class StreamTransferChunkEnvelope
    {
        public Guid TransferId { get; set; }
        public int Seq { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string ChunkHash { get; set; } = string.Empty;
        public bool IsLast { get; set; }
    }

    public class StreamTransferSession
    {
        public Guid TransferId { get; }
        public Guid StreamChatId { get; }
        public Guid SenderId { get; }
        public Guid ReceiverId { get; }
        public string FileName { get; }
        public long FileSize { get; }
        public string FileHash { get; }
        public string FileHashAlgorithm { get; }
        public string ChunkHashAlgorithm { get; }
        public int ChunkSize { get; }
        public int TotalChunks { get; }
        public string? ContentType { get; }
        public string? Caption { get; }
        public StreamTransferState State { get; set; }
        public DateTime CreatedAt { get; }
        public DateTime LastActivityAt { get; set; }
        public Channel<StreamTransferChunkEnvelope> Channel { get; }
        public CancellationTokenSource Cancellation { get; }
        public Task? RelayTask { get; set; }

        public StreamTransferSession(
            Guid transferId,
            Guid streamChatId,
            Guid senderId,
            Guid receiverId,
            string fileName,
            long fileSize,
            string fileHash,
            string fileHashAlgorithm,
            string chunkHashAlgorithm,
            int chunkSize,
            int totalChunks,
            string? contentType,
            string? caption,
            int windowSize)
        {
            TransferId = transferId;
            StreamChatId = streamChatId;
            SenderId = senderId;
            ReceiverId = receiverId;
            FileName = fileName;
            FileSize = fileSize;
            FileHash = fileHash;
            FileHashAlgorithm = fileHashAlgorithm;
            ChunkHashAlgorithm = chunkHashAlgorithm;
            ChunkSize = chunkSize;
            TotalChunks = totalChunks;
            ContentType = contentType;
            Caption = caption;
            State = StreamTransferState.AwaitingAcceptance;
            CreatedAt = DateTime.UtcNow;
            LastActivityAt = CreatedAt;
            Cancellation = new CancellationTokenSource();
            Channel = System.Threading.Channels.Channel.CreateBounded<StreamTransferChunkEnvelope>(new BoundedChannelOptions(windowSize)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false
            });
        }
    }
}
