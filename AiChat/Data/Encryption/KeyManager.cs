using System.Security.Cryptography;
using System.Text;
using AiChat.Config;
using Azure.Core;

public interface IEncryptionKeyManager
{
    byte[] GetKey();
    string? Encrypt(string? plaintext);
    string? Decrypt(string? encryptedBase64);
}

/// <summary>
/// Encrypts and decrypts string values stored in the database using AES-256-GCM
/// (Authenticated Encryption with Associated Data).
///
/// AES-GCM vs the previous AES-CBC:
///   • Produces a 128-bit authentication tag alongside the ciphertext. Any
///     modification to the stored bytes — even a single bit — causes Decrypt to
///     throw <see cref="CryptographicException"/> before any plaintext is returned.
///   • Eliminates padding-oracle attacks entirely (GCM is a stream mode; no padding).
///   • Prevents ciphertext bit-flipping attacks.
///
/// Wire format written by Encrypt (Base64 of):
///   [0x02 version (1 byte)] [nonce (12 bytes)] [auth tag (16 bytes)] [ciphertext]
///
/// The version prefix allows Decrypt to fall back to the legacy AES-CBC path
/// for rows that were written before this upgrade, so existing data keeps working
/// and is transparently re-encrypted on the next write.
/// </summary>
public class KeyManager : IEncryptionKeyManager
{
    // Version byte that marks the new AES-GCM format.
    private const byte GcmVersion = 0x02;
    private const int NonceSize = 12;  // 96-bit nonce — GCM standard
    private const int TagSize   = 16;  // 128-bit auth tag — maximum strength

    private readonly KeyVaultConfig _keyVaultConfig;
    private readonly EncryptionConfig _encryptionConfig;
    private readonly TokenCredential? _credential;
    private byte[]? _keyCache;
    private readonly object _lock = new();

    public KeyManager(KeyVaultConfig keyVaultConfig, EncryptionConfig encryptionConfig, TokenCredential? credential)
    {
        _keyVaultConfig = keyVaultConfig;
        _encryptionConfig = encryptionConfig;
        _credential = credential;
    }

    private byte[] Key
    {
        get
        {
            lock (_lock)
            {
                if (_keyCache is not null)
                    return _keyCache;

                if (!string.IsNullOrEmpty(_keyVaultConfig.Url) && !string.IsNullOrEmpty(_encryptionConfig.KeyName))
                {
                    _keyCache = FetchKeyFromKeyVault();
                }
                else
                {
                    throw new InvalidOperationException(
                        "No encryption key configured. Set KeyVault:Url and Encryption:KeyName.");
                }

                return _keyCache;
            }
        }
    }

    private byte[] FetchKeyFromKeyVault()
    {
        if (_credential is null)
            throw new InvalidOperationException("A TokenCredential is required to access Key Vault but none was configured.");

        var secretClient = new Azure.Security.KeyVault.Secrets.SecretClient(
            new Uri(_keyVaultConfig.Url!), _credential);

        var secret = secretClient.GetSecret(_encryptionConfig.KeyName!).Value;
        var key = Convert.FromBase64String(secret.Value);

        // AES-GCM requires a 128, 192, or 256-bit key.
        if (key.Length is not (16 or 24 or 32))
            throw new InvalidOperationException(
                $"Encryption key must be 16, 24, or 32 bytes (got {key.Length}). " +
                "Re-generate the Key Vault secret as a Base64-encoded 32-byte value.");

        return key;
    }

    public byte[] GetKey() => Key;

    // ── AES-256-GCM encrypt ────────────────────────────────────────────────
    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        var key        = Key;
        var nonce      = new byte[NonceSize];
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher     = new byte[plainBytes.Length];
        var tag        = new byte[TagSize];

        RandomNumberGenerator.Fill(nonce);

        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Encrypt(nonce, plainBytes, cipher, tag);

        // Layout: [version 1B][nonce 12B][tag 16B][ciphertext]
        var blob = new byte[1 + NonceSize + TagSize + cipher.Length];
        blob[0] = GcmVersion;
        Buffer.BlockCopy(nonce,  0, blob, 1,                      NonceSize);
        Buffer.BlockCopy(tag,    0, blob, 1 + NonceSize,           TagSize);
        Buffer.BlockCopy(cipher, 0, blob, 1 + NonceSize + TagSize, cipher.Length);

        return Convert.ToBase64String(blob);
    }

    // ── Decrypt — GCM for new rows, CBC fallback for legacy rows ──────────
    public string? Decrypt(string? encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return null;

        var blob = Convert.FromBase64String(encryptedBase64);
        var key  = Key;

        if (blob.Length > 0 && blob[0] == GcmVersion)
            return DecryptGcm(blob, key);

        // Legacy path: data encrypted with AES-CBC before the GCM upgrade.
        return DecryptCbcLegacy(blob, key);
    }

    private static string DecryptGcm(byte[] blob, byte[] key)
    {
        const int minLength = 1 + NonceSize + TagSize;
        if (blob.Length < minLength)
            throw new CryptographicException("AES-GCM ciphertext is too short.");

        var nonce      = blob[1..(1 + NonceSize)];
        var tag        = blob[(1 + NonceSize)..(1 + NonceSize + TagSize)];
        var cipher     = blob[(1 + NonceSize + TagSize)..];
        var plainBytes = new byte[cipher.Length];

        // Throws CryptographicException if the tag does not match —
        // i.e. the data has been tampered with or the key is wrong.
        using var aesGcm = new AesGcm(key, TagSize);
        aesGcm.Decrypt(nonce, cipher, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static string DecryptCbcLegacy(byte[] blob, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;

        var iv = new byte[aes.IV.Length];
        Buffer.BlockCopy(blob, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor  = aes.CreateDecryptor();
        var cipherBytes      = new byte[blob.Length - iv.Length];
        Buffer.BlockCopy(blob, iv.Length, cipherBytes, 0, cipherBytes.Length);
        var plainBytes       = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
