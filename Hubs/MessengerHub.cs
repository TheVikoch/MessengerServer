using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.messages;
using MessengerServer.Services.chat;
using MessengerServer.Data;
using Microsoft.EntityFrameworkCore;

namespace MessengerServer.Hubs
{
    /// <summary>
    /// SignalR Hub для real-time коммуникаций в мессенджере
    /// </summary>
    public class MessengerHub : Hub
    {
        private readonly IMessageService _messageService;
        private readonly IChatService _chatService;
        private readonly AppDbContext _context;
        
        // Храним соединения пользователей: UserId -> ConnectionId
        private static readonly ConcurrentDictionary<Guid, string> _userConnections = new();
        // Храним группы бесед: ConversationId -> Set of UserIds
        private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, bool>> _conversationGroups = new();

        public MessengerHub(
            IMessageService messageService, 
            IChatService chatService,
            AppDbContext context)
        {
            _messageService = messageService;
            _chatService = chatService;
            _context = context;
        }

        /// <summary>
        /// Вызывается при подключении клиента
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = GetUserIdFromContext();
            Console.WriteLine($"[HUB] OnConnected: ConnectionId={Context.ConnectionId}, ExtractedUserId={userId}");
            
            if (userId != Guid.Empty)
            {
                // Сохраняем соединение пользователя
                _userConnections[userId] = Context.ConnectionId;
                
                // Проверяем, сколько бесед у пользователя в БД
                var conversationCount = await _context.ConversationMembers
                    .Where(cm => cm.UserId == userId)
                    .CountAsync();
                Console.WriteLine($"[HUB] User {userId} has {conversationCount} conversations in DB");
                
                // Добавляем пользователя во все группы бесед, в которых он состоит
                await JoinUserConversationsAsync(userId);
                Console.WriteLine($"[HUB] User {userId} auto-joined conversations");
            }
            else
            {
                Console.WriteLine($"[HUB] UserId is empty! JWT token may not contain NameIdentifier claim.");
            }
            
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Вызывается при отключении клиента
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = GetUserIdFromContext();
            if (userId != Guid.Empty)
            {
                // Удаляем соединение пользователя
                _userConnections.TryRemove(userId, out _);
                
                // Удаляем пользователя из всех групп
                await LeaveUserConversationsAsync(userId);
                
                Console.WriteLine($"User {userId} disconnected. Reason: {exception?.Message ?? "Normal disconnect"}");
            }
            
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Подключиться к группе беседы (комнате)
        /// </summary>
        public async Task JoinConversation(Guid conversationId)
        {
            var userId = GetUserIdFromContext();
            Console.WriteLine($"[HUB] JoinConversation called: ConnectionId={Context.ConnectionId}, UserId={userId}, ConversationId={conversationId}");
            
            if (userId == Guid.Empty)
            {
                Console.WriteLine($"[HUB] JoinConversation FAILED: UserId is empty (unauthorized)");
                throw new HubException("Unauthorized");
            }

            // Проверяем, является ли пользователь участником беседы
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            Console.WriteLine($"[HUB] Is user {userId} member of conversation {conversationId}? {isMember}");

            if (!isMember)
            {
                Console.WriteLine($"[HUB] JoinConversation FAILED: User is not a member");
                throw new HubException("You are not a member of this conversation");
            }

            // Добавляем соединение в группу беседы
            await Groups.AddToGroupAsync(Context.ConnectionId, conversationId.ToString());
            
            // Сохраняем информацию о группе
            var group = _conversationGroups.GetOrAdd(conversationId, _ => new());
            group[userId] = true;

            Console.WriteLine($"[HUB] User {userId} successfully joined conversation {conversationId}. Group now has {group.Count} members");
        }

        /// <summary>
        /// Покинуть группу беседы
        /// </summary>
        public async Task LeaveConversation(Guid conversationId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId.ToString());
            
            var userId = GetUserIdFromContext();
            if (userId != Guid.Empty && _conversationGroups.ContainsKey(conversationId))
            {
                _conversationGroups[conversationId].TryRemove(userId, out _);
            }
            
