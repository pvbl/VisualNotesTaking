using System.Security.Cryptography;
using System.Text;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.Security;

/// <summary>Stores one DPAPI-protected binary value per purpose, outside application data and backups.</summary>
public sealed class WindowsDpapiCredentialStore : IApiCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VisualNotes.ApiCredentials.v1");
    private readonly string _directory;

    public WindowsDpapiCredentialStore(string? directory = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The Windows credential store requires Windows.");
        _directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualNotesCredentials");
    }

    public async Task SaveAsync(ApiCredentialProfile profile, string credential, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        Directory.CreateDirectory(_directory);
        var plaintext = Encoding.UTF8.GetBytes(credential);
        try
        {
            var protectedValue = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            var temporary = Path.Combine(_directory, $".{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(temporary, protectedValue, cancellationToken);
            File.Move(temporary, PathFor(profile), true);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public Task<bool> ExistsAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(PathFor(profile)));

    public async Task<string?> GetAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default)
    {
        var path = PathFor(profile);
        if (!File.Exists(path)) return null;
        var encrypted = await File.ReadAllBytesAsync(path, cancellationToken);
        var plaintext = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public async Task<bool> VerifyAsync(ApiCredentialProfile profile, string candidate, CancellationToken cancellationToken = default)
    {
        var stored = await GetAsync(profile, cancellationToken);
        if (stored is null) return false;
        var left = Encoding.UTF8.GetBytes(stored);
        var right = Encoding.UTF8.GetBytes(candidate ?? string.Empty);
        try { return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right); }
        finally { CryptographicOperations.ZeroMemory(left); CryptographicOperations.ZeroMemory(right); }
    }

    public Task DeleteAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default)
    {
        File.Delete(PathFor(profile));
        return Task.CompletedTask;
    }

    public async Task<string?> GetMaskedAsync(ApiCredentialProfile profile, CancellationToken cancellationToken = default) =>
        await ExistsAsync(profile, cancellationToken) ? "••••••••" : null;

    private string PathFor(ApiCredentialProfile profile) => Path.Combine(_directory, $"{profile.ToString().ToLowerInvariant()}.credential");
}
