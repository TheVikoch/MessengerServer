using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.chat
{
    public interface IChatService
    {
        Task<ConversationDto> CreatePersonalChatAsync(Guid currentUserId, string? userEmail, string? userDisplayName);
        Task<ConversationDto> CreateGroupChatAsync(Guid currentUserId, CreateGroupChatDto createGroupChatDto);
        Task<List<UserSearchResultDto>> SearchUsersAsync(Guid currentUserId, string query, int limit);
        Task<InitConversationAvatarUploadResponseDto> InitConversationAvatarUploadAsync(Guid currentUserId, Guid conversationId, InitConversationAvatarUploadRequestDto request);
        Task<ConversationDto> CompleteConversationAvatarUploadAsync(Guid currentUserId, Guid conversationId, CompleteConversationAvatarUploadRequestDto request);
        Task<MediaUrlResponseDto> GetConversationAvatarUrlAsync(Guid currentUserId, Guid conversationId, Guid avatarPhotoId);
        Task<bool> HasMessagesAsync(Guid currentUserId, Guid conversationId);
        Task<ConversationDto> GetConversationAsync(Guid userId, Guid conversationId);
        Task<List<ConversationDto>> GetConversationsForUserAsync(Guid userId);
        Task DeleteConversationForUserAsync(Guid currentUserId, Guid conversationId);
        Task<List<Guid>> DeletePersonalConversationForEveryoneAsync(Guid currentUserId, Guid conversationId);
        Task<ConversationDto> AddMemberAsync(Guid currentUserId, Guid conversationId, string? userEmail, string? userDisplayName);
        Task RemoveMemberAsync(Guid currentUserId, Guid conversationId, Guid userIdToRemove);
    }
}
