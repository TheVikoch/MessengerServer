namespace MessengerServer.Services.stream
{
    public class StreamTransferOptions
    {
        public int ChunkSizeBytes { get; set; } = 2 * 1024 * 1024;
        public int WindowSize { get; set; } = 64;
        public long MaxFileSizeBytes { get; set; } = 100L * 1024 * 1024 * 1024;
        public int SessionTtlMinutes { get; set; } = 720;
        public int CleanupIntervalSeconds { get; set; } = 300;
    }
}
