using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace Handshake.Desktop.Services;

public sealed class SecureSettings
{
    private readonly string _file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Handshake", "openai.key");

    public bool HasApiKey => File.Exists(_file);

    public void SaveApiKey(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_file, encrypted);
    }

    public string GetApiKey()
    {
        if (!File.Exists(_file)) return "";
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_file), null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
