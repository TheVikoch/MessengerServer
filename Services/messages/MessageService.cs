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

        public async Task<MessageDto> UpdateMessageAsync(Guid userId, Guid conversationId, string messageId, UpdateMessageDto updateMessageDto)
        {
            var message = await RequireEditableMessageAsync(userId, conversationId, messageId, requirePersonalConversation: false);

            if (string.IsNullOrWhiteSpace(updateMessageDto.Content) && message.Attachments.Count == 0)
            {
                throw new ArgumentException("Message content or attachments are required");
            }

            var encryptedContent = _encryptionService.Encrypt(updateMessageDto.Content ?? string.Empty);
            var editedAt = DateTime.UtcNow;

            var update = Builders<Message>.Update
                .Set(m => m.EncryptedContent, encryptedContent)
                .Set(m => m.EditedAt, editedAt);

            await _messages.UpdateOneAsync(m => m.Id == messageId, update);
            message.EncryptedContent = encryptedContent;
            message.EditedAt = editedAt;

            return await GetMessageDtoAsync(message);
        }

        public async Task DeleteMessageForUserAsync(Guid userId, Guid conversationId, string messageId)
        {
            await RequireVisibleMessageAsync(userId, conversationId, messageId);

            var existing = await _context.DeletedMessagesForUsers
                .FirstOrDefaultAsync(entry => entry.UserId == userId && entry.MessageId == messageId);

            if (existing == null)
            {
                _context.DeletedMessagesForUsers.Add(new DeletedMessageForUser
                {
                    UserId = userId,
                    ConversationId = conversationId,
                    MessageId = messageId,
                    DeletedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }
        }

        public async Task DeleteMessageForEveryoneAsync(Guid userId, Guid conversationId, string messageId)
        {
            var message = await RequireEditableMessageAsync(userId, conversationId, messageId, requirePersonalConversation: true);

            var update = Builders<Message>.Update
                .Set(m => m.IsDeleted, true);

            await _messages.UpdateOneAsync(m => m.Id == messageId, update);
            message.IsDeleted = true;

            var hiddenEntries = await _context.DeletedMessagesForUsers
                .Where(entry => entry.MessageId == messageId)
                .ToListAsync();

            if (hiddenEntries.Count > 0)
            {
                _context.DeletedMessagesForUsers.RemoveRange(hiddenEntries);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<MessagesResponseDto> GetMessagesAsync(Guid userId, Guid conversationId, int limit = 50, string? cursor = null)
        {
            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var filter = await BuildVisibleMessageFilterAsync(userId, conversationId, member.ClearedAt);

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

        public async Task<ConversationAttachmentsResponseDto> GetConversationAttachmentsAsync(
            Guid userId,
            Guid conversationId,
            int limit = 100,
            string? cursor = null)
        {
            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var filter = await BuildVisibleMessageFilterAsync(userId, conversationId, member.ClearedAt);
            filter = Builders<Message>.Filter.And(
                filter,
                Builders<Message>.Filter.Exists("Attachments.0")
            );

            if (!string.IsNullOrEmpty(cursor) && DateTime.TryParse(cursor, out var cursorDate))
            {
                filter = Builders<Message>.Filter.And(
                    filter,
                    Builders<Message>.Filter.Lt(m => m.SentAt, cursorDate)
                );
            }

            var messages = await _messages
                .Find(filter)
                .Sort(Builders<Message>.Sort.Descending(m => m.SentAt))
                .Limit(limit + 1)
                .ToListAsync();

            var hasMore = messages.Count > limit;
            if (hasMore)
            {
                messages = messages.Take(limit).ToList();
            }

            var senderIds = messages
                .Select(m => m.SenderId)
                .Distinct()
                .ToList();

            var senders = await _context.Users
                .Where(u => senderIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    DisplayName = u.DisplayName ?? string.Empty,
                    u.Email
                })
                .ToListAsync();

            var senderLabels = senders.ToDictionary(
                sender => sender.Id,
                sender =>
                {
                    if (!string.IsNullOrWhiteSpace(sender.DisplayName))
                    {
                        return sender.DisplayName;
                    }

                    return string.IsNullOrWhiteSpace(sender.Email)
                        ? "Собеседник"
                        : _encryptionService.Decrypt(sender.Email);
                }
            );

            var attachments = new List<ConversationAttachmentEntryDto>();
            foreach (var message in messages)
            {
                var senderLabel = senderLabels.TryGetValue(message.SenderId, out var resolvedLabel) &&
                    !string.IsNullOrWhiteSpace(resolvedLabel)
                    ? resolvedLabel
                    : "Собеседник";

                foreach (var attachment in message.Attachments)
                {
                    attachments.Add(new ConversationAttachmentEntryDto
                    {
                        ConversationId = message.ConversationId,
                        MessageId = message.Id,
                        SenderLabel = senderLabel,
                        SentAt = message.SentAt,
                        Attachment = new MessageAttachmentDto
                        {
                            Id = attachment.Id,
                            FileName = attachment.FileName,
                            ContentType = attachment.ContentType,
                            Size = attachment.Size,
                            Status = attachment.Status,
                            CreatedAt = attachment.CreatedAt,
                            Encryption = attachment.Encryption == null
                                ? null
                                : new MediaEncryptionMetadataDto
                                {
                                    Algorithm = attachment.Encryption.Algorithm,
                                    KeyId = attachment.Encryption.KeyId,
                                    IvBase64 = attachment.Encryption.IvBase64,
                                    Version = attachment.Encryption.Version
                                }
                        }
                    });
                }
            }

            return new ConversationAttachmentsResponseDto
            {
                Attachments = attachments,
                HasMore = hasMore,
                NextCursor = hasMore && messages.Any() ? messages.Last().SentAt.ToString("o") : null
            };
        }

        public async Task<int> GetUnreadCountAsync(Guid userId, Guid conversationId)
        {
            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var threshold = member.LastReadAt;
            if (member.ClearedAt.HasValue && (!threshold.HasValue || member.ClearedAt > threshold))
            {
                threshold = member.ClearedAt;
            }

            if (threshold == null)
            {
                return 0;
            }

            var filter = await BuildVisibleMessageFilterAsync(userId, conversationId, member.ClearedAt);
            filter = Builders<Message>.Filter.And(
                filter,
                Builders<Message>.Filter.Gt(m => m.SentAt, threshold.Value),
                Builders<Message>.Filter.Ne(m => m.SenderId, userId)
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
                    DisplayName = u.DisplayName ?? string.Empty,
                    LatestProfilePhotoId = u.ProfilePhotos
                        .Where(p => !p.IsDeleted && p.Status == "Ready")
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => (Guid?)p.Id)
                        .FirstOrDefault()
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
                EditedAt = message.EditedAt,
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

        private async Task<FilterDefinition<Message>> BuildVisibleMessageFilterAsync(
            Guid userId,
            Guid conversationId,
            DateTime? clearedAt)
        {
            var deletedMessageIds = await _context.DeletedMessagesForUsers
                .Where(entry => entry.UserId == userId && entry.ConversationId == conversationId)
                .Select(entry => entry.MessageId)
                .ToListAsync();

            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                Builders<Message>.Filter.Eq(m => m.IsDeleted, false)
            );

            if (clearedAt.HasValue)
            {
                filter = Builders<Message>.Filter.And(
                    filter,
                    Builders<Message>.Filter.Gt(m => m.SentAt, clearedAt.Value)
                );
            }

            if (deletedMessageIds.Count > 0)
            {
                filter = Builders<Message>.Filter.And(
                    filter,
                    Builders<Message>.Filter.Nin(m => m.Id, deletedMessageIds)
                );
            }

            return filter;
        }

        private async Task<Message> RequireVisibleMessageAsync(Guid userId, Guid conversationId, string messageId)
        {
            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var filter = await BuildVisibleMessageFilterAsync(userId, conversationId, member.ClearedAt);
            filter = Builders<Message>.Filter.And(
                filter,
                Builders<Message>.Filter.Eq(m => m.Id, messageId)
            );

            var message = await _messages.Find(filter).FirstOrDefaultAsync();
            if (message == null)
            {
                throw new KeyNotFoundException("Message not found");
            }

            return message;
        }

        private async Task<Message> RequireEditableMessageAsync(
            Guid userId,
            Guid conversationId,
            string messageId,
            bool requirePersonalConversation)
        {
            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            if (requirePersonalConversation &&
                !string.Equals(conversation.Type, "personal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("This action is available only in personal chats");
            }

            var message = await _messages.Find(m =>
                    m.Id == messageId &&
                    m.ConversationId == conversationId &&
                    !m.IsDeleted)
                .FirstOrDefaultAsync();

            if (message == null)
            {
                throw new KeyNotFoundException("Message not found");
            }

            if (message.SenderId != userId)
            {
                throw new UnauthorizedAccessException("You can only modify your own messages");
            }

            return message;
        }
    }
}

