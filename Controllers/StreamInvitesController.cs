using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.messages;
using MessengerServer.Services.stream;
using MessengerServer.Services.websocket;

namespace MessengerServer.Controllers
{
    [ApiController]
    [Route("api/stream-invites")]
    [Authorize]
    public class StreamInvitesController : ControllerBase
    {
        private readonly IStreamInviteService _streamInviteService;
        private readonly IMessageService _messageService;
        private readonly IWebSocketNotifier _webSocketNotifier;

        public StreamInvitesController(
            IStreamInviteService streamInviteService,
            IMessageService messageService,
            IWebSocketNotifier webSocketNotifier)
        {
            _streamInviteService = streamInviteService;
            _messageService = messageService;
            _webSocketNotifier = webSocketNotifier;
        }

        [HttpPost]
        public async Task<IActionResult> CreateInvite([FromBody] CreateStreamInviteRequestDto request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _streamInviteService.CreateInviteAsync(userId, request);
                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("accept")]
        public async Task<IActionResult> AcceptInvite([FromBody] AcceptStreamInviteRequestDto request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _streamInviteService.AcceptInviteAsync(userId, request);

                var metadata = new StreamInviteMetadataDto
                {
                    InviteId = result.InviteId,
                    PersonalChatId = result.PersonalChatId,
                    CreatorId = result.CreatorId,
                    TargetUserId = result.TargetUserId,
                    StreamChatId = result.StreamChatId,
                    Status = "accepted",
                    ExpiresAt = result.ExpiresAt,
                    AcceptedAt = result.AcceptedAt
                };

                var metadataJson = JsonSerializer.Serialize(metadata);
                var message = await _messageService.SendSystemMessageAsync(
                    userId,
                    result.PersonalChatId,
                    "stream_invite_accepted",
                    "Инвайт принят. Чат для файлов создан.",
                    metadataJson);

                await _webSocketNotifier.NotifyNewMessageAsync(result.PersonalChatId, message, userId);

                return Ok(result);
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("revoke")]
        public async Task<IActionResult> RevokeInvite([FromBody] RevokeStreamInviteRequestDto request)
        {
            try
            {
                var userId = GetCurrentUserId();
                var invite = await _streamInviteService.RevokeInviteAsync(userId, request);

                var metadata = new StreamInviteMetadataDto
                {
                    InviteId = invite.Id,
                    PersonalChatId = invite.PersonalChatId,
                    CreatorId = invite.CreatorId,
                    TargetUserId = invite.TargetUserId,
                    StreamChatId = invite.StreamChatId,
                    Status = "revoked",
                    ExpiresAt = invite.ExpiresAt,
                    RevokedAt = invite.RevokedAt
                };

                var metadataJson = JsonSerializer.Serialize(metadata);
                var message = await _messageService.SendSystemMessageAsync(
                    userId,
                    invite.PersonalChatId,
                    "stream_invite_revoked",
                    "Инвайт отозван.",
                    metadataJson);

                await _webSocketNotifier.NotifyNewMessageAsync(invite.PersonalChatId, message, userId);

                return NoContent();
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        private Guid GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)
                ?? User.FindFirst("sub")
                ?? User.FindFirst("userId");

            if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            {
                throw new UnauthorizedAccessException("Invalid user token");
            }

            return userId;
        }
    }
}
