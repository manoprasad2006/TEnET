using System.Collections.Concurrent;

namespace TenNet.Api.Storage;

public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string ContentType)> _storage = new();

    public Task<FileUploadResult> UploadAsync(
        string key,
        Stream stream,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        _storage[key] = (bytes, contentType);
        return Task.FromResult(new FileUploadResult(key, bytes.Length, contentType));
    }

    public Task<FileDownloadResult?> DownloadAsync(
        string key,
        string originalName,
        CancellationToken cancellationToken = default)
    {
        if (!_storage.TryGetValue(key, out var entry))
        {
            return Task.FromResult<FileDownloadResult?>(null);
        }

        var stream = new MemoryStream(entry.Data);
        return Task.FromResult<FileDownloadResult?>(new FileDownloadResult(stream, entry.ContentType, originalName));
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var removed = _storage.TryRemove(key, out _);
        return Task.FromResult(removed);
    }

    public Task<string> GetPresignedDownloadUrlAsync(
        string key,
        string originalName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult($"/api/files/download-direct?key={Uri.EscapeDataString(key)}&name={Uri.EscapeDataString(originalName)}");
    }
}
