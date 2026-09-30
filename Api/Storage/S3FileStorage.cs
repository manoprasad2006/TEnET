using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace TenNet.Api.Storage;

public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3Client;
    private readonly StorageOptions _options;
    private readonly ILogger<S3FileStorage> _logger;

    public S3FileStorage(IOptions<StorageOptions> options, ILogger<S3FileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region),
            ForcePathStyle = _options.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
        {
            config.ServiceURL = _options.ServiceUrl;
        }

        if (!string.IsNullOrWhiteSpace(_options.AccessKey) && !string.IsNullOrWhiteSpace(_options.SecretKey))
        {
            _s3Client = new AmazonS3Client(_options.AccessKey, _options.SecretKey, config);
        }
        else
        {
            _s3Client = new AmazonS3Client(config);
        }
    }

    public async Task<FileUploadResult> UploadAsync(
        string key,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var putRequest = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
                AutoCloseStream = false
            };

            _logger.LogInformation("Uploading object to S3: Bucket={Bucket}, Key={Key}, Size={Size}", _options.BucketName, key, stream.Length);
            var response = await _s3Client.PutObjectAsync(putRequest, cancellationToken);
            _logger.LogInformation("Successfully uploaded to S3: Key={Key}, Status={Status}", key, response.HttpStatusCode);
            return new FileUploadResult(key, stream.Length, contentType);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "AWS S3 upload error: [{ErrorCode} - {StatusCode}] {Message}", ex.ErrorCode, ex.StatusCode, ex.Message);
            throw;
        }
    }

    public async Task<FileDownloadResult?> DownloadAsync(
        string key,
        string originalName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var getRequest = new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectAsync(getRequest, cancellationToken);
            return new FileDownloadResult(response.ResponseStream, response.Headers.ContentType, originalName);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = key
            };

            await _s3Client.DeleteObjectAsync(deleteRequest, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task<string> GetPresignedDownloadUrlAsync(
        string key,
        string originalName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if (expiry > TimeSpan.FromMinutes(5))
        {
            expiry = TimeSpan.FromMinutes(5); // Bound to at most 5 minutes
        }

        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiry),
            Verb = HttpVerb.GET,
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(originalName)}\""
            }
        };

        var url = _s3Client.GetPreSignedURL(request);
        return Task.FromResult(url);
    }
}
