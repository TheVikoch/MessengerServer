using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.chat;
using MessengerServer.Services.messages;
using MessengerServer.Services.websocket;

namespace MessengerServer.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class MessagesController : ControllerBase
    {
        private readonly IMessageService _messageService;
        private readonly IChatService _chatService;
        private readonly IWebSocketNotifier _webSocketNotifier;

        public MessagesController(
            IMessageService messageService,
            IChatService chatService,
            IWebSocketNotifier webSocketNotifier)
        {
            _messageService = messageService;
            _chatService = chatService;
            _webSocketNotifier = webSocketNotifier;
        }

        /// <summary>
        /// Send a message to a conversation
        /// </summary>
        /// <param name="sendMessageDto">Message data (conversationId, content optional, attachmentIds optional)</param>
        /// <returns>The sent message with decrypted content</returns>
        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageDto sendMessageDto)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (IsEmptyMessage(sendMessageDto))
                {
                    return BadRequest(new { message = "Message content or attachments are required" });
                }

                var shouldNotifyConversationCreated = !await _chatService.HasMessagesAsync(userId, sendMessageDto.ConversationId);

                var result = await _messageService.SendMessageAsync(userId, sendMessageDto);

                if (shouldNotifyConversationCreated)
                {
                    var updatedConversation = await _chatService.GetConversationAsync(userId, sendMessageDto.ConversationId);
                    await _webSocketNotifier.NotifyConversationCreatedAsync(updatedConversation, userId);
                }

                await _webSocketNotifier.NotifyNewMessageAsync(
                    sendMessageDto.ConversationId,
                    result,
                    userId
                );

                return Ok(result);
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

        [HttpPut("{conversationId}/{messageId}")]
        public async Task<IActionResult> UpdateMessage(Guid conversationId, string messageId, [FromBody] UpdateMessageDto updateMessageDto)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _messageService.UpdateMessageAsync(userId, conversationId, messageId, updateMessageDto);
                await _webSocketNotifier.NotifyMessageUpdatedAsync(conversationId, result, userId);
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
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
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

        [HttpDelete("{conversationId}/{messageId}/me")]
        public async Task<IActionResult> DeleteMessageForMe(Guid conversationId, string messageId)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _messageService.DeleteMessageForUserAsync(userId, conversationId, messageId);
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
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{conversationId}/{messageId}/everyone")]
        public async Task<IActionResult> DeleteMessageForEveryone(Guid conversationId, string messageId)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _messageService.DeleteMessageForEveryoneAsync(userId, conversationId, messageId);
                await _webSocketNotifier.NotifyMessageDeletedAsync(conversationId, messageId, userId, deletedForEveryone: true);
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

        /// <summary>
        /// Get messages from a conversation (with pagination)
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="limit">Number of messages to retrieve (default 50, max 100)</param>
        /// <param name="cursor">ISO 8601 date string for pagination (gets messages older than this)</param>
        /// <returns>List of messages with optional hasMore and nextCursor</returns>
        [HttpGet("{conversationId}")]
        public async Task<IActionResult> GetMessages(
            Guid conversationId,
            [FromQuery] int limit = 50,
            [FromQuery] string? cursor = null)
        {
            try
            {
                var userId = GetCurrentUserId();

                if (limit <= 0 || limit > 100)
                {
                    return BadRequest(new { message = "Limit must be between 1 and 100" });
                }

                var result = await _messageService.GetMessagesAsync(userId, conversationId, limit, cursor);
                return Ok(result);
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
        /// Get attachments from the full conversation history (with pagination).
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="limit">Number of message pages with attachments to retrieve (default 100, max 100)</param>
        /// <param name="cursor">ISO 8601 date string for pagination (gets older messages with attachments)</param>
        [HttpGet("{conversationId}/attachments")]
        public async Task<IActionResult> GetConversationAttachments(
            Guid conversationId,
            [FromQuery] int limit = 100,
            [FromQuery] string? cursor = null)
        {
            try
            {
                var userId = GetCurrentUserId();

                if (limit <= 0 || limit > 100)
                {
                    return BadRequest(new { message = "Limit must be between 1 and 100" });
                }

                var result = await _messageService.GetConversationAttachmentsAsync(userId, conversationId, limit, cursor);
                return Ok(result);
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
        /// Get unread message count for a conversation
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <returns>Number of unread messages</returns>
        [HttpGet("{conversationId}/unread-count")]
        public async Task<IActionResult> GetUnreadCount(Guid conversationId)
        {
            try
            {
                var userId = GetCurrentUserId();
                var result = await _messageService.GetUnreadCountAsync(userId, conversationId);
                return Ok(new { count = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Mark messages as read up to a specific message
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="messageId">Message ID to mark as read (all messages up to this one will be considered read)</param>
        [HttpPost("{conversationId}/read/{messageId}")]
        public async Task<IActionResult> MarkAsRead(Guid conversationId, string messageId)
        {
            try
            {
                var userId = GetCurrentUserId();
                await _messageService.MarkAsReadAsync(userId, conversationId, messageId);
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

        private static bool IsEmptyMessage(SendMessageDto dto)
        {
            return string.IsNullOrWhiteSpace(dto.Content) && (dto.AttachmentIds == null || dto.AttachmentIds.Count == 0);
        }
    }
}

