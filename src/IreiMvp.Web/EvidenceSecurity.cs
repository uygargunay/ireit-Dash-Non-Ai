using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace IreiMvp.Web;

public sealed class EvidenceSecurity(IOptions<EvidenceOptions> options, IWebHostEnvironment env)
{
    public EvidenceOptions Options => options.Value;
    private byte[] Key => Convert.FromBase64String(Options.EncryptionKey);
    public bool LocalHttp(HttpContext context) => Options.Prototype && Options.AllowLocalHttp && env.IsDevelopment() &&
        context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) &&
        (context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1");
    public byte[] Encrypt(byte[] plain, string purpose)
    {
        var result = new byte[29 + plain.Length]; result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 12));
        using var aes = new AesGcm(Key, 16);
        aes.Encrypt(result.AsSpan(1, 12), plain, result.AsSpan(29), result.AsSpan(13, 16), Encoding.UTF8.GetBytes(purpose));
        return result;
    }
    public byte[] Decrypt(byte[] encrypted, string purpose)
    {
        if (encrypted.Length < 29 || encrypted[0] != 1) throw new CryptographicException("Invalid protected data.");
        var result = new byte[encrypted.Length - 29]; using var aes = new AesGcm(Key, 16);
        aes.Decrypt(encrypted.AsSpan(1, 12), encrypted.AsSpan(29), encrypted.AsSpan(13, 16), result, Encoding.UTF8.GetBytes(purpose)); return result;
    }
    public string Protect<T>(T value, string purpose) => WebEncoders.Base64UrlEncode(Encrypt(JsonSerializer.SerializeToUtf8Bytes(value), purpose));
    public T? Unprotect<T>(string? value, string purpose)
    {
        try { return value is null ? default : JsonSerializer.Deserialize<T>(Decrypt(WebEncoders.Base64UrlDecode(value), purpose)); }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException) { return default; }
    }
    public static string Token() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    public static string Password(string password)
    {
        if (password.Length is < 14 or > 256) throw new InvalidDataException("Use a password between 14 and 256 characters.");
        var salt = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32));
    }
    public static bool PasswordMatches(string password, string hash)
    {
        if (password.Length > 256) return false;
        var parts = hash.Split(':');
        return parts.Length == 2 && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(parts[1]),
            Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), 210000, HashAlgorithmName.SHA256, 32));
    }
    public static long TotpCounter(string secret, string code)
    {
        if (code.Length != 6 || !code.All(char.IsAsciiDigit)) return -1;
        byte[] key;
        try
        {
            var bytes = new List<byte>(); var buffer = 0; var bits = 0;
            foreach (var c in secret.TrimEnd('=').ToUpperInvariant())
            { var n = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c); if (n < 0) return -1; buffer = (buffer << 5) | n; bits += 5;
                if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); } }
            key = bytes.ToArray(); if (key.Length < 20) return -1;
        }
        catch { return -1; }
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> value = stackalloc byte[8];
        for (var step = counter - 1; step <= counter + 1; step++)
        {
            BinaryPrimitives.WriteInt64BigEndian(value, step);
            var mac = HMACSHA1.HashData(key, value); var offset = mac[^1] & 15;
            var number = (BinaryPrimitives.ReadInt32BigEndian(mac.AsSpan(offset, 4)) & 0x7fffffff) % 1000000;
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(number.ToString("D6", CultureInfo.InvariantCulture)), Encoding.ASCII.GetBytes(code))) return step;
        }
        return -1;
    }
    public EvidenceSession? Session(HttpContext c) => Unprotect<EvidenceSession>(c.Request.Cookies["irei-evidence"], "session") is { } session && session.Expires > DateTimeOffset.UtcNow ? session : null;
    public string Preauth(HttpContext c)
    {
        var token = Token(); Set(c, "irei-evidence-csrf", Protect(new EvidenceSession("", 0, DateTimeOffset.UtcNow.AddMinutes(10), token), "csrf"), 10); return token;
    }
    public void SignIn(HttpContext c, EvidenceUser u)
    { Set(c, "irei-evidence", Protect(new EvidenceSession(u.Id, u.SecurityVersion, DateTimeOffset.UtcNow.AddMinutes(30), Token()), "session"), 30); }
    private void Set(HttpContext c, string name, string value, int minutes) => c.Response.Cookies.Append(name, value,
        new CookieOptions { HttpOnly = true, Secure = !LocalHttp(c), SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromMinutes(minutes), IsEssential = true });
    public bool ValidCsrf(HttpContext c)
    {
        var expected = Session(c)?.Csrf;
        var pre = Unprotect<EvidenceSession>(c.Request.Cookies["irei-evidence-csrf"], "csrf");
        if (expected is null && pre?.Expires > DateTimeOffset.UtcNow) expected = pre.Csrf;
        var given = c.Request.Headers["X-CSRF-Token"].FirstOrDefault();
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(given));
    }
}
