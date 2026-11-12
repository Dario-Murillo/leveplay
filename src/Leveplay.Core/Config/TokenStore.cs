using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Leveplay.Core.Config;

public interface ITokenStore
{
    string? Load();
    void Save(string refreshToken);
    void Clear();
}

/// <summary>
/// Persists a secret (the refresh token, or the Soloist API key) under
/// <c>%APPDATA%\Leveplay</c>, encrypted with DPAPI so that only the current Windows
/// user can read it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiTokenStore : ITokenStore
{
    private readonly string _path;

    public const string RefreshTokenFile = "token.bin";
    public const string EngineApiKeyFile = "engine-key.bin";

    public DpapiTokenStore(string? directory = null, string fileName = RefreshTokenFile)
    {
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Leveplay");
        _path = Path.Combine(directory, fileName);
    }

    public string? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain);
            return token.Length == 0 ? null : token;
        }
        catch (CryptographicException)
        {
            // Written by another user or corrupted: treat as signed out.
            return null;
        }
    }

    public void Save(string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var cipher = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(refreshToken), null, DataProtectionScope.CurrentUser);

        // Write to a temp file first so a crash never leaves a truncated token behind.
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, _path, overwrite: true);
    }

    public void Clear() => File.Delete(_path);
}
