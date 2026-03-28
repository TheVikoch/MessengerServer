namespace MessengerServer.Models.DTOs
{
    public class InitUserProfilePhotoUploadRequestDto
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}
