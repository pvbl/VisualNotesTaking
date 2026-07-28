using System.Security.Cryptography;

using Shouldly;

using VisualNotes.Core.Services;

using Xunit;

namespace VisualNotes.UnitTests;

public sealed class ApiCredentialStoreContractTests
{
    [Fact]
    [Trait("Category", "Unit")]
    public async Task Separate_profiles_support_save_replace_verify_mask_and_delete()
    {
        IApiCredentialStore store = new FakeCredentialStore();

        await store.SaveAsync(ApiCredentialProfile.Extraction, "extract-test-token");
        await store.SaveAsync(ApiCredentialProfile.Composition, "compose-test-token");
        (await store.GetMaskedAsync(ApiCredentialProfile.Extraction)).ShouldBe("••••••••");
        (await store.VerifyAsync(ApiCredentialProfile.Extraction, "extract-test-token")).ShouldBeTrue();
        (await store.VerifyAsync(ApiCredentialProfile.Extraction, "compose-test-token")).ShouldBeFalse();

        await store.SaveAsync(ApiCredentialProfile.Extraction, "replacement-test-token");
        (await store.VerifyAsync(ApiCredentialProfile.Extraction, "extract-test-token")).ShouldBeFalse();
        (await store.VerifyAsync(ApiCredentialProfile.Extraction, "replacement-test-token")).ShouldBeTrue();
        await store.DeleteAsync(ApiCredentialProfile.Extraction);
        (await store.ExistsAsync(ApiCredentialProfile.Extraction)).ShouldBeFalse();
        (await store.ExistsAsync(ApiCredentialProfile.Composition)).ShouldBeTrue();
    }

    private sealed class FakeCredentialStore : IApiCredentialStore
    {
        private readonly Dictionary<ApiCredentialProfile, string> _values = [];
        public Task SaveAsync(ApiCredentialProfile profile, string credential, CancellationToken cancellationToken = default) { _values[profile] = credential; return Task.CompletedTask; }
        public Task<bool> ExistsAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(_values.ContainsKey(profile));
        public Task<string?> GetAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(_values.GetValueOrDefault(profile));
        public Task<bool> VerifyAsync(ApiCredentialProfile profile, string candidate, CancellationToken cancellationToken = default)
        {
            var matches = _values.TryGetValue(profile, out var value) && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(value), System.Text.Encoding.UTF8.GetBytes(candidate));
            return Task.FromResult(matches);
        }
        public Task DeleteAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) { _values.Remove(profile); return Task.CompletedTask; }
        public Task<string?> GetMaskedAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) => Task.FromResult<string?>(_values.ContainsKey(profile) ? "••••••••" : null);
    }
}
