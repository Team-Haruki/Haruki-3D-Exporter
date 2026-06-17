using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PjskBundle2Parts.Services;

public sealed class GltfExtensionInjector
{
    private const uint GlbMagic = 0x46546C67;
    private const uint GlbVersion = 2;
    private const uint JsonChunkType = 0x4E4F534A;
    private const uint BinChunkType = 0x004E4942;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public void InjectRootExtension<T>(
        string inputGlbPath,
        string outputGlbPath,
        string extensionName,
        T extensionPayload
    )
    {
        var glb = File.ReadAllBytes(inputGlbPath);
        var chunks = ReadChunks(glb);
        if (chunks.Count == 0 || chunks[0].Type != JsonChunkType)
        {
            throw new InvalidDataException($"GLB does not start with a JSON chunk: {inputGlbPath}");
        }

        var jsonText = Encoding.UTF8.GetString(chunks[0].Data).TrimEnd('\0', ' ', '\n', '\r', '\t');
        var root = JsonNode.Parse(jsonText)?.AsObject()
            ?? throw new InvalidDataException($"GLB JSON chunk is not an object: {inputGlbPath}");
        var extensionNode = JsonSerializer.SerializeToNode(extensionPayload, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to serialize extension {extensionName}.");

        AddExtensionUsed(root, extensionName);
        var extensions = EnsureObject(root, "extensions");
        extensions[extensionName] = extensionNode;

        var newJson = JsonSerializer.Serialize(root, JsonOptions);
        var newJsonBytes = PadTo4Bytes(Encoding.UTF8.GetBytes(newJson), 0x20);
        var outputChunks = new List<GlbChunk>
        {
            new(JsonChunkType, newJsonBytes),
        };
        outputChunks.AddRange(chunks.Skip(1));

        WriteGlb(outputGlbPath, outputChunks);
    }

    private static void AddExtensionUsed(JsonObject root, string extensionName)
    {
        var array = EnsureArray(root, "extensionsUsed");
        if (!array.OfType<JsonValue>().Any(value =>
                value.TryGetValue<string>(out var text) &&
                string.Equals(text, extensionName, StringComparison.Ordinal)))
        {
            array.Add(extensionName);
        }
    }

    private static JsonObject EnsureObject(JsonObject root, string key)
    {
        if (root.TryGetPropertyValue(key, out var existing) && existing is JsonObject obj)
        {
            return obj;
        }

        obj = new JsonObject();
        root[key] = obj;
        return obj;
    }

    private static JsonArray EnsureArray(JsonObject root, string key)
    {
        if (root.TryGetPropertyValue(key, out var existing) && existing is JsonArray array)
        {
            return array;
        }

        array = new JsonArray();
        root[key] = array;
        return array;
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

    private sealed record GlbChunk(uint Type, byte[] Data);
}
