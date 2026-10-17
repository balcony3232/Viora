using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Viora;

public static class LocalAccountService
{
    private const int Iterations = 210_000;
    private const int HashBytes = 32;

    public static bool Exists => File.Exists(Storage.AccountPath);

    public static string Register(string username, string password)
    {
        username = username.Trim();
        if (!Regex.IsMatch(username, @"^[a-zA-Z0-9_]{3,24}$"))
            throw new ArgumentException("Use 3–24 letters, numbers or underscores for your username.");
        if (password.Length < 8 || password.Length > 256)
            throw new ArgumentException("Choose a password of 8–256 characters.");
        if (Exists) throw new InvalidOperationException("A local account already exists on this PC.");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        var account = new LocalAccount
        {
            Username = username,
            PasswordSaltBase64 = Convert.ToBase64String(salt),
            PasswordHashBase64 = Convert.ToBase64String(hash)
        };
        Storage.WriteJson(Storage.AccountPath, account);
        return username;
    }

    public static string? Authenticate(string username, string password)
    {
        if (!Exists) return null;
        try
        {
            var account = JsonSerializer.Deserialize<LocalAccount>(File.ReadAllText(Storage.AccountPath), Storage.JsonOptions);
            if (account is null || !string.Equals(account.Username, username.Trim(), StringComparison.Ordinal)) return null;
            var salt = Convert.FromBase64String(account.PasswordSaltBase64);
            var expected = Convert.FromBase64String(account.PasswordHashBase64);
            if (salt.Length != 16 || expected.Length != HashBytes) return null;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
            return CryptographicOperations.FixedTimeEquals(actual, expected) ? account.Username : null;
        }
        catch (Exception) { return null; }
    }

    public static string? AutoSignInUsername()
    {
        try
        {
            if (!Exists) return null;
            var account = JsonSerializer.Deserialize<LocalAccount>(File.ReadAllText(Storage.AccountPath), Storage.JsonOptions);
            return account?.StaySignedIn == true ? account.Username : null;
        }
        catch { return null; }
    }

    public static void SetStaySignedIn(bool enabled)
    {
        if (!Exists) return;
        var account = JsonSerializer.Deserialize<LocalAccount>(File.ReadAllText(Storage.AccountPath), Storage.JsonOptions)
            ?? throw new InvalidDataException("The local account file is invalid.");
        account.StaySignedIn = enabled;
        Storage.WriteJson(Storage.AccountPath, account);
    }

    public static string? ExistingUsername()
    {
        try
        {
            if (!Exists) return null;
            return JsonSerializer.Deserialize<LocalAccount>(File.ReadAllText(Storage.AccountPath), Storage.JsonOptions)?.Username;
        }
        catch (Exception) { return null; }
    }
}
