using System;
using System.Threading.Tasks;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.media
{
    public interface IMediaService
    {
        Task<InitUploadResponseDto> InitUploadAsync(Guid userId, InitUploadRequestDto request);
        Task CompleteUploadAsync(Guid userId, CompleteUploadRequestDto request);
        Task<MediaUrlResponseDto> GetDownloadUrlAsync(Guid userId, Guid conversationId, string messageId, string attachmentId);
    }
}
