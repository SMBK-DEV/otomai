using System.Security.Cryptography;
using System.Text;

namespace NYROX.Desktop.Control;

internal static class ControlTokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NYROX.Control.Api.v1");

    public static string GetOrCreate()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NYROX");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "control.token");

        if (File.Exists(path))
        {
            try
            {
                var protectedBytes = File.ReadAllBytes(path);
                return Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                File.Delete(path);
            }
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
        return token;
    }
}
