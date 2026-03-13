namespace XcordHub.Infrastructure.Services;

public interface IColdStorageService
{
    Task UploadAsync(string key, Stream content, CancellationToken ct = default);
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    Task<long> GetObjectSizeAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, CancellationToken ct = default);
}
