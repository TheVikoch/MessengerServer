using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Driver;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.encryption;
using Microsoft.EntityFrameworkCore;

namespace MessengerServer.Services.messages
{
    public class MessageService : IMessageService
    {
        private readonly IMongoCollection<Message> _messages;
        private readonly IMongoCollection<MediaUpload> _uploads;
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
    
        public MessageService(IConfiguration configuration, AppDbContext context, IEncryptionService encryptionService)
        {
            var mongoConnectionString = configuration.GetConnectionString("MongoDb") 
                ?? configuration["MongoDb:ConnectionString"] 
                ?? "mongodb://localhost:27017/MessengerDB";
            
            var mongoUrl = new MongoUrl(mongoConnectionString);
            var client = new MongoClient(mongoUrl);
            var database = client.GetDatabase(mongoUrl.DatabaseName ?? "MessengerDB");
            _messages = database.GetCollection<Message>("Messages");
            _uploads = database.GetCollection<MediaUpload>("MediaUploads");
            
            _context = context;
            _encryptionService = encryptionService;
        }

        public async Task<MessageDto> SendMessageAsync(Guid senderId, SendMessageDto sendMessageDto)
        {
            // Verify that sender is a member of the conversation
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == sendMessageDto.ConversationId && cm.UserId == senderId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            if (string.IsNullOrWhiteSpace(sendMessageDto.Content) &&
                (sendMessageDto.AttachmentIds == null || sendMessageDto.AttachmentIds.Count == 0))
            {
                throw new ArgumentException("Message content or attachments are required");
            }

            var attachments = new List<MessageAttachment>();
            var attachmentIds = sendMessageDto.AttachmentIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList() ?? new List<string>();

            if (attachmentIds.Count > 0)
            {
                var uploads = await _uploads.Find(u => attachmentIds.Contains(u.Id)).ToListAsync();

                if (uploads.Count != attachmentIds.Count)
                {
                    throw new KeyNotFoundException("One or more uploads not found");
                }

                if (uploads.Any(u => u.ConversationId != sendMessageDto.ConversationId || u.UserId != senderId))
                {
                    throw new UnauthorizedAccessException("Upload does not belong to this user or conversation");
                }

                if (uploads.Any(u => !string.Equals(u.Status, "Ready", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("One or more uploads are not ready");
                }

                var uploadsById = uploads.ToDictionary(u => u.Id, u => u);
                attachments = attachmentIds.Select(id => uploadsById[id]).Select(u => new MessageAttachment
                {
                    Id = u.Id,
                    ObjectKey = u.ObjectKey,
                    FileName = u.FileName,
                    ContentType = u.ContentType,
                    Size = u.Size,
                    Status = "Ready",
                    CreatedAt = u.CreatedAt,
                    Encryption = u.Encryption
                }).ToList();
            }

            // Encrypt the message content
            var encryptedContent = _encryptionService.Encrypt(sendMessageDto.Content);

            var message = new Message
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = sendMessageDto.ConversationId,
                SenderId = senderId,
                EncryptedContent = encryptedContent,
                SentAt = DateTime.UtcNow,
                IsDeleted = false,
                ReplyToMessageId = sendMessageDto.ReplyToMessageId,
                Kind = "text",
                MetadataJson = null,
                Attachments = attachments
            };

            await _messages.InsertOneAsync(message);

            if (attachmentIds.Count > 0)
            {
                await _uploads.DeleteManyAsync(u => attachmentIds.Contains(u.Id));
            }

            // Update conversation's LastMessageAt
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == sendMessageDto.ConversationId);
            
            if (conversation != null)
            {
                conversation.LastMessageAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            // РџРѕР»СѓС‡Р°РµРј DTO РґР»СЏ СЃРѕРѕР±С‰РµРЅРёСЏ
            var resultDto = await GetMessageDtoAsync(message);
            
            return resultDto;
        }

        public async Task<MessageDto> SendSystemMessageAsync(Guid senderId, Guid conversationId, string kind, string content, string? metadataJson)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("Message kind is required");
            }

            if (string.IsNullOrWhiteSpace(content) && string.IsNullOrWhiteSpace(metadataJson))
            {
                throw new ArgumentException("Message content or metadata is required");
            }

            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == senderId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var encryptedContent = _encryptionService.Encrypt(content ?? string.Empty);

