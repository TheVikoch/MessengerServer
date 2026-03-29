using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.encryption;
using MessengerServer.Services.storage;

namespace MessengerServer.Services.chat
{
    public class ChatService : IChatService
    {
        private const int UploadUrlMinutes = 10;
        private const int DownloadUrlMinutes = 5;

        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;
        private readonly IStorageService _storage;
        private readonly IMongoCollection<Message> _messages;

        public ChatService(
            AppDbContext context,
            IEncryptionService encryptionService,
            IStorageService storage,
            IConfiguration configuration)
        {
            _context = context;
            _encryptionService = encryptionService;
            _storage = storage;

            var mongoConnectionString = configuration.GetConnectionString("MongoDb")
                ?? configuration["MongoDb:ConnectionString"]
                ?? "mongodb://localhost:27017/MessengerDB";

            var mongoUrl = new MongoUrl(mongoConnectionString);
            var client = new MongoClient(mongoUrl);
            var database = client.GetDatabase(mongoUrl.DatabaseName ?? "MessengerDB");
            _messages = database.GetCollection<Message>("Messages");
        }

        public async Task<ConversationDto> CreatePersonalChatAsync(Guid currentUserId, string? userEmail, string? userDisplayName)
        {
            User? targetUser = null;

            var trimmedEmail = userEmail?.Trim();
            var trimmedDisplayName = userDisplayName?.Trim();

            if (!string.IsNullOrWhiteSpace(trimmedEmail))
            {
                var encryptedEmail = _encryptionService.Encrypt(trimmedEmail);
                targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == encryptedEmail);
            }
            else if (!string.IsNullOrWhiteSpace(trimmedDisplayName))
            {
                targetUser = await _context.Users.FirstOrDefaultAsync(u =>
                    u.DisplayName != null && EF.Functions.ILike(u.DisplayName, trimmedDisplayName));
            }

            if (targetUser == null)
            {
                var identifier = trimmedEmail ?? trimmedDisplayName ?? string.Empty;
                throw new KeyNotFoundException($"User with identifier {identifier} not found");
            }

            // Check if personal chat already exists between these two users
           var existingChat = await _context.Conversations
            .Where(c => c.Type == "personal")
            .Where(c => c.Members.Any(m => m.UserId == currentUserId))
            .Where(c => c.Members.Any(m => m.UserId == targetUser.Id))
            .FirstOrDefaultAsync();



            if (existingChat != null && !existingChat.IsDeleted)
            {
                // Return existing chat
                return await GetConversationDtoAsync(existingChat.Id, currentUserId);
            }

            // Create new personal chat
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Type = "personal",
                Name = null,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.Conversations.Add(conversation);

            // Add both users as members
            var currentUserMember = new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = currentUserId,
                Role = "creator",
                JoinedAt = DateTime.UtcNow,
                IsPinned = false
            };

