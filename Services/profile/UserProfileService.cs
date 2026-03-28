using System;
using System.Linq;
using System.Threading.Tasks;
using MessengerServer.Data;
using MessengerServer.Models;
using MessengerServer.Models.DTOs;
using MessengerServer.Services.storage;
using Microsoft.EntityFrameworkCore;

namespace MessengerServer.Services.profile
{
    public class UserProfileService : IUserProfileService
    {
        private const int UploadUrlMinutes = 10;
        private const int DownloadUrlMinutes = 5;

        private readonly AppDbContext _context;
        private readonly IStorageService _storage;

        public UserProfileService(AppDbContext context, IStorageService storage)
        {
            _context = context;
            _storage = storage;
        }

        public async Task<UserProfileDto> GetProfileAsync(Guid requesterId, Guid targetUserId)
        {
            _ = requesterId;
            return await BuildProfileDtoAsync(targetUserId);
        }

        public async Task<UserProfileDto> UpdateMyProfileAsync(Guid userId, UpdateUserProfileDto request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
            {
                throw new KeyNotFoundException("User not found");
            }

            var displayName = request.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("Display name is required");
            }

            if (displayName.Length > 64)
            {
                throw new ArgumentException("Display name is too long");
            }

            var aboutMe = request.AboutMe?.Trim();
            if (!string.IsNullOrWhiteSpace(aboutMe) && aboutMe.Length > 1024)
            {
                throw new ArgumentException("About me is too long");
            }

            var nameTaken = await _context.Users.AnyAsync(u =>
                u.Id != userId &&
                u.DisplayName != null &&
                EF.Functions.ILike(u.DisplayName, displayName));

            if (nameTaken)
            {
                throw new InvalidOperationException("Display name is already taken");
            }

            user.DisplayName = displayName;
            user.AboutMe = aboutMe?.IfBlankAsNull();

            await _context.SaveChangesAsync();

            return await BuildProfileDtoAsync(userId);
        }

        public async Task<InitUserProfilePhotoUploadResponseDto> InitPhotoUploadAsync(Guid userId, InitUserProfilePhotoUploadRequestDto request)
        {
            var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                throw new KeyNotFoundException("User not found");
            }

            var fileName = request.FileName?.Trim();
            var contentType = request.ContentType?.Trim();
            if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(contentType))
            {
                throw new ArgumentException("File name and content type are required");
            }

            if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only image files can be used as profile photos");
            }

            if (request.Size <= 0)
            {
                throw new ArgumentException("File size must be greater than zero");
            }

            var photoId = Guid.NewGuid();
            var objectKey = BuildPhotoObjectKey(userId, photoId, fileName);
            var photo = new UserProfilePhoto
            {
                Id = photoId,
                UserId = userId,
                ObjectKey = objectKey,
                FileName = fileName,
                ContentType = contentType,
                Size = request.Size,
                Status = "Pending",
                IsDeleted = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserProfilePhotos.Add(photo);
            await _context.SaveChangesAsync();

            var expiresIn = TimeSpan.FromMinutes(UploadUrlMinutes);
            var uploadUrl = await _storage.GetUploadUrlAsync(objectKey, contentType, expiresIn);

            return new InitUserProfilePhotoUploadResponseDto
            {
                PhotoId = photoId,
                UploadUrl = uploadUrl,
                ExpiresAt = DateTime.UtcNow.Add(expiresIn)
            };
        }

        public async Task<UserProfileDto> CompletePhotoUploadAsync(Guid userId, CompleteUserProfilePhotoUploadRequestDto request)
        {
            var photo = await _context.UserProfilePhotos
                .FirstOrDefaultAsync(p => p.Id == request.PhotoId && p.UserId == userId && !p.IsDeleted);

            if (photo == null)
            {
                throw new KeyNotFoundException("Profile photo upload not found");
            }

            var exists = await _storage.ExistsAsync(photo.ObjectKey);
            photo.Status = exists ? "Ready" : "Failed";
            await _context.SaveChangesAsync();

            if (!exists)
            {
                throw new InvalidOperationException("Uploaded file was not found in storage");
            }

            return await BuildProfileDtoAsync(userId);
        }

        public async Task<UserProfileDto> DeletePhotoAsync(Guid userId, Guid photoId)
        {
            var photo = await _context.UserProfilePhotos
                .FirstOrDefaultAsync(p => p.Id == photoId && p.UserId == userId && !p.IsDeleted);

            if (photo == null)
            {
                throw new KeyNotFoundException("Profile photo not found");
            }

            photo.IsDeleted = true;
            photo.Status = "Deleted";
            await _context.SaveChangesAsync();

            await _storage.DeleteAsync(photo.ObjectKey);

            return await BuildProfileDtoAsync(userId);
        }

        public async Task<MediaUrlResponseDto> GetPhotoDownloadUrlAsync(Guid requesterId, Guid targetUserId, Guid photoId)
        {
            _ = requesterId;

            var photo = await _context.UserProfilePhotos
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.Id == photoId &&
                    p.UserId == targetUserId &&
                    !p.IsDeleted &&
                    p.Status == "Ready");

            if (photo == null)
            {
                throw new KeyNotFoundException("Profile photo not found");
            }

            var expiresIn = TimeSpan.FromMinutes(DownloadUrlMinutes);
            var url = await _storage.GetDownloadUrlAsync(photo.ObjectKey, expiresIn);

            return new MediaUrlResponseDto
            {
                Url = url,
                ExpiresAt = DateTime.UtcNow.Add(expiresIn)
            };
        }

        private async Task<UserProfileDto> BuildProfileDtoAsync(Guid userId)
        {
            var profile = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new UserProfileDto
                {
                    UserId = u.Id,
                    DisplayName = u.DisplayName ?? string.Empty,
                    AboutMe = u.AboutMe,
                    LatestProfilePhotoId = u.ProfilePhotos
                        .Where(p => !p.IsDeleted && p.Status == "Ready")
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => (Guid?)p.Id)
                        .FirstOrDefault(),
                    Photos = u.ProfilePhotos
                        .Where(p => !p.IsDeleted && p.Status == "Ready")
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => new UserProfilePhotoDto
                        {
                            Id = p.Id,
                            FileName = p.FileName,
                            ContentType = p.ContentType,
                            Size = p.Size,
                            CreatedAt = p.CreatedAt
                        })
                        .ToList()
                })
                .FirstOrDefaultAsync();

            if (profile == null)
            {
                throw new KeyNotFoundException("User not found");
            }

            return profile;
        }

        private static string BuildPhotoObjectKey(Guid userId, Guid photoId, string fileName)
        {
            var safeFileName = SanitizeFileName(fileName);
            return $"users/{userId}/profile/{photoId:N}/{safeFileName}";
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

    internal static class UserProfileStringExtensions
    {
        public static string? IfBlankAsNull(this string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
