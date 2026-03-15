using System;
using System.Threading.Tasks;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.messages
{
    public interface IMessageService
    {
        Task<MessageDto> SendMessageAsync(Guid senderId, SendMessageDto sendMessageDto);
        Task<MessagesResponseDto> GetMessagesAsync(Guid userId, Guid conversationId, int limit = 50, string? cursor = null);
        Task<int> GetUnreadCountAsync(Guid userId, Guid conversationId);
        Task MarkAsReadAsync(Guid userId, Guid conversationId, string messageId);
    }
}
