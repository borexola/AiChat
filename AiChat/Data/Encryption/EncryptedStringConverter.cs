using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiChat.Data.Encryption;

public class EncryptedStringConverter : ValueConverter<string?, string?>
{
    public EncryptedStringConverter(IEncryptionKeyManager keyManager)
        : base(
            v => keyManager.Encrypt(v),
            v => keyManager.Decrypt(v))
    {
    }
}

public class IntArrayToByteArrayConverter : ValueConverter<int[]?, byte[]?>
{
    public IntArrayToByteArrayConverter()
        : base(
            v => Serialize(v),
            v => Deserialize(v))
    {
    }

    private static byte[]? Serialize(int[]? values)
    {
        if (values is null || values.Length == 0)
            return values is null ? null : [];

        var bytes = new byte[values.Length * sizeof(int)];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static int[]? Deserialize(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return bytes is null ? null : [];

        var values = new int[bytes.Length / sizeof(int)];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        return values;
    }
}

public static class EncryptedStringConverterExtensions
{
    public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string?> HasTypedConversion(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<string?> builder,
        IEncryptionKeyManager keyManager)
    {
        return builder.HasConversion(new EncryptedStringConverter(keyManager));
    }
}
