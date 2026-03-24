using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using MessengerServer.Models.DTOs;
using MessengerServer.Models;
using MessengerServer.Services.messages;
using MessengerServer.Services.chat;
using MessengerServer.Services.stream;
using MessengerServer.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MessengerServer.Hubs
{
    /// <summary>
    /// SignalR Hub для real-time коммуникаций в мессенджере
    /// </summary>
    public class MessengerHub : Hub
    {
        private readonly IMessageService _messageService;
        private readonly IChatService _chatService;
        private readonly IStreamTransferService _streamTransferService;
        private readonly IOptions<StreamTransferOptions> _streamTransferOptions;
        private readonly AppDbContext _context;
        
        // Храним соединения пользователей: UserId -> ConnectionId
        private static readonly ConcurrentDictionary<Guid, string> _userConnections = new();
        // Храним группы бесед: ConversationId -> Set of UserIds
        private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, bool>> _conversationGroups = new();

        public MessengerHub(
            IMessageService messageService, 
            IChatService chatService,
            IStreamTransferService streamTransferService,
            IOptions<StreamTransferOptions> streamTransferOptions,
            AppDbContext context)
        {
            _messageService = messageService;
            _chatService = chatService;
            _streamTransferService = streamTransferService;
            _streamTransferOptions = streamTransferOptions;
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

            await SendEventToConversationExceptSenderAsync(conversationId, "message_read", readEvent);
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
            await SendEventToConversationAsync(conversationId, "new_message", newMessageEvent);
        }

        /// <summary>
        /// Индикатор печати
        /// </summary>
        public async Task SendTypingIndicator(Guid conversationId, bool isTyping, string? userName = null)
        {
            var userId = GetUserIdFromContext();
            Console.WriteLine($"[HUB] SendTypingIndicator called: ConnectionId={Context.ConnectionId}, UserId={userId}, ConversationId={conversationId}, IsTyping={isTyping}, UserName={userName}");
            if (userId == Guid.Empty)
            {
                Console.WriteLine("[HUB] SendTypingIndicator FAILED: UserId is empty (unauthorized)");
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
            await SendEventToConversationExceptSenderAsync(conversationId, "typing", typingEvent);
            Console.WriteLine($"[HUB] SendTypingIndicator delivered: ConversationId={conversationId}, UserId={userId}, IsTyping={isTyping}");
        }

        /// <summary>
        /// Инициация передачи файла в stream-чате
        /// </summary>
        public async Task<StreamTransferStartResponseDto> StartStreamTransfer(StreamTransferInitRequestDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (request.FileSize <= 0)
            {
                throw new HubException("File size must be positive");
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                throw new HubException("File name is required");
            }

            if (string.IsNullOrWhiteSpace(request.FileHash))
            {
                throw new HubException("File hash is required");
            }

            var options = _streamTransferOptions.Value;
            if (request.FileSize > options.MaxFileSizeBytes)
            {
                throw new HubException("File size exceeds maximum allowed");
            }

            if (request.ChunkSize != options.ChunkSizeBytes)
            {
                throw new HubException($"Chunk size must be {options.ChunkSizeBytes} bytes");
            }

            var expectedChunks = (int)((request.FileSize + request.ChunkSize - 1L) / request.ChunkSize);
            if (request.TotalChunks != expectedChunks)
            {
                throw new HubException("TotalChunks does not match file size");
            }

            var (_, receiverId) = await GetStreamChatPeerAsync(request.StreamChatId, userId);

            var transferId = Guid.NewGuid();
            var session = new StreamTransferSession(
                transferId,
                request.StreamChatId,
                userId,
                receiverId,
                request.FileName.Trim(),
                request.FileSize,
                request.FileHash,
                request.FileHashAlgorithm,
                request.ChunkHashAlgorithm,
                request.ChunkSize,
                request.TotalChunks,
                request.ContentType,
                request.Caption,
                options.WindowSize);

            _streamTransferService.CreateTransfer(session);

            var offer = new StreamTransferOfferDto
            {
                TransferId = transferId,
                StreamChatId = request.StreamChatId,
                SenderId = userId,
                FileName = session.FileName,
                FileSize = session.FileSize,
                FileHash = session.FileHash,
                FileHashAlgorithm = session.FileHashAlgorithm,
                ChunkHashAlgorithm = session.ChunkHashAlgorithm,
                ChunkSize = session.ChunkSize,
                TotalChunks = session.TotalChunks,
                ContentType = session.ContentType,
                Caption = session.Caption
            };

            await SendEventToUserAsync(receiverId, "stream_transfer_offer", offer);

            return new StreamTransferStartResponseDto
            {
                TransferId = transferId,
                StreamChatId = request.StreamChatId,
                ReceiverId = receiverId,
                ExpiresAt = _streamTransferService.GetExpiryTime(session.LastActivityAt)
            };
        }

        /// <summary>
        /// Принять передачу файла
        /// </summary>
        public async Task AcceptStreamTransfer(StreamTransferAcceptRequestDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            _streamTransferService.AcceptTransfer(request.TransferId, userId);

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            await SendEventToUserAsync(session.SenderId, "stream_transfer_accepted", new
            {
                TransferId = session.TransferId,
                StreamChatId = session.StreamChatId,
                ReceiverId = session.ReceiverId
            });
        }

        /// <summary>
        /// Отклонить передачу файла
        /// </summary>
        public async Task RejectStreamTransfer(StreamTransferRejectRequestDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            if (session.ReceiverId != userId)
            {
                throw new HubException("Only receiver can reject transfer");
            }

            _streamTransferService.RejectTransfer(request.TransferId, userId);

            await SendEventToUserAsync(session.SenderId, "stream_transfer_rejected", new
            {
                TransferId = request.TransferId,
                Reason = request.Reason
            });
        }

        /// <summary>
        /// Отправить чанк файла
        /// </summary>
        public async Task SendStreamChunk(StreamTransferChunkDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            if (request.Seq < 0 || request.Seq >= session.TotalChunks)
            {
                throw new HubException("Invalid chunk sequence");
            }

            if (request.Data == null || request.Data.Length == 0)
            {
                throw new HubException("Chunk data is empty");
            }

            if (request.Data.Length > session.ChunkSize)
            {
                throw new HubException("Chunk size exceeds limit");
            }

            var shouldBeLast = request.Seq == session.TotalChunks - 1;
            if (request.IsLast != shouldBeLast)
            {
                throw new HubException("Invalid last chunk flag");
            }

            var chunk = new StreamTransferChunkEnvelope
            {
                TransferId = request.TransferId,
                Seq = request.Seq,
                Data = request.Data,
                ChunkHash = request.ChunkHash,
                IsLast = request.IsLast
            };

            await _streamTransferService.EnqueueChunkAsync(request.TransferId, userId, chunk, Context.ConnectionAborted);
        }

        /// <summary>
        /// Подтвердить получение чанков
        /// </summary>
        public async Task AckStreamChunks(StreamTransferAckDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            if (session.ReceiverId != userId)
            {
                throw new HubException("Only receiver can ACK chunks");
            }

            _streamTransferService.Touch(request.TransferId);

            await SendEventToUserAsync(session.SenderId, "stream_transfer_ack", request);
        }

        /// <summary>
        /// Запросить повторную отправку чанков
        /// </summary>
        public async Task NackStreamChunks(StreamTransferNackDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            if (session.ReceiverId != userId)
            {
                throw new HubException("Only receiver can NACK chunks");
            }

            _streamTransferService.Touch(request.TransferId);

            await SendEventToUserAsync(session.SenderId, "stream_transfer_nack", request);
        }

        /// <summary>
        /// Запросить resume (повтор недостающих чанков)
        /// </summary>
        public async Task RequestStreamTransferResume(StreamTransferResumeRequestDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            if (session.ReceiverId != userId)
            {
                throw new HubException("Only receiver can request resume");
            }

            _streamTransferService.Touch(request.TransferId);

            await SendEventToUserAsync(session.SenderId, "stream_transfer_resume", request);
        }

        /// <summary>
        /// Завершить передачу файла
        /// </summary>
        public async Task CompleteStreamTransfer(StreamTransferCompleteRequestDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            if (session.ReceiverId != userId)
            {
                throw new HubException("Only receiver can complete transfer");
            }

            _streamTransferService.CompleteTransfer(request.TransferId, userId);

            await SendEventToUserAsync(session.SenderId, "stream_transfer_complete", new
            {
                TransferId = request.TransferId
            });

            await TryWriteTransferReportAsync(session);
        }

        /// <summary>
        /// Отменить передачу файла
        /// </summary>
        public async Task CancelStreamTransfer(StreamTransferCancelRequestDto request)
        {
            var userId = GetUserIdFromContext();
            if (userId == Guid.Empty)
            {
                throw new HubException("Unauthorized");
            }

            if (!_streamTransferService.TryGetSession(request.TransferId, out var session))
            {
                throw new HubException("Transfer not found or expired");
            }

            _streamTransferService.CancelTransfer(request.TransferId, userId);

            var otherUserId = userId == session.SenderId ? session.ReceiverId : session.SenderId;
            await SendEventToUserAsync(otherUserId, "stream_transfer_canceled", new
            {
                TransferId = request.TransferId,
                Reason = request.Reason
            });
        }

        /// <summary>
        /// Отправить событие всем участникам беседы
        /// </summary>
        private async Task SendEventToConversationAsync(Guid conversationId, string eventType, object payload)
        {
            await Clients.Group(conversationId.ToString())
                .SendAsync("ReceiveEvent", eventType, payload);
        }

        /// <summary>
        /// Отправить событие всем участникам беседы, кроме отправителя
        /// </summary>
        private async Task SendEventToConversationExceptSenderAsync(Guid conversationId, string eventType, object payload)
        {
            await Clients.GroupExcept(conversationId.ToString(), new[] { Context.ConnectionId })
                .SendAsync("ReceiveEvent", eventType, payload);
        }

        /// <summary>
        /// Отправить событие конкретному пользователю
        /// </summary>
        private async Task SendEventToUserAsync(Guid userId, string eventType, object payload)
        {
            await Clients.User(userId.ToString())
                .SendAsync("ReceiveEvent", eventType, payload);
        }

        private async Task TryWriteTransferReportAsync(StreamTransferSession session)
        {
            try
            {
                var invite = await _context.StreamChatInvites
                    .Where(i => i.StreamChatId == session.StreamChatId && i.Status == "Accepted")
                    .OrderByDescending(i => i.CreatedAt)
                    .FirstOrDefaultAsync();

                if (invite == null)
                {
                    Console.WriteLine($"[HUB] Stream invite not found for stream chat {session.StreamChatId}");
                    return;
                }

                var report = new StreamTransferReportDto
                {
                    StreamChatId = session.StreamChatId,
                    SenderId = session.SenderId,
                    ReceiverId = session.ReceiverId,
                    FileName = session.FileName,
                    FileSize = session.FileSize,
                    FileHash = session.FileHash,
                    Status = "completed",
                    ChunkSize = session.ChunkSize,
                    TotalChunks = session.TotalChunks,
                    StartedAt = session.CreatedAt,
                    CompletedAt = DateTime.UtcNow
                };

                var metadataJson = JsonSerializer.Serialize(report);
                var content = $"Передан файл: {session.FileName} ({session.FileSize} байт)";
                var message = await _messageService.SendSystemMessageAsync(
                    session.SenderId,
                    invite.PersonalChatId,
                    "stream_report",
                    content,
                    metadataJson);

                var newMessageEvent = new NewMessageEventDto
                {
                    ConversationId = invite.PersonalChatId,
                    Message = message,
                    UserId = session.SenderId
                };

                await SendEventToConversationAsync(invite.PersonalChatId, "new_message", newMessageEvent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUB] Failed to write stream transfer report: {ex.Message}");
            }
        }

        /// <summary>
        /// Получить UserId из HttpContext (из JWT claims)
        /// </summary>
        private Guid GetUserIdFromContext()
        {
            try
            {
                var userIdClaim = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                    ?? Context.User?.FindFirst("sub")
                    ?? Context.User?.FindFirst("userId");
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

        private async Task<(Conversation Conversation, Guid OtherUserId)> GetStreamChatPeerAsync(Guid streamChatId, Guid userId)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == streamChatId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new HubException("Stream chat not found");
            }

            if (!string.Equals(conversation.Type, "stream", StringComparison.OrdinalIgnoreCase))
            {
                throw new HubException("Conversation is not a stream chat");
            }

            var members = await _context.ConversationMembers
                .Where(cm => cm.ConversationId == streamChatId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            if (!members.Contains(userId))
            {
                throw new HubException("You are not a member of this stream chat");
            }

            if (members.Count != 2)
            {
                throw new HubException("Stream chat must have exactly two members");
            }

            var otherUserId = members.First(id => id != userId);
            return (conversation, otherUserId);
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
