using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using MessengerServer.Hubs;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.websocket
{
    /// <summary>
    /// Сервис для отправки WebSocket уведомлений через SignalR
    /// </summary>
    public interface IWebSocketNotifier
    {
        Task NotifyNewMessageAsync(Guid conversationId, MessageDto message, Guid senderId);
    }

    public class WebSocketNotifier : IWebSocketNotifier
    {
        private readonly IHubContext<MessengerHub> _hubContext;

        public WebSocketNotifier(IHubContext<MessengerHub> hubContext)
        {
            _hubContext = hubContext;
        }

        /// <summary>
        /// Отправить уведомление о новом сообщении всем участникам беседы
        /// </summary>
        public async Task NotifyNewMessageAsync(Guid conversationId, MessageDto message, Guid senderId)
        {
            try
            {
                // Создаем событие для WebSocket
                var newMessageEvent = new NewMessageEventDto
                {
                    ConversationId = conversationId,
                    Message = message,
                    UserId = senderId,
                    Timestamp = DateTime.UtcNow
                };

                // Отправляем всем участникам беседы
                await _hubContext.Clients.Group(conversationId.ToString()).SendAsync("ReceiveEvent", "new_message", newMessageEvent);

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending WebSocket notification: {ex.Message}");
            }
        }
    }
}
