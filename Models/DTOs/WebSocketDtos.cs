using System;
using System.Collections.Generic;

namespace MessengerServer.Models.DTOs
{
    /// <summary>
    /// Базовый класс для всех WebSocket событий
    /// </summary>
    public class WebSocketEventDto
    {
        public string Type { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public Guid? UserId { get; set; }
    }

    /// <summary>
    /// Событие нового сообщения
    /// </summary>
    public class NewMessageEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public MessageDto Message { get; set; } = new MessageDto();
        
        public NewMessageEventDto()
        {
            Type = "new_message";
        }
    }

    /// <summary>
    /// Событие прочтения сообщения
    /// </summary>
    public class MessageReadEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public Guid ReadByUserId { get; set; }
        
        public MessageReadEventDto()
        {
            Type = "message_read";
        }
    }

    /// <summary>
    /// Событие добавления участника в беседу
    /// </summary>
    public class MemberAddedEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public UserDto AddedUser { get; set; } = new UserDto();
        public Guid AddedByUserId { get; set; }
        
        public MemberAddedEventDto()
        {
            Type = "member_added";
        }
    }

    /// <summary>
    /// Событие удаления участника из беседы
    /// </summary>
    public class MemberRemovedEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public UserDto RemovedUser { get; set; } = new UserDto();
        public Guid RemovedByUserId { get; set; }
        
        public MemberRemovedEventDto()
        {
            Type = "member_removed";
        }
    }

    /// <summary>
    /// Событие создания беседы
    /// </summary>
    public class ConversationCreatedEventDto : WebSocketEventDto
    {
        public ConversationDto Conversation { get; set; } = new ConversationDto();
        
        public ConversationCreatedEventDto()
        {
            Type = "conversation_created";
        }
    }

    /// <summary>
    /// Событие печати (typing indicator)
    /// </summary>
    public class TypingEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public bool IsTyping { get; set; }
        public string? UserName { get; set; }
        
        public TypingEventDto()
        {
            Type = "typing";
        }
    }

    public class MessageUpdatedEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public MessageDto Message { get; set; } = new MessageDto();

        public MessageUpdatedEventDto()
        {
            Type = "message_updated";
        }
    }

    public class MessageDeletedEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public string MessageId { get; set; } = string.Empty;
        public bool DeletedForEveryone { get; set; }

        public MessageDeletedEventDto()
        {
            Type = "message_deleted";
        }
    }

    public class ConversationDeletedEventDto : WebSocketEventDto
    {
        public Guid ConversationId { get; set; }
        public bool DeletedForEveryone { get; set; }

        public ConversationDeletedEventDto()
        {
            Type = "conversation_deleted";
        }
    }
}
