using System;
using System.Threading.Tasks;

namespace MessengerServer.Services.storage
{
    public interface IStorageService
    {
        Task<string> GetUploadUrlAsync(string objectKey, string contentType, TimeSpan expiresIn);
        Task<string> GetDownloadUrlAsync(string objectKey, TimeSpan expiresIn);
        Task<bool> ExistsAsync(string objectKey);
    }
}
