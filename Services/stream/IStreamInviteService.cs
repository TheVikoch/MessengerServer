using System;
using System.Threading.Tasks;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;

namespace MessengerServer.Services.stream
{
    public interface IStreamInviteService
    {
        Task<CreateStreamInviteResponseDto> CreateInviteAsync(Guid creatorId, CreateStreamInviteRequestDto request);
        Task<AcceptStreamInviteResponseDto> AcceptInviteAsync(Guid userId, AcceptStreamInviteRequestDto request);
        Task<StreamChatInvite> RevokeInviteAsync(Guid userId, RevokeStreamInviteRequestDto request);
    }
}
