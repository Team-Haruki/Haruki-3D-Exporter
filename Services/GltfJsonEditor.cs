using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PjskBundle2Parts.Services;

internal static class GltfJsonEditor
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    public static GltfDocument ReadDocument(byte[] glb)
    {
        var chunks = ReadChunks(glb);
        if (chunks.Count == 0 || chunks[0].Type != JsonChunkType)
        {
            throw new InvalidDataException("GLB does not start with a JSON chunk.");
        }

        var jsonText = Encoding.UTF8.GetString(chunks[0].Data).TrimEnd('\0', ' ', '\n', '\r', '\t');
        var root = JsonNode.Parse(jsonText)?.AsObject()
            ?? throw new InvalidDataException("GLB JSON chunk is not an object.");
        var bin = chunks.FirstOrDefault(chunk => chunk.Type == BinChunkType)?.Data ?? Array.Empty<byte>();
        return new GltfDocument(root, bin);
    }

    public static JsonObject ReadJsonObject(byte[] glb)
    {
        return ReadDocument(glb).Root;
    }

    public static void WriteJsonToGlb(
        byte[] sourceGlb,
        JsonObject root,
        string outputGlbPath
    )
    {
        var chunks = ReadChunks(sourceGlb);
        WriteDocumentToGlb(root, chunks.FirstOrDefault(chunk => chunk.Type == BinChunkType)?.Data, outputGlbPath);
    }

    public static void WriteDocumentToGlb(
        JsonObject root,
        byte[]? binChunk,
        string outputGlbPath
    )
    {
        var newJson = JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = false,
        });
        var newJsonBytes = PadTo4Bytes(Encoding.UTF8.GetBytes(newJson), 0x20);
        var outputChunks = new List<GlbChunk>
        {
            new(JsonChunkType, newJsonBytes),
        };
        if (binChunk is { Length: > 0 })
        {
            outputChunks.Add(new GlbChunk(BinChunkType, PadTo4Bytes(binChunk, 0x00)));
        }
        WriteGlb(outputGlbPath, outputChunks);
    }

    private static IReadOnlyList<GlbChunk> ReadChunks(byte[] glb)
    {
        if (glb.Length < 12)
        {
            throw new InvalidDataException("GLB is too small.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(0, 4));
        var version = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4, 4));
        var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(8, 4));
        if (magic != GlbMagic || version != GlbVersion)
        {
            throw new InvalidDataException("Only GLB 2.0 files are supported.");
        }
        if (declaredLength != glb.Length)
        {
            throw new InvalidDataException(
                $"GLB declared length {declaredLength} does not match file length {glb.Length}."
            );
        }

        var chunks = new List<GlbChunk>();
        var offset = 12;
        while (offset < glb.Length)
        {
            if (offset + 8 > glb.Length)
            {
                throw new InvalidDataException("Truncated GLB chunk header.");
            }

            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(offset, 4));
            var chunkType = BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(offset + 4, 4));
            offset += 8;

            if (offset + chunkLength > glb.Length)
            {
                throw new InvalidDataException("Truncated GLB chunk payload.");
            }

            chunks.Add(new GlbChunk(
                Type: chunkType,
                Data: glb.AsSpan(offset, checked((int)chunkLength)).ToArray()
            ));
            offset += checked((int)chunkLength);
        }

        return chunks;
    }

    private static void WriteGlb(
        string outputGlbPath,
        IReadOnlyList<GlbChunk> chunks
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputGlbPath) ?? ".");
        var totalLength = 12 + chunks.Sum(chunk => 8 + chunk.Data.Length);
        using var stream = File.Create(outputGlbPath);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], GlbMagic);
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..8], GlbVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..12], checked((uint)totalLength));
        stream.Write(header);

        Span<byte> chunkHeader = stackalloc byte[8];
        foreach (var chunk in chunks)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[0..4], checked((uint)chunk.Data.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(chunkHeader[4..8], chunk.Type);
            stream.Write(chunkHeader);
            stream.Write(chunk.Data);
        }
    }

    private static byte[] PadTo4Bytes(byte[] data, byte pad)
    {
        var paddedLength = (data.Length + 3) & ~3;
        if (paddedLength == data.Length)
        {
            return data;
        }

        var padded = new byte[paddedLength];
        data.CopyTo(padded, 0);
        Array.Fill(padded, pad, data.Length, paddedLength - data.Length);
        return padded;
    }

    public sealed record GltfDocument(JsonObject Root, byte[] BinaryChunk);

    private sealed record GlbChunk(uint Type, byte[] Data);
}
