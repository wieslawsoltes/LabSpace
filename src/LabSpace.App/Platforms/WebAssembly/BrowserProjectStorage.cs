using System.Runtime.InteropServices.JavaScript;
using LabSpace.Storage;

namespace LabSpace.App;

internal sealed class BrowserProjectStorage : IProjectStorage
{
    public async Task<string?> OpenAsync(CancellationToken cancellation = default) { cancellation.ThrowIfCancellationRequested(); var json = await BrowserFiles.Open(); return string.IsNullOrEmpty(json) ? null : json; }
    public async Task SaveAsync(string name, string json, CancellationToken cancellation = default) { cancellation.ThrowIfCancellationRequested(); await BrowserFiles.Download(name, json, "application/json"); }
    public async Task ExportAsync(string name, string content, string mediaType, CancellationToken cancellation = default) { cancellation.ThrowIfCancellationRequested(); await BrowserFiles.Download(name, content, mediaType); }
    public async Task<string?> ReadRecoveryAsync(CancellationToken cancellation = default) { cancellation.ThrowIfCancellationRequested(); return await BrowserFiles.Load(); }
    public async Task WriteRecoveryAsync(string json, CancellationToken cancellation = default) { cancellation.ThrowIfCancellationRequested(); await BrowserFiles.Save(json); }
}
internal static partial class BrowserFiles
{
    [JSImport("globalThis.labSpaceStorage.load")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Load();
    [JSImport("globalThis.labSpaceStorage.save")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Save(string json);
    [JSImport("globalThis.labSpaceStorage.open")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Open();
    [JSImport("globalThis.labSpaceStorage.download")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Download(string name, string content, string contentType);
    [JSImport("globalThis.labSpaceStorage.isTestMode")]
    internal static partial bool IsTestMode();
    [JSImport("globalThis.labSpaceStorage.publishDiagnostics")]
    internal static partial void PublishDiagnostics(string json);
}
