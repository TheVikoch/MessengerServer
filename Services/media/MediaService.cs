using System;
using System.Linq;
using System.Threading.Tasks;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.storage;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;

namespace MessengerServer.Services.media
{
    public class MediaService : IMediaService
    {
        private const int UploadUrlMinutes = 10;
        private const int DownloadUrlMinutes = 5;
        private readonly IMongoCollection<Message> _messages;
        private readonly IMongoCollection<MediaUpload> _uploads;
        private readonly AppDbContext _context;
        private readonly IStorageService _storage;

        public MediaService(IConfiguration configuration, AppDbContext context, IStorageService storage)
        {
            var mongoConnectionString = configuration.GetConnectionString("MongoDb")
                ?? configuration["MongoDb:ConnectionString"]
                ?? "mongodb://localhost:27017/MessengerDB";

            var mongoUrl = new MongoUrl(mongoConnectionString);
            var client = new MongoClient(mongoUrl);
            var database = client.GetDatabase(mongoUrl.DatabaseName ?? "MessengerDB");
            _messages = database.GetCollection<Message>("Messages");
            _uploads = database.GetCollection<MediaUpload>("MediaUploads");

            _context = context;
            _storage = storage;
        }

        public async Task<InitUploadResponseDto> InitUploadAsync(Guid userId, InitUploadRequestDto request)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == request.ConversationId && cm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var attachmentId = Guid.NewGuid().ToString("N");
            var objectKey = BuildObjectKey(request.ConversationId, attachmentId, request.FileName);

            var upload = new MediaUpload
            {
                Id = attachmentId,
                ConversationId = request.ConversationId,
                UserId = userId,
                ObjectKey = objectKey,
                FileName = request.FileName,
                ContentType = request.ContentType,
                Size = request.Size,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            await _uploads.InsertOneAsync(upload);

            var expiresIn = TimeSpan.FromMinutes(UploadUrlMinutes);
            var uploadUrl = await _storage.GetUploadUrlAsync(objectKey, request.ContentType, expiresIn);

            return new InitUploadResponseDto
            {
                AttachmentId = attachmentId,
                UploadUrl = uploadUrl,
                ExpiresAt = DateTime.UtcNow.Add(expiresIn)
            };
        }

        public async Task CompleteUploadAsync(Guid userId, CompleteUploadRequestDto request)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == request.ConversationId && cm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var upload = await _uploads.Find(u => u.Id == request.AttachmentId).FirstOrDefaultAsync();
            if (upload == null || upload.ConversationId != request.ConversationId || upload.UserId != userId)
            {
                throw new KeyNotFoundException("Upload not found");
            }

            var exists = await _storage.ExistsAsync(upload.ObjectKey);
            upload.Status = exists ? "Ready" : "Failed";

            await _uploads.ReplaceOneAsync(u => u.Id == upload.Id, upload);

            if (!exists)
            {
                throw new InvalidOperationException("File not found in storage");
            }
        }

        public async Task<MediaUrlResponseDto> GetDownloadUrlAsync(Guid userId, Guid conversationId, string messageId, string attachmentId)
        {
            var isMember = await _context.ConversationMembers
                .AnyAsync(cm => cm.ConversationId == conversationId && cm.UserId == userId);

            if (!isMember)
            {
                throw new UnauthorizedAccessException("You are not a member of this conversation");
            }

            var message = await _messages.Find(m => m.Id == messageId).FirstOrDefaultAsync();
            if (message == null || message.ConversationId != conversationId)
            {
                throw new KeyNotFoundException("Message not found");
            }

            var attachment = message.Attachments.FirstOrDefault(a => a.Id == attachmentId);
            if (attachment == null)
            {
                throw new KeyNotFoundException("Attachment not found");
            }

            if (!string.Equals(attachment.Status, "Ready", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Attachment is not ready");
            }

            var expiresIn = TimeSpan.FromMinutes(DownloadUrlMinutes);
            var url = await _storage.GetDownloadUrlAsync(attachment.ObjectKey, expiresIn);

            return new MediaUrlResponseDto
            {
                Url = url,
                ExpiresAt = DateTime.UtcNow.Add(expiresIn)
            };
        }

        private static string BuildObjectKey(Guid conversationId, string attachmentId, string fileName)
        {
            var safeFileName = SanitizeFileName(fileName);
            return $"conversations/{conversationId}/uploads/{attachmentId}/{safeFileName}";
        }

        private static string SanitizeFileName(string fileName)
        {
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            return fileName.Trim();
        }
    }
}
