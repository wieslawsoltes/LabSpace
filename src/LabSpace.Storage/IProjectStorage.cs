namespace LabSpace.Storage;

public interface IProjectStorage
{
    Task<string?> OpenAsync(CancellationToken cancellation = default);
    Task SaveAsync(string name, string json, CancellationToken cancellation = default);
    Task<string?> ReadRecoveryAsync(CancellationToken cancellation = default);
    Task WriteRecoveryAsync(string json, CancellationToken cancellation = default);
    Task ExportAsync(string name, string content, string mediaType, CancellationToken cancellation = default);
}

public sealed class MemoryProjectStorage : IProjectStorage
{
    public string? Recovery { get; private set; }
    public string? OpenContent { get; set; }
    public string? LastSaved { get; private set; }
    public Task<string?> OpenAsync(CancellationToken cancellation = default) => Task.FromResult(OpenContent);
    public Task SaveAsync(string name, string json, CancellationToken cancellation = default) { LastSaved = json; return Task.CompletedTask; }
    public Task<string?> ReadRecoveryAsync(CancellationToken cancellation = default) => Task.FromResult(Recovery);
    public Task WriteRecoveryAsync(string json, CancellationToken cancellation = default) { Recovery = json; return Task.CompletedTask; }
    public Task ExportAsync(string name, string content, string mediaType, CancellationToken cancellation = default) { LastSaved = content; return Task.CompletedTask; }
}
