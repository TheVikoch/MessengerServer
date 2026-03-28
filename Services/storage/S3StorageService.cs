using System;
using System.Net;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace MessengerServer.Services.storage
{
    public class S3StorageService : IStorageService
    {
        private readonly IAmazonS3 _s3;
        private readonly S3Options _options;

        public S3StorageService(IOptions<S3Options> options)
        {
            _options = options.Value;

            var config = new AmazonS3Config
            {
                ServiceURL = _options.ServiceURL,
                ForcePathStyle = true,
                SignatureVersion = "4",
                AuthenticationRegion = _options.Region
            };

            _s3 = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
        }

        public Task<string> GetUploadUrlAsync(string objectKey, string contentType, TimeSpan expiresIn)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                Verb = HttpVerb.PUT,
                Expires = DateTime.UtcNow.Add(expiresIn)
            };

            var url = _s3.GetPreSignedURL(request);
            return Task.FromResult(url);
        }

        public Task<string> GetDownloadUrlAsync(string objectKey, TimeSpan expiresIn)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(expiresIn)
            };

            var url = _s3.GetPreSignedURL(request);
            return Task.FromResult(url);
        }

        public async Task<bool> ExistsAsync(string objectKey)
        {
            try
            {
                var response = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = objectKey
                });

                return response.HttpStatusCode == HttpStatusCode.OK;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public async Task DeleteAsync(string objectKey)
        {
            await _s3.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey
            });
        }
    }
}
