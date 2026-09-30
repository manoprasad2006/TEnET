namespace TenNet.Api.Storage;

public sealed record FileUploadResult(string StoredKey, long Size, string ContentType);

public sealed record FileDownloadResult(Stream ContentStream, string ContentType, string OriginalName);

public interface IFileStorage
{
    Task<FileUploadResult> UploadAsync(
        string key,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<FileDownloadResult?> DownloadAsync(
        string key,
        string originalName,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<string> GetPresignedDownloadUrlAsync(
        string key,
        string originalName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);
}
