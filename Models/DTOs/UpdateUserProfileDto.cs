namespace MessengerServer.Models.DTOs
{
    public class UpdateUserProfileDto
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? AboutMe { get; set; }
    }
}
