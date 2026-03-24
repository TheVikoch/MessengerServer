using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.stream
{
    public class StreamInviteService : IStreamInviteService
    {
        private const int InviteExpiryMinutes = 60;
        private readonly AppDbContext _context;

        public StreamInviteService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<CreateStreamInviteResponseDto> CreateInviteAsync(Guid creatorId, CreateStreamInviteRequestDto request)
        {
            var now = DateTime.UtcNow;
            var (personalChat, targetUserId) = await GetPersonalChatAndTargetAsync(creatorId, request.PersonalChatId);

            var existingInvite = await _context.StreamChatInvites
                .Where(i => i.PersonalChatId == request.PersonalChatId && i.Status == "Pending")
                .Where(i => i.ExpiresAt > now)
                .FirstOrDefaultAsync();

            if (existingInvite != null)
            {
                throw new InvalidOperationException("Active invite already exists");
            }

            var token = GenerateToken();
            var expiresAt = now.AddMinutes(InviteExpiryMinutes);
            var streamChatName = string.IsNullOrWhiteSpace(request.StreamChatName) ? null : request.StreamChatName.Trim();

            var invite = new StreamChatInvite
            {
                Id = Guid.NewGuid(),
                CreatorId = creatorId,
                TargetUserId = targetUserId,
                PersonalChatId = personalChat.Id,
                StreamChatName = streamChatName,
                Token = token,
                Status = "Pending",
                CreatedAt = now,
                ExpiresAt = expiresAt
            };

            _context.StreamChatInvites.Add(invite);
            await _context.SaveChangesAsync();

            return new CreateStreamInviteResponseDto
            {
                InviteId = invite.Id,
                PersonalChatId = invite.PersonalChatId,
                CreatorId = invite.CreatorId,
                TargetUserId = invite.TargetUserId,
                Token = invite.Token,
                StreamChatName = invite.StreamChatName,
                ExpiresAt = invite.ExpiresAt
            };
        }

        public async Task<AcceptStreamInviteResponseDto> AcceptInviteAsync(Guid userId, AcceptStreamInviteRequestDto request)
        {
            var now = DateTime.UtcNow;

            var invite = await _context.StreamChatInvites
                .FirstOrDefaultAsync(i => i.Token == request.Token);

            if (invite == null)
            {
                throw new KeyNotFoundException("Invite not found");
            }

            if (invite.Status != "Pending")
            {
                throw new InvalidOperationException("Invite is not active");
            }

            if (invite.ExpiresAt <= now)
            {
                invite.Status = "Expired";
                await _context.SaveChangesAsync();
                throw new InvalidOperationException("Invite has expired");
            }

            if (invite.TargetUserId != userId)
            {
                throw new UnauthorizedAccessException("Invite is not intended for this user");
            }

            await EnsurePersonalChatStillValid(invite.PersonalChatId, invite.CreatorId, invite.TargetUserId);

            var streamChatId = Guid.NewGuid();
            var streamChat = new Conversation
            {
                Id = streamChatId,
                Type = "stream",
                Name = invite.StreamChatName,
                CreatedAt = now,
                IsDeleted = false
            };

            _context.Conversations.Add(streamChat);

            _context.ConversationMembers.Add(new ConversationMember
            {
                ConversationId = streamChatId,
                UserId = invite.CreatorId,
                Role = "creator",
                JoinedAt = now,
                IsPinned = false
            });

            _context.ConversationMembers.Add(new ConversationMember
            {
                ConversationId = streamChatId,
                UserId = invite.TargetUserId,
                Role = "member",
                JoinedAt = now,
                IsPinned = false
            });

            invite.StreamChatId = streamChatId;
            invite.AcceptedAt = now;
            invite.Status = "Accepted";

            await _context.SaveChangesAsync();

            return new AcceptStreamInviteResponseDto
            {
                InviteId = invite.Id,
                PersonalChatId = invite.PersonalChatId,
                CreatorId = invite.CreatorId,
                TargetUserId = invite.TargetUserId,
                StreamChatId = streamChatId,
                StreamChatName = invite.StreamChatName,
                AcceptedAt = now,
                ExpiresAt = invite.ExpiresAt
            };
        }

        public async Task<StreamChatInvite> RevokeInviteAsync(Guid userId, RevokeStreamInviteRequestDto request)
        {
            var now = DateTime.UtcNow;
            var invite = await _context.StreamChatInvites
                .FirstOrDefaultAsync(i => i.Id == request.InviteId);

            if (invite == null)
            {
                throw new KeyNotFoundException("Invite not found");
            }

            if (invite.CreatorId != userId)
            {
                throw new UnauthorizedAccessException("Only creator can revoke invite");
            }

            if (invite.Status != "Pending")
            {
                throw new InvalidOperationException("Invite is not active");
            }

            if (invite.ExpiresAt <= now)
            {
                invite.Status = "Expired";
                await _context.SaveChangesAsync();
                throw new InvalidOperationException("Invite has expired");
            }

            invite.Status = "Revoked";
            invite.RevokedAt = now;

            await _context.SaveChangesAsync();

            return invite;
        }

        private async Task<(Conversation Conversation, Guid TargetUserId)> GetPersonalChatAndTargetAsync(Guid creatorId, Guid personalChatId)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == personalChatId && !c.IsDeleted);

            if (conversation == null)
            {
                throw new KeyNotFoundException("Personal chat not found");
            }

            if (!string.Equals(conversation.Type, "personal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invite can be created only for personal chats");
            }

            var members = await _context.ConversationMembers
                .Where(cm => cm.ConversationId == personalChatId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            if (!members.Contains(creatorId))
            {
                throw new UnauthorizedAccessException("You are not a member of this chat");
            }

            if (members.Count != 2)
            {
                throw new InvalidOperationException("Personal chat must have exactly two members");
            }

            var targetUserId = members.First(id => id != creatorId);
            return (conversation, targetUserId);
        }

        private async Task EnsurePersonalChatStillValid(Guid personalChatId, Guid creatorId, Guid targetUserId)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == personalChatId && !c.IsDeleted);

            if (conversation == null || !string.Equals(conversation.Type, "personal", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Personal chat is no longer available");
            }

            var members = await _context.ConversationMembers
                .Where(cm => cm.ConversationId == personalChatId)
                .Select(cm => cm.UserId)
                .ToListAsync();

            if (members.Count != 2 || !members.Contains(creatorId) || !members.Contains(targetUserId))
            {
                throw new InvalidOperationException("Personal chat members have changed");
            }
        }

        private static string GenerateToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