            var targetUserMember = new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = targetUser.Id,
                Role = "member",
                JoinedAt = DateTime.UtcNow,
                IsPinned = false
            };

            _context.ConversationMembers.Add(currentUserMember);
            _context.ConversationMembers.Add(targetUserMember);
            await _context.SaveChangesAsync();

            return await GetConversationDtoAsync(conversation.Id, currentUserId);
        }

        public async Task<ConversationDto> CreateGroupChatAsync(Guid currentUserId, CreateGroupChatDto createGroupChatDto)
        {
            if (string.IsNullOrWhiteSpace(createGroupChatDto.Name))
            {
                throw new ArgumentException("Group chat name is required");
            }

            // Create new group chat
            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Type = "group",
                Name = createGroupChatDto.Name,
                CreatedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.Conversations.Add(conversation);

            // Add current user as creator
            var creatorMember = new ConversationMember
            {
                ConversationId = conversation.Id,
                UserId = currentUserId,
                Role = "creator",
                JoinedAt = DateTime.UtcNow,
                IsPinned = false
            };
            _context.ConversationMembers.Add(creatorMember);

            // Add other members
            if (createGroupChatDto.MemberEmails != null)
            {
                foreach (var email in createGroupChatDto.MemberEmails.Distinct())
                {
                    if (string.IsNullOrWhiteSpace(email)) continue;

                    var encryptedEmail = _encryptionService.Encrypt(email);
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == encryptedEmail);
                    
                    if (user != null && user.Id != currentUserId)
                    {
                        // Check if user is already added (avoid duplicates)
                        var alreadyMember = await _context.ConversationMembers
                            .AnyAsync(cm => cm.ConversationId == conversation.Id && cm.UserId == user.Id);
                        
                        if (!alreadyMember)
                        {
                            var member = new ConversationMember
                            {
                                ConversationId = conversation.Id,
                                UserId = user.Id,
                                Role = "member",
                                JoinedAt = DateTime.UtcNow,
                                IsPinned = false
                            };
                            _context.ConversationMembers.Add(member);
                        }
                    }
                }
            }

            await _context.SaveChangesAsync();

            return await GetConversationDtoAsync(conversation.Id, currentUserId);
        }

        public async Task<List<UserSearchResultDto>> SearchUsersAsync(Guid currentUserId, string query, int limit)
        {
            var trimmedQuery = query?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedQuery))
            {
                return new List<UserSearchResultDto>();
            }

            limit = Math.Clamp(limit, 1, 20);

            var matchedUsers = await _context.Users
                .AsNoTracking()
                .Where(u =>
                    u.Id != currentUserId &&
                    u.DisplayName != null &&
                    EF.Functions.ILike(u.DisplayName, $"%{trimmedQuery}%"))
                .Select(u => new UserSearchResultDto
                {
                    Id = u.Id,
                    DisplayName = u.DisplayName ?? string.Empty,
                    LatestProfilePhotoId = u.ProfilePhotos
                        .Where(p => !p.IsDeleted && p.Status == "Ready")
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => (Guid?)p.Id)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var orderedUsers = matchedUsers
                .OrderByDescending(u => u.DisplayName.StartsWith(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                .ThenBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(limit)
                .ToList();

            if (orderedUsers.Count == 0)
            {
                return orderedUsers;
            }

            var userIds = orderedUsers
                .Select(u => u.Id)
                .ToList();

            var existingChats = await _context.Conversations
                .AsNoTracking()
                .Where(c =>
                    !c.IsDeleted &&
                    c.Type == "personal" &&
                    c.Members.Any(m => m.UserId == currentUserId) &&
                    c.Members.Any(m => userIds.Contains(m.UserId)))
                .Select(c => new
                {
                    ConversationId = c.Id,
                    OtherUserId = c.Members
                        .Where(m => m.UserId != currentUserId)
                        .Select(m => m.UserId)
                        .FirstOrDefault()
                })
                .ToListAsync();

            var existingConversationByUserId = existingChats
                .Where(item => item.OtherUserId != Guid.Empty)
                .GroupBy(item => item.OtherUserId)
                .ToDictionary(group => group.Key, group => (Guid?)group.First().ConversationId);

            foreach (var user in orderedUsers)
            {
                if (existingConversationByUserId.TryGetValue(user.Id, out var conversationId))
                {
                    user.ExistingConversationId = conversationId;
                }
            }

            return orderedUsers;
        }

        public async Task<InitConversationAvatarUploadResponseDto> InitConversationAvatarUploadAsync(
            Guid currentUserId,
            Guid conversationId,
            InitConversationAvatarUploadRequestDto request)
        {
            var conversation = await GetConversationForAvatarChangeAsync(currentUserId, conversationId);

            var contentType = request.ContentType?.Trim();
            if (string.IsNullOrWhiteSpace(contentType) || !contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Conversation avatar must be an image");
            }

            if (request.Size <= 0)
            {
                throw new ArgumentException("File size must be greater than zero");
            }

            var photoId = Guid.NewGuid();
            var objectKey = BuildConversationAvatarObjectKey(conversation.Id, photoId);
            var expiresIn = TimeSpan.FromMinutes(UploadUrlMinutes);
            var uploadUrl = await _storage.GetUploadUrlAsync(objectKey, contentType, expiresIn);

            return new InitConversationAvatarUploadResponseDto
            {
                PhotoId = photoId,
                UploadUrl = uploadUrl,
                ExpiresAt = DateTime.UtcNow.Add(expiresIn)
            };
        }

        public async Task<ConversationDto> CompleteConversationAvatarUploadAsync(
            Guid currentUserId,
            Guid conversationId,
            CompleteConversationAvatarUploadRequestDto request)
        {
            if (request.PhotoId == Guid.Empty)
            {
                throw new ArgumentException("Avatar photo id is required");
            }

            var conversation = await GetConversationForAvatarChangeAsync(currentUserId, conversationId);
            var objectKey = BuildConversationAvatarObjectKey(conversationId, request.PhotoId);
            var exists = await _storage.ExistsAsync(objectKey);
            if (!exists)
            {
                throw new InvalidOperationException("Uploaded avatar file was not found in storage");
            }

            var previousObjectKey = conversation.AvatarObjectKey;
            conversation.AvatarPhotoId = request.PhotoId;
            conversation.AvatarObjectKey = objectKey;
            conversation.AvatarUpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(previousObjectKey) &&
                !string.Equals(previousObjectKey, objectKey, StringComparison.Ordinal))
            {
                await _storage.DeleteAsync(previousObjectKey);
            }

            return await GetConversationDtoAsync(conversationId, currentUserId);
        }

        public async Task<MediaUrlResponseDto> GetConversationAvatarUrlAsync(Guid currentUserId, Guid conversationId, Guid avatarPhotoId)
        {
            var conversation = await GetConversationForAvatarReadAsync(currentUserId, conversationId);
            if (conversation.AvatarPhotoId != avatarPhotoId || string.IsNullOrWhiteSpace(conversation.AvatarObjectKey))
            {
                throw new KeyNotFoundException("Conversation avatar not found");
            }

            var expiresIn = TimeSpan.FromMinutes(DownloadUrlMinutes);
            var url = await _storage.GetDownloadUrlAsync(conversation.AvatarObjectKey, expiresIn);

            return new MediaUrlResponseDto
            {
                Url = url,
                ExpiresAt = DateTime.UtcNow.Add(expiresIn)
            };
        }

        public async Task<bool> HasMessagesAsync(Guid currentUserId, Guid conversationId)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == currentUserId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var conversationExists = await _context.Conversations
                .AnyAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (!conversationExists)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            var filter = Builders<Message>.Filter.And(
                Builders<Message>.Filter.Eq(m => m.ConversationId, conversationId),
                Builders<Message>.Filter.Eq(m => m.IsDeleted, false)
            );

            return await _messages.Find(filter).AnyAsync();
        }

        public async Task<ConversationDto> GetConversationAsync(Guid userId, Guid conversationId)
        {
            // Check if user is a member of the conversation
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            return await GetConversationDtoAsync(conversation.Id, userId);
        }

        public async Task<List<ConversationDto>> GetConversationsForUserAsync(Guid userId)
        {
            var memberships = await _context.ConversationMembers
                .Where(cm => cm.UserId == userId)
                .Select(cm => new
                {
                    cm.ConversationId,
                    cm.ClearedAt
                })
                .ToListAsync();

            var conversationIds = memberships.Select(m => m.ConversationId).ToList();
            var conversations = await _context.Conversations
                .Where(c => conversationIds.Contains(c.Id) && !c.IsDeleted)
                .ToListAsync();

            var result = new List<ConversationDto>();
            foreach (var membership in memberships)
            {
                if (conversations.All(c => c.Id != membership.ConversationId))
                {
                    continue;
                }

                var dto = await GetConversationDtoAsync(membership.ConversationId, userId);
                if (membership.ClearedAt.HasValue && dto.LastMessageAt == null)
                {
                    continue;
                }
                result.Add(dto);
            }

            return result
                .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
                .ToList();
        }

        public async Task DeleteConversationForUserAsync(Guid currentUserId, Guid conversationId)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            var member = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == currentUserId);

            if (member == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var clearedAt = DateTime.UtcNow;
            member.ClearedAt = clearedAt;
            if (member.LastReadAt == null || member.LastReadAt < clearedAt)
            {
                member.LastReadAt = clearedAt;
            }
            member.LastReadMessageId = null;
            await _context.SaveChangesAsync();
        }

        public async Task<List<Guid>> DeletePersonalConversationForEveryoneAsync(Guid currentUserId, Guid conversationId)
        {
            var conversation = await _context.Conversations
                .Include(c => c.Members)
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            if (!string.Equals(conversation.Type, "personal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only personal chats can be deleted for everyone");
            }

            if (!conversation.Members.Any(m => m.UserId == currentUserId))
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            conversation.IsDeleted = true;
            await _context.SaveChangesAsync();

            return conversation.Members
                .Select(m => m.UserId)
                .Distinct()
                .ToList();
        }

        public async Task<ConversationDto> AddMemberAsync(Guid currentUserId, Guid conversationId, string? userEmail, string? userDisplayName)
        {
            // Get conversation
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            // Only group chats can add members
            if (conversation.Type != "group")
            {
                throw new InvalidOperationException("Cannot add members to a personal chat");
            }

            // Check if current user is creator or admin
            var currentUserMember = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == currentUserId);

            if (currentUserMember == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            if (currentUserMember.Role != "creator" && currentUserMember.Role != "admin")
            {
                throw new UnauthorizedAccessException("Only creator or admin can add members");
            }

            var trimmedEmail = userEmail?.Trim();
            var trimmedDisplayName = userDisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(trimmedEmail) && string.IsNullOrWhiteSpace(trimmedDisplayName))
            {
                throw new ArgumentException("User email or display name is required");
            }

            // Find the user to add
            User? userToAdd = null;
            if (!string.IsNullOrWhiteSpace(trimmedEmail))
            {
                var encryptedEmail = _encryptionService.Encrypt(trimmedEmail);
                userToAdd = await _context.Users.FirstOrDefaultAsync(u => u.Email == encryptedEmail);
            }
            else if (!string.IsNullOrWhiteSpace(trimmedDisplayName))
            {
                userToAdd = await _context.Users.FirstOrDefaultAsync(u =>
                    u.DisplayName != null && EF.Functions.ILike(u.DisplayName, trimmedDisplayName));
            }

            if (userToAdd == null)
            {
                var identifier = trimmedEmail ?? trimmedDisplayName ?? string.Empty;
                throw new KeyNotFoundException($"User with identifier {identifier} not found");
            }

            // Check if user is already a member
            var existingMember = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userToAdd.Id);

            if (existingMember != null)
            {
                throw new InvalidOperationException("User is already a member of this conversation");
            }

            // Add the member
            var newMember = new ConversationMember
            {
                ConversationId = conversationId,
                UserId = userToAdd.Id,
                Role = "member",
                JoinedAt = DateTime.UtcNow,
                IsPinned = false
            };

            _context.ConversationMembers.Add(newMember);
            await _context.SaveChangesAsync();

            return await GetConversationDtoAsync(conversationId, currentUserId);
        }

        public async Task RemoveMemberAsync(Guid currentUserId, Guid conversationId, Guid userIdToRemove)
        {
            // Get conversation
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            // Check if current user is the creator
            var currentUserMember = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == currentUserId);

            if (currentUserMember == null || currentUserMember.Role != "creator")
            {
                throw new UnauthorizedAccessException("Only the creator can remove members");
            }

            // Find the member to remove
            var memberToRemove = await _context.ConversationMembers
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == userIdToRemove);

            if (memberToRemove == null)
            {
                throw new KeyNotFoundException("Member not found in this conversation");
            }

            // Cannot remove the creator
            if (memberToRemove.Role == "creator")
            {
                throw new InvalidOperationException("Cannot remove the creator of the conversation");
            }

            _context.ConversationMembers.Remove(memberToRemove);
            await _context.SaveChangesAsync();
        }

        private async Task<ConversationDto> GetConversationDtoAsync(Guid conversationId, Guid requestingUserId)
        {
            var membership = await _context.ConversationMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(cm => cm.ConversationId == conversationId && cm.UserId == requestingUserId);

            if (membership == null)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var conversation = await _context.Conversations
                .Where(c => c.Id == conversationId)
                .Select(c => new ConversationDto
                {
                    Id = c.Id,
                    Type = c.Type,
                    Name = c.Name,
                    AvatarPhotoId = c.AvatarPhotoId,
                    CreatedAt = c.CreatedAt,
                    LastMessageAt = c.LastMessageAt,
                    IsDeleted = c.IsDeleted,
                    Members = c.Members
                        .OrderBy(m => m.JoinedAt)
                        .Select(m => new ConversationMemberDto
                        {
                            UserId = m.UserId,
                            User = new UserDto
                            {
                                Id = m.User.Id,
                                Email = m.User.Email,
                                DisplayName = m.User.DisplayName ?? string.Empty,
                                LatestProfilePhotoId = m.User.ProfilePhotos
                                    .Where(p => !p.IsDeleted && p.Status == "Ready")
                                    .OrderByDescending(p => p.CreatedAt)
                                    .Select(p => (Guid?)p.Id)
                                    .FirstOrDefault()
                            },
                            Role = m.Role,
                            JoinedAt = m.JoinedAt,
                            IsPinned = m.IsPinned
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (conversation == null)
                throw new KeyNotFoundException("Conversation not found");

            var lastMessage = await GetLastVisibleMessageAsync(conversationId, requestingUserId, membership.ClearedAt);

            if (lastMessage != null)
            {
                conversation.LastMessageAt = lastMessage.SentAt;
                try
                {
                    conversation.LastMessageContent = _encryptionService.Decrypt(lastMessage.EncryptedContent);
                }
                catch
                {
                    conversation.LastMessageContent = null;
                }
            }
            else
            {
                conversation.LastMessageAt = null;
                conversation.LastMessageContent = null;
            }

            return conversation;
        }

        private async Task<Conversation> GetConversationForAvatarChangeAsync(Guid currentUserId, Guid conversationId)
        {
            var conversation = await GetConversationForAvatarReadAsync(currentUserId, conversationId);
            if (conversation.Type == "personal")
            {
                throw new InvalidOperationException("Personal chats do not support a conversation avatar");
            }

            return conversation;
        }

        private async Task<Conversation> GetConversationForAvatarReadAsync(Guid currentUserId, Guid conversationId)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == currentUserId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            return conversation;
        }

        private static string BuildConversationAvatarObjectKey(Guid conversationId, Guid avatarPhotoId)
        {
            return $"conversations/{conversationId}/avatar/{avatarPhotoId:N}";
        }

        private async Task<Message?> GetLastVisibleMessageAsync(Guid conversationId, Guid userId, DateTime? clearedAt)
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

            return await _messages.Find(filter)
                .SortByDescending(m => m.SentAt)
                .FirstOrDefaultAsync();
        }

    }
}