            var message = new Message
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = conversationId,
                SenderId = senderId,
                EncryptedContent = encryptedContent,
                SentAt = DateTime.UtcNow,
                IsDeleted = false,
                ReplyToMessageId = null,
                Kind = kind,
                MetadataJson = metadataJson,
                Attachments = new List<MessageAttachment>()
            };

            await _messages.InsertOneAsync(message);

            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation != null)
            {
                conversation.LastMessageAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return await GetMessageDtoAsync(message);
        }

        public async Task<MessagesResponseDto> GetMessagesAsync(Guid userId, Guid conversationId, int limit = 50, string? cursor = null)
        {
            // Verify that user is a member of the conversation
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                Builders<Message>.Filter.Eq(m => m.IsDeleted, false)
            );

            // If cursor provided, get messages older than cursor
            if (!string.IsNullOrEmpty(cursor) && DateTime.TryParse(cursor, out var cursorDate))
            {
                filter = Builders<Message>.Filter.And(
                    filter,
                    Builders<Message>.Filter.Lt(m => m.SentAt, cursorDate)
                );
            }

            var sort = Builders<Message>.Sort.Descending(m => m.SentAt);

            var messages = await _messages
                .Find(filter)
                .Sort(sort)
                .Limit(limit + 1) // Get one extra to check if there are more
                .ToListAsync();

            var hasMore = messages.Count > limit;
            if (hasMore)
            {
                messages = messages.Take(limit).ToList();
            }

            var messageDtos = new List<MessageDto>();
            foreach (var message in messages)
            {
                var dto = await GetMessageDtoAsync(message);
                messageDtos.Add(dto);
            }

            var nextCursor = hasMore && messages.Any() 
                ? messages.Last().SentAt.ToString("o") 
                : null;

            return new MessagesResponseDto
            {
                Messages = messageDtos,
                HasMore = hasMore,
                NextCursor = nextCursor
            };
        }

        public async Task<int> GetUnreadCountAsync(Guid userId, Guid conversationId)
        {
            // Get the last read time for this user in this conversation
            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null || member.LastReadAt == null)
            {
                return 0;
            }

            // Count messages sent after LastReadAt by other users
            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                Builders<Message>.Filter.Gt(m => m.SentAt, member.LastReadAt),
                Builders<Message>.Filter.Ne(m => m.SenderId, userId),
                Builders<Message>.Filter.Eq(m => m.IsDeleted, false)
            );

            var count = await _messages.CountDocumentsAsync(filter);
            return (int)count;
        }

        public async Task MarkAsReadAsync(Guid userId, Guid conversationId, string messageId)
        {
            // Update the member's LastReadAt to the message's SentAt
            var message = await _messages.Find(m => m.Id == messageId).FirstOrDefaultAsync();
            if (message == null)
            {
                throw new KeyNotFoundException("Message not found");
            }

            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            // Update LastReadAt if this message is newer than current value
            if (member.LastReadAt == null || message.SentAt > member.LastReadAt)
            {
                member.LastReadAt = message.SentAt;
                member.LastReadMessageId = messageId;
                await _context.SaveChangesAsync();
            }
        }

        private async Task<MessageDto> GetMessageDtoAsync(Message message)
        {
            // Decrypt the content
            var decryptedContent = _encryptionService.Decrypt(message.EncryptedContent);

            // Get sender info
            var sender = await _context.Users
                .Where(u => u.Id == message.SenderId)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Email = u.Email, // This is encrypted in DB
                    DisplayName = u.DisplayName ?? string.Empty
                })
                .FirstOrDefaultAsync();

            return new MessageDto
            {
                Id = message.Id,
                ConversationId = message.ConversationId,
                SenderId = message.SenderId,
                Sender = sender,
                Content = decryptedContent,
                Kind = message.Kind,
                MetadataJson = message.MetadataJson,
                SentAt = message.SentAt,
                IsDeleted = message.IsDeleted,
                ReplyToMessageId = message.ReplyToMessageId,
                Attachments = message.Attachments.Select(a => new MessageAttachmentDto
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    Size = a.Size,
                    Status = a.Status,
                    CreatedAt = a.CreatedAt,
                    Encryption = a.Encryption == null
                        ? null
                        : new MediaEncryptionMetadataDto
                        {
                            Algorithm = a.Encryption.Algorithm,
                            KeyId = a.Encryption.KeyId,
                            IvBase64 = a.Encryption.IvBase64,
                            Version = a.Encryption.Version
                        }
                }).ToList()
            };
        }
    }
}