            Console.WriteLine($"User {userId} left conversation {conversationId}");
        }

        /// <summary>
        /// Отметить сообщение как прочитанное
        /// </summary>
        public async Task MarkMessageAsRead(Guid conversationId, string messageId)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            // Вызываем сервис для отметки прочтения
            await _messageService.MarkAsReadAsync(userId, conversationId, messageId);

            // Отправляем уведомление другим участникам беседы
            var readEvent = new MessageReadEventDto
            {
                ConversationId = conversationId,
                MessageId = messageId,
                ReadByUserId = userId,
                UserId = userId
            };

            await SendToConversationExceptSenderAsync(conversationId, readEvent, userId);
        }

        /// <summary>
        /// Отправить сообщение (опционально через WebSocket, но обычно через REST API)
        /// </summary>
        public async Task SendMessage(Guid conversationId, string content, string? replyToMessageId = null)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            var sendMessageDto = new SendMessageDto
            {
                ConversationId = conversationId,
                Content = content,
                ReplyToMessageId = replyToMessageId
            };

            // Используем MessageService для отправки
            var messageDto = await _messageService.SendMessageAsync(userId, sendMessageDto);

            // Создаем событие нового сообщения
            var newMessageEvent = new NewMessageEventDto
            {
                ConversationId = conversationId,
                Message = messageDto,
                UserId = userId
            };

            // Отправляем всем участникам беседы (включая отправителя)
            await SendToConversationAsync(conversationId, newMessageEvent);
        }

        /// <summary>
        /// Индикатор печати
        /// </summary>
        public async Task SendTypingIndicator(Guid conversationId, bool isTyping, string? userName = null)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            var typingEvent = new TypingEventDto
            {
                ConversationId = conversationId,
                IsTyping = isTyping,
                UserName = userName,
                UserId = userId
            };

            // Отправляем другим участникам (не себе)
            await SendToConversationExceptSenderAsync(conversationId, typingEvent, userId);
        }

        /// <summary>
        /// Отправить событие всем участникам беседы
        /// </summary>
        private async Task SendToConversationAsync(Guid conversationId, object message)
        {
            await Clients.Group(conversationId.ToString())
                .SendAsync("ReceiveEvent", message);
        }

        /// <summary>
        /// Отправить событие всем участникам беседы, кроме отправителя
        /// </summary>
        private async Task SendToConversationExceptSenderAsync(Guid conversationId, object message, Guid senderId)
        {
            await Clients.GroupExcept(conversationId.ToString(), new[] { Context.ConnectionId })
                .SendAsync("ReceiveEvent", message);
        }

        /// <summary>
        /// Получить UserId из HttpContext (из JWT claims)
        /// </summary>
        private Guid GetUserIdFromContext()
        {
            try
            {
                var userIdClaim = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
                {
                    return userId;
                }
            }
            catch
            {
                // Игнорируем ошибки и возвращаем пустой GUID
            }
            
            return Guid.Empty;
        }

        /// <summary>
        /// Присоединить пользователя ко всем его беседам при подключении
        /// </summary>
        private async Task JoinUserConversationsAsync(Guid userId)
        {
            var conversationIds = await _context.ConversationMembers
                .Where(cm => cm.UserId == userId)
                .Select(cm => cm.ConversationId)
                .ToListAsync();

            foreach (var conversationId in conversationIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, conversationId.ToString());
                
                var group = _conversationGroups.GetOrAdd(conversationId, _ => new());
                group[userId] = true;
            }
        }

        /// <summary>
        /// Удалить пользователя из всех групп при отключении
        /// </summary>
        private async Task LeaveUserConversationsAsync(Guid userId)
        {
            var conversationsToRemove = new List<Guid>();

            foreach (var kvp in _conversationGroups)
            {
                if (kvp.Value.TryRemove(userId, out _))
                {
                    conversationsToRemove.Add(kvp.Key);
                }
            }

            // Удаляем пользователя из групп SignalR
            foreach (var conversationId in conversationsToRemove)
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, conversationId.ToString());
            }
        }
    }
}
