namespace LabSpace.Storage;

/// <summary>Prevents a failed recovery import from being overwritten by an automatic save of example content.
/// An explicit successful project save is required before recovery writes resume.</summary>
public sealed class ProtectedRecoveryStorage(IProjectStorage inner) : IProjectStorage
{
    private bool _enabled;
    public Task<string?> OpenAsync(CancellationToken cancellation = default) => inner.OpenAsync(cancellation);
    public async Task SaveAsync(string name, string json, CancellationToken cancellation = default)
    {
        await inner.SaveAsync(name, json, cancellation);
        await inner.WriteRecoveryAsync(json, cancellation);
        _enabled = true;
    }
    public Task<string?> ReadRecoveryAsync(CancellationToken cancellation = default) => inner.ReadRecoveryAsync(cancellation);
    public Task WriteRecoveryAsync(string json, CancellationToken cancellation = default) => _enabled ? inner.WriteRecoveryAsync(json, cancellation) : Task.CompletedTask;
    public Task ExportAsync(string name, string content, string mediaType, CancellationToken cancellation = default) => inner.ExportAsync(name, content, mediaType, cancellation);
}
