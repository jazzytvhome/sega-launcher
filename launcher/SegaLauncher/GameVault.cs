using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SegaLauncher;

/// <summary>
/// Decrypts game.enc in memory and exposes the game files as a path → bytes map.
/// Plaintext never touches the disk. Format must match vercel-app/scripts/lib/pack.mjs:
///   blob      = nonce(12) | tag(16) | ciphertext            (AES-256-GCM)
///   container = "SEGA1" | count(u32 LE) | [ pathLen(u16 LE) | path(utf8) | compLen(u32 LE) | deflateRaw(content) ]*
/// </summary>
public sealed class GameVault
{
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    private GameVault(Dictionary<string, byte[]> files) => Files = files;

    public static GameVault Open(byte[] blob, byte[] key)
    {
        if (key.Length != 32) throw new ArgumentException("key must be 32 bytes");
        if (blob.Length < 28) throw new InvalidDataException("blob too small");

        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var ciphertext = blob.AsSpan(28);
        var container = new byte[ciphertext.Length];

        using (var aes = new AesGcm(key, 16))
            aes.Decrypt(nonce, ciphertext, tag, container); // throws if tag/key wrong

        return new GameVault(ParseContainer(container));
    }

    private static Dictionary<string, byte[]> ParseContainer(byte[] data)
    {
        if (data.Length < 9 || Encoding.ASCII.GetString(data, 0, 5) != "SEGA1")
            throw new InvalidDataException("bad container magic");

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        int p = 5;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4)); p += 4;

        for (uint i = 0; i < count; i++)
        {
            ushort pathLen = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2)); p += 2;
            string path = Encoding.UTF8.GetString(data, p, pathLen); p += pathLen;
            uint compLen = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4)); p += 4;
            var comp = data.AsSpan(p, (int)compLen).ToArray(); p += (int)compLen;
            files[path] = Inflate(comp);
        }
        return files;
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
