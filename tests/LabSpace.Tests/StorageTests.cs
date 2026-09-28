using LabSpace.Storage;
using Xunit;

namespace LabSpace.Tests;

public sealed class StorageTests
{
    [Fact]
    public async Task RecoveryFailureDoesNotOverwriteTheOldPayload()
    {
        var memory = new MemoryProjectStorage(); await memory.WriteRecoveryAsync("broken original recovery");
        var storage = new ProtectedRecoveryStorage(memory); await storage.WriteRecoveryAsync("example project");
        Assert.Equal("broken original recovery", memory.Recovery);
    }
    [Fact]
    public async Task ExplicitSaveUnlocksProtectedRecovery()
    {
        var memory = new MemoryProjectStorage(); await memory.WriteRecoveryAsync("original"); var storage = new ProtectedRecoveryStorage(memory);
        await storage.SaveAsync("new.json", "explicitly saved project"); Assert.Equal("explicitly saved project", memory.Recovery);
        await storage.WriteRecoveryAsync("new edit"); Assert.Equal("new edit", memory.Recovery);
    }
    [Fact]
    public async Task CsvExportDoesNotUnlockProtectedRecovery()
    {
        var memory = new MemoryProjectStorage(); await memory.WriteRecoveryAsync("original"); var storage = new ProtectedRecoveryStorage(memory);
        await storage.ExportAsync("data.csv", "x,y", "text/csv"); await storage.WriteRecoveryAsync("example project"); Assert.Equal("original", memory.Recovery);
    }
}
