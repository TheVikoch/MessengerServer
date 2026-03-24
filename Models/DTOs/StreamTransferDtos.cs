using System;
using System.Collections.Generic;

namespace MessengerServer.Models.DTOs
{
    public class StreamTransferInitRequestDto
    {
        public Guid StreamChatId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string FileHashAlgorithm { get; set; } = "SHA-256";
        public string ChunkHashAlgorithm { get; set; } = "CRC32";
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public string? ContentType { get; set; }
        public string? Caption { get; set; }
    }

    public class StreamTransferStartResponseDto
    {
        public Guid TransferId { get; set; }
        public Guid StreamChatId { get; set; }
        public Guid ReceiverId { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class StreamTransferOfferDto
    {
        public Guid TransferId { get; set; }
        public Guid StreamChatId { get; set; }
        public Guid SenderId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string FileHashAlgorithm { get; set; } = "SHA-256";
        public string ChunkHashAlgorithm { get; set; } = "CRC32";
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public string? ContentType { get; set; }
        public string? Caption { get; set; }
    }

    public class StreamTransferAcceptRequestDto
    {
        public Guid TransferId { get; set; }
    }

    public class StreamTransferRejectRequestDto
    {
        public Guid TransferId { get; set; }
        public string? Reason { get; set; }
    }

    public class StreamTransferChunkDto
    {
        public Guid TransferId { get; set; }
        public int Seq { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string ChunkHash { get; set; } = string.Empty;
        public bool IsLast { get; set; }
    }

    public class StreamTransferAckDto
    {
        public Guid TransferId { get; set; }
        public List<int> Seqs { get; set; } = new();
    }

    public class StreamTransferNackDto
    {
        public Guid TransferId { get; set; }
        public List<int> Seqs { get; set; } = new();
    }

    public class StreamTransferResumeRequestDto
    {
        public Guid TransferId { get; set; }
        public List<int> MissingSeqs { get; set; } = new();
    }

    public class StreamTransferCompleteRequestDto
    {
        public Guid TransferId { get; set; }
    }

    public class StreamTransferCancelRequestDto
    {
        public Guid TransferId { get; set; }
        public string? Reason { get; set; }
    }
}
