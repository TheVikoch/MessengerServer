using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.encryption;

namespace MessengerServer.Services.chat
{
    public class ChatService : IChatService
    {
        private readonly AppDbContext _context;
        private readonly IEncryptionService _encryptionService;

        public ChatService(AppDbContext context, IEncryptionService encryptionService)
        {
            _context = context;
            _encryptionService = encryptionService;
        }

        public async Task<ConversationDto> CreatePersonalChatAsync(Guid currentUserId, string userEmail)
        {
            // Encrypt the email for lookup
            var encryptedEmail = _encryptionService.Encrypt(userEmail);
            
            // Find the target user
            var targetUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == encryptedEmail);
            if (targetUser == null)
            {
                throw new KeyNotFoundException($"User with email {userEmail} not found");
            }

            // Check if personal chat already exists between these two users
            var existingChat = await _context.ConversationMembers
                .Where(cm => cm.UserId == currentUserId)
                .Join(_context.ConversationMembers,
                    cm1 => cm1.ConversationId,
                    cm2 => cm2.ConversationId,
                    (cm1, cm2) => new { cm1, cm2 })
                .Where(x => x.cm1.UserId == currentUserId && x.cm2.UserId == targetUser.Id && x.cm1.Conversation != null && x.cm1.Conversation.Type == "personal")
                .Select(x => x.cm1.Conversation)
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
            var conversationIds = await _context.ConversationMembers
                .Where(cm => cm.UserId == userId)
                .Select(cm => cm.ConversationId)
                .ToListAsync();

            var conversations = await _context.Conversations
                .Where(c => conversationIds.Contains(c.Id) && !c.IsDeleted)
                .OrderByDescending(c => c.LastMessageAt)
                .ToListAsync();

            var result = new List<ConversationDto>();
            foreach (var conversation in conversations)
            {
                var dto = await GetConversationDtoAsync(conversation.Id, userId);
                result.Add(dto);
            }

            return result;
        }

        public async Task<ConversationDto> AddMemberAsync(Guid currentUserId, Guid conversationId, string userEmail)
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

            // Find the user to add
            var encryptedEmail = _encryptionService.Encrypt(userEmail);
            var userToAdd = await _context.Users.FirstOrDefaultAsync(u => u.Email == encryptedEmail);
            
            if (userToAdd == null)
            {
                throw new KeyNotFoundException($"User with email {userEmail} not found");
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
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Conversation not found");
            }

            var members = await _context.ConversationMembers
                .Where(cm => cm.ConversationId == conversationId)
                .Include(cm => cm.User)
                .OrderBy(cm => cm.JoinedAt)
                .Select(cm => new ConversationMemberDto
                {
                    UserId = cm.UserId,
                    User = cm.User != null ? new UserDto
                    {
                        Id = cm.User.Id,
                        Email = cm.User.Email ?? string.Empty // Note: This is encrypted email
                    } : new UserDto(),
                    Role = cm.Role,
                    JoinedAt = cm.JoinedAt,
                    IsPinned = cm.IsPinned
                })
                .ToListAsync();

            // Get the current user's member info (for pinned status, role, etc.)
            var currentUserMember = members.FirstOrDefault(m => m.UserId == requestingUserId);

            return new ConversationDto
            {
                Id = conversation.Id,
                Type = conversation.Type,
                Name = conversation.Name,
                CreatedAt = conversation.CreatedAt,
                LastMessageAt = conversation.LastMessageAt,
                IsDeleted = conversation.IsDeleted,
                Members = members
            };
        }
    }
}
