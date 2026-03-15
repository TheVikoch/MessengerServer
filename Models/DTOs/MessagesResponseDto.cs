using System;
using System.Collections.Generic;

namespace MessengerServer.Models.DTOs
{
    public class MessagesResponseDto
    {
        public List<MessageDto> Messages { get; set; } = new List<MessageDto>();
        public bool HasMore { get; set; } = false;
        public string? NextCursor { get; set; }
    }
}
