using LabSpace.Storage;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace LabSpace.App;

internal sealed class DesktopProjectStorage : IProjectStorage
{
    private static string RecoveryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LabSpace", "recovery.labspace.json");
    public async Task<string?> OpenAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary }; picker.FileTypeFilter.Add(".json");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync(); if (properties.Size > 8 * 1024 * 1024) throw new InvalidDataException("Project exceeds the 8 MiB limit.");
        return await FileIO.ReadTextAsync(file);
    }
    public Task SaveAsync(string name, string json, CancellationToken cancellation = default) => ExportAsync(name, json, "application/json", cancellation);
    public async Task ExportAsync(string name, string content, string mediaType, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = name };
        picker.FileTypeChoices.Add(mediaType == "text/csv" ? "CSV data" : "LabSpace project", new List<string> { mediaType == "text/csv" ? ".csv" : ".json" });
        var file = await picker.PickSaveFileAsync(); if (file is null) throw new OperationCanceledException("Save was canceled."); await FileIO.WriteTextAsync(file, content);
    }
    public async Task<string?> ReadRecoveryAsync(CancellationToken cancellation = default)
    {
        if (!File.Exists(RecoveryPath)) return null;
        if (new FileInfo(RecoveryPath).Length > 8 * 1024 * 1024) throw new InvalidDataException("Recovery exceeds the 8 MiB limit.");
        return await File.ReadAllTextAsync(RecoveryPath, cancellation);
    }
    public async Task WriteRecoveryAsync(string json, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath)!); var temporary = RecoveryPath + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellation); File.Move(temporary, RecoveryPath, true);
    }
}
