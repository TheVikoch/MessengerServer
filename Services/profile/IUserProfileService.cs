using System;
using System.Threading.Tasks;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.profile
{
    public interface IUserProfileService
    {
        Task<UserProfileDto> GetProfileAsync(Guid requesterId, Guid targetUserId);
        Task<UserProfileDto> UpdateMyProfileAsync(Guid userId, UpdateUserProfileDto request);
        Task<InitUserProfilePhotoUploadResponseDto> InitPhotoUploadAsync(Guid userId, InitUserProfilePhotoUploadRequestDto request);
        Task<UserProfileDto> CompletePhotoUploadAsync(Guid userId, CompleteUserProfilePhotoUploadRequestDto request);
        Task<UserProfileDto> DeletePhotoAsync(Guid userId, Guid photoId);
        Task<MediaUrlResponseDto> GetPhotoDownloadUrlAsync(Guid requesterId, Guid targetUserId, Guid photoId);
    }
}
