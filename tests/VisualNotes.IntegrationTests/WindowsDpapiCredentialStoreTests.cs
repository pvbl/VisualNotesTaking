using Shouldly;

using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Security;

using Xunit;

namespace VisualNotes.IntegrationTests;

public sealed class WindowsDpapiCredentialStoreTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task Windows_store_protects_reads_and_deletes_credentials()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "visualnotes-credentials-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WindowsDpapiCredentialStore(directory);
            const string secret = "dpapi-integration-secret-value";
            await store.SaveAsync(ApiCredentialProfile.Extraction, secret);

            (await store.GetAsync(ApiCredentialProfile.Extraction)).ShouldBe(secret);
            (await store.VerifyAsync(ApiCredentialProfile.Extraction, secret)).ShouldBeTrue();
            var persisted = await File.ReadAllBytesAsync(Directory.EnumerateFiles(directory).Single());
            System.Text.Encoding.UTF8.GetString(persisted).ShouldNotContain(secret);

            await store.DeleteAsync(ApiCredentialProfile.Extraction);
            (await store.GetAsync(ApiCredentialProfile.Extraction)).ShouldBeNull();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
