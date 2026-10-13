using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace EnterpriseApim.Core;

/// <summary>
/// Keys look like <c>apim_3f9a1c2b7e4d8a10_Xq9...</c>: a fixed prefix, a 16-char hex key ID
/// (public, used for lookup) and a 256-bit random secret (only its SHA-256 hash is stored).
/// </summary>
public static class ApiKeyFormat
{
    public const string Prefix = "apim";
    public const int KeyIdLength = 16;

    public static (string PlainKey, string KeyId, byte[] SecretHash) Generate()
    {
        // Hex never contains '_', so the key always splits unambiguously into three parts.
        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyIdLength / 2)).ToLowerInvariant();
        var secret = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        return ($"{Prefix}_{keyId}_{secret}", keyId, HashSecret(secret));
    }

    /// <summary>
    /// SHA-256 is appropriate here (unlike for passwords): the secret has 256 bits of entropy,
    /// so brute force is infeasible, and a slow hash would add latency to every request.
    /// </summary>
    public static byte[] HashSecret(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public static bool TryParse(string? key, out string keyId, out string secret)
    {
        keyId = secret = "";
        var parts = key?.Trim().Split('_', 3);
        if (parts is not { Length: 3 } || parts[0] != Prefix) return false;
        if (parts[1].Length != KeyIdLength || parts[2].Length == 0) return false;
        (keyId, secret) = (parts[1], parts[2]);
        return true;
    }
}
