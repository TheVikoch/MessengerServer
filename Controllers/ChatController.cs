using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.chat;

namespace MessengerServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly IChatService _chatService;

        public ChatController(IChatService chatService)
        {
            _chatService = chatService;
        }

        /// <summary>
        /// Create a personal chat with another user by email
        /// </summary>
        /// <param name="userEmail">Email of the user to chat with</param>
        /// <returns>The created or existing personal chat</returns>
        [HttpPost("personal")]
        public async Task<IActionResult> CreatePersonalChat([FromBody] CreatePersonalChatDto createPersonalChatDto)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (string.IsNullOrWhiteSpace(createPersonalChatDto.UserEmail) && string.IsNullOrWhiteSpace(createPersonalChatDto.UserDisplayName))
                {
                    return BadRequest(new { message = "Укажите email или имя пользователя" });
                }

                var result = await _chatService.CreatePersonalChatAsync(userId, createPersonalChatDto.UserEmail, createPersonalChatDto.UserDisplayName);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Create a group chat
        /// </summary>
        /// <param name="createGroupChatDto">Group chat creation data (name and member emails)</param>
        /// <returns>The created group chat</returns>
        [HttpPost("group")]
        public async Task<IActionResult> CreateGroupChat([FromBody] CreateGroupChatDto createGroupChatDto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _chatService.CreateGroupChatAsync(userId, createGroupChatDto);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Get a specific conversation
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <returns>The conversation details</returns>
        [HttpGet("{conversationId}")]
        public async Task<IActionResult> GetConversation(Guid conversationId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _chatService.GetConversationAsync(userId, conversationId);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Get all conversations for the current user
        /// </summary>
        /// <returns>List of all user's conversations</returns>
        [HttpGet]
        public async Task<IActionResult> GetConversations()
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _chatService.GetConversationsForUserAsync(userId);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Add a member to a group chat by email
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="addMemberDto">User email to add</param>
        /// <returns>Updated conversation details</returns>
        [HttpPost("{conversationId}/members")]
        public async Task<IActionResult> AddMember(Guid conversationId, [FromBody] AddMemberDto addMemberDto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _chatService.AddMemberAsync(userId, conversationId, addMemberDto.UserEmail);
                return Ok(result);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
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

        /// <summary>
        /// Remove a member from a group chat
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="userIdToRemove">User ID to remove</param>
        /// <returns>No content if successful</returns>
        [HttpDelete("{conversationId}/members/{userIdToRemove}")]
        public async Task<IActionResult> RemoveMember(Guid conversationId, Guid userIdToRemove)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _chatService.RemoveMemberAsync(userId, conversationId, userIdToRemove);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
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


