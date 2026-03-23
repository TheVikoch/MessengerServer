namespace MessengerServer.Models
{
    public class MediaEncryptionMetadata
    {
        public string? Algorithm { get; set; }
        public string? KeyId { get; set; }
        public string? IvBase64 { get; set; }
        public int? Version { get; set; }
    }
}
