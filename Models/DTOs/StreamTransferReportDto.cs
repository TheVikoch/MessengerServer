using System;

namespace MessengerServer.Models.DTOs
{
    public class StreamTransferReportDto
    {
        public Guid StreamChatId { get; set; }
        public Guid SenderId { get; set; }
        public Guid ReceiverId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string Status { get; set; } = "completed"; // completed/failed/canceled
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
    }
}
