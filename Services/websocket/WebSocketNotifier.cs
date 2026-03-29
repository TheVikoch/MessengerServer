using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using MessengerServer.Hubs;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.websocket
{
    public interface IWebSocketNotifier
    {
        Task NotifyConversationCreatedAsync(ConversationDto conversation, Guid triggeredByUserId);
        Task NotifyConversationDeletedAsync(Guid conversationId, IEnumerable<Guid> participantIds, Guid triggeredByUserId, bool deletedForEveryone);
        Task NotifyNewMessageAsync(Guid conversationId, MessageDto message, Guid senderId);
        Task NotifyMessageUpdatedAsync(Guid conversationId, MessageDto message, Guid senderId);
        Task NotifyMessageDeletedAsync(Guid conversationId, string messageId, Guid senderId, bool deletedForEveryone);
    }

    public class WebSocketNotifier : IWebSocketNotifier
    {
        private readonly IHubContext<MessengerHub> _hubContext;

        public WebSocketNotifier(IHubContext<MessengerHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task NotifyConversationCreatedAsync(ConversationDto conversation, Guid triggeredByUserId)
        {
            try
            {
                if (conversation.Id == Guid.Empty)
                {
                    return;
                }

                var participantIds = conversation.Members
                    .Select(member => member.UserId)
                    .Where(userId => userId != Guid.Empty)
                    .Distinct()
                    .Select(userId => userId.ToString())
                    .ToList();

                if (participantIds.Count == 0)
                {
                    return;
                }

                var createdEvent = new ConversationCreatedEventDto
                {
                    Conversation = conversation,
                    UserId = triggeredByUserId,
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Users(participantIds)
                    .SendAsync("ReceiveEvent", createdEvent.Type, createdEvent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending conversation created notification: {ex.Message}");
            }
        }

        public async Task NotifyConversationDeletedAsync(
            Guid conversationId,
            IEnumerable<Guid> participantIds,
            Guid triggeredByUserId,
            bool deletedForEveryone)
        {
            try
            {
                var recipients = participantIds
                    .Where(userId => userId != Guid.Empty)
                    .Distinct()
                    .Select(userId => userId.ToString())
                    .ToList();

                if (recipients.Count == 0)
                {
                    return;
                }

                var deletedEvent = new ConversationDeletedEventDto
                {
                    ConversationId = conversationId,
                    DeletedForEveryone = deletedForEveryone,
                    UserId = triggeredByUserId,
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Users(recipients)
                    .SendAsync("ReceiveEvent", deletedEvent.Type, deletedEvent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending conversation deleted notification: {ex.Message}");
            }
        }

        public async Task NotifyNewMessageAsync(Guid conversationId, MessageDto message, Guid senderId)
        {
            try
            {
                var newMessageEvent = new NewMessageEventDto
                {
                    ConversationId = conversationId,
                    Message = message,
                    UserId = senderId,
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Group(conversationId.ToString())
                    .SendAsync("ReceiveEvent", newMessageEvent.Type, newMessageEvent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending WebSocket notification: {ex.Message}");
            }
        }

        public async Task NotifyMessageUpdatedAsync(Guid conversationId, MessageDto message, Guid senderId)
        {
            try
            {
                var updateEvent = new MessageUpdatedEventDto
                {
                    ConversationId = conversationId,
                    Message = message,
                    UserId = senderId,
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Group(conversationId.ToString())
                    .SendAsync("ReceiveEvent", updateEvent.Type, updateEvent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message updated notification: {ex.Message}");
            }
        }

        public async Task NotifyMessageDeletedAsync(Guid conversationId, string messageId, Guid senderId, bool deletedForEveryone)
        {
            try
            {
                var deletedEvent = new MessageDeletedEventDto
                {
                    ConversationId = conversationId,
                    MessageId = messageId,
                    DeletedForEveryone = deletedForEveryone,
                    UserId = senderId,
                    Timestamp = DateTime.UtcNow
                };

                await _hubContext.Clients.Group(conversationId.ToString())
                    .SendAsync("ReceiveEvent", deletedEvent.Type, deletedEvent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message deleted notification: {ex.Message}");
            }
        }
    }
}
