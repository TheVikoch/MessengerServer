using System;
using System.Threading.Tasks;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.messages
{
    public interface IMessageService
    {
        Task<MessageDto> SendMessageAsync(Guid senderId, SendMessageDto sendMessageDto);
        Task<MessageDto> SendSystemMessageAsync(Guid senderId, Guid conversationId, string kind, string content, string? metadataJson);
        Task<MessageDto> UpdateMessageAsync(Guid userId, Guid conversationId, string messageId, UpdateMessageDto updateMessageDto);
        Task DeleteMessageForUserAsync(Guid userId, Guid conversationId, string messageId);
        Task DeleteMessageForEveryoneAsync(Guid userId, Guid conversationId, string messageId);
        Task<MessagesResponseDto> GetMessagesAsync(Guid userId, Guid conversationId, int limit = 50, string? cursor = null);
        Task<ConversationAttachmentsResponseDto> GetConversationAttachmentsAsync(Guid userId, Guid conversationId, int limit = 100, string? cursor = null);
        Task<int> GetUnreadCountAsync(Guid userId, Guid conversationId);
        Task MarkAsReadAsync(Guid userId, Guid conversationId, string messageId);
    }
}
