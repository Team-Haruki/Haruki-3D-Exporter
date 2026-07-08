using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PjskBundle2Parts.Services;

public static class RuntimeJsonWriter
{
    public const string MessagePackBrotli = "msgpack-br";
    public const string Gzip = "gzip";
    public const string Json = "json";
    public const string Both = "both";

    public static bool IsValidMode(string value)
    {
        return NormalizeMode(value) is MessagePackBrotli or Gzip or Json or Both;
    }

    public static string NormalizeMode(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "br" or "msgpack" or "messagepack-br" ? MessagePackBrotli : normalized;
    }

    public static string PrimaryPath(string jsonPath, string mode)
    {
        return NormalizeMode(mode) switch
        {
            Json => jsonPath,
            MessagePackBrotli => MessagePackBrotliPath(jsonPath),
            _ => GzipPath(jsonPath),
        };
    }

    public static bool OutputsExist(string jsonPath, string mode)
    {
        return NormalizeMode(mode) switch
        {
            Json => File.Exists(jsonPath),
            Both => File.Exists(jsonPath) && File.Exists(GzipPath(jsonPath)),
            MessagePackBrotli => File.Exists(MessagePackBrotliPath(jsonPath)),
            _ => File.Exists(GzipPath(jsonPath)),
        };
    }

    public static void Write<T>(
        string jsonPath,
        T value,
        JsonSerializerOptions options,
        string mode,
        CompressionLevel? brotliCompressionLevel = null
    )
    {
        var normalizedMode = NormalizeMode(mode);
        var parent = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, options);
        if (normalizedMode is Json or Both)
        {
            File.WriteAllBytes(jsonPath, bytes);
        }
        if (normalizedMode is Gzip or Both)
        {
            using var file = File.Create(GzipPath(jsonPath));
            using var gzip = new GZipStream(file, CompressionLevel.Optimal);
            gzip.Write(bytes);
        }
        if (normalizedMode == MessagePackBrotli)
        {
            WriteMessagePackBrotli(jsonPath, bytes, brotliCompressionLevel ?? CompressionLevel.SmallestSize);
        }
    }

    public static JsonObject ReadJsonObject(string jsonPath)
    {
        return JsonNode.Parse(ReadJsonBytes(jsonPath))?.AsObject()
            ?? throw new InvalidOperationException($"Runtime JSON is empty: {jsonPath}");
    }

    public static JsonDocument ReadJsonDocument(string jsonPath, string mode)
    {
        return JsonDocument.Parse(ReadJsonBytes(PrimaryPath(jsonPath, mode)));
    }

    public static string GzipPath(string jsonPath)
    {
        return jsonPath + ".gz";
    }

    public static string MessagePackBrotliPath(string jsonPath)
    {
        return jsonPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? jsonPath[..^".json".Length] + ".msgpack.br"
            : jsonPath + ".msgpack.br";
    }

    private static byte[] ReadJsonBytes(string path)
    {
        if (path.EndsWith(".msgpack.br", StringComparison.OrdinalIgnoreCase))
        {
            return DecodeMessagePackBrotli(File.ReadAllBytes(path));
        }
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            using var input = File.OpenRead(path);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        if (File.Exists(MessagePackBrotliPath(path)))
        {
            return DecodeMessagePackBrotli(File.ReadAllBytes(MessagePackBrotliPath(path)));
        }
        if (File.Exists(GzipPath(path)))
        {
            using var input = File.OpenRead(GzipPath(path));
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        return File.ReadAllBytes(path);
    }

    private static void WriteMessagePackBrotli(string jsonPath, byte[] jsonBytes, CompressionLevel compressionLevel)
    {
        using var document = JsonDocument.Parse(jsonBytes);
        using var packed = new MemoryStream();
        WriteMessagePackValue(packed, document.RootElement);
        using var compressed = new MemoryStream();
        using (var brotli = new BrotliStream(compressed, compressionLevel, leaveOpen: true))
        {
            brotli.Write(packed.ToArray());
        }
        File.WriteAllBytes(MessagePackBrotliPath(jsonPath), compressed.ToArray());
    }

    private static byte[] DecodeMessagePackBrotli(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var packed = new MemoryStream();
        brotli.CopyTo(packed);
        var data = packed.ToArray();
        var offset = 0;
        using var json = new MemoryStream();
        using (var writer = new Utf8JsonWriter(json))
        {
            ReadMessagePackValue(data, ref offset, writer);
        }
        return json.ToArray();
    }

    private static void WriteMessagePackValue(Stream stream, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().ToArray();
                WriteMapHeader(stream, properties.Length);
                foreach (var property in properties)
                {
                    WriteString(stream, property.Name);
                    WriteMessagePackValue(stream, property.Value);
                }
                break;
            case JsonValueKind.Array:
                var items = value.EnumerateArray().ToArray();
                WriteArrayHeader(stream, items.Length);
                foreach (var item in items)
                {
                    WriteMessagePackValue(stream, item);
                }
                break;
            case JsonValueKind.String:
                WriteString(stream, value.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Number:
                WriteNumber(stream, value);
                break;
            case JsonValueKind.True:
                stream.WriteByte(0xc3);
                break;
            case JsonValueKind.False:
                stream.WriteByte(0xc2);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                stream.WriteByte(0xc0);
                break;
            default:
                throw new NotSupportedException($"Unsupported JSON value kind: {value.ValueKind}");
        }
    }

    private static void WriteMapHeader(Stream stream, int count)
    {
        if (count <= 15)
        {
            stream.WriteByte((byte)(0x80 | count));
        }
        else if (count <= ushort.MaxValue)
        {
            stream.WriteByte(0xde);
            WriteUInt16(stream, (ushort)count);
        }
        else
        {
            stream.WriteByte(0xdf);
            WriteUInt32(stream, (uint)count);
        }
    }

    private static void WriteArrayHeader(Stream stream, int count)
    {
        if (count <= 15)
        {
            stream.WriteByte((byte)(0x90 | count));
        }
        else if (count <= ushort.MaxValue)
        {
            stream.WriteByte(0xdc);
            WriteUInt16(stream, (ushort)count);
        }
        else
        {
            stream.WriteByte(0xdd);
            WriteUInt32(stream, (uint)count);
        }
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= 31)
        {
            stream.WriteByte((byte)(0xa0 | bytes.Length));
        }
        else if (bytes.Length <= byte.MaxValue)
        {
            stream.WriteByte(0xd9);
            stream.WriteByte((byte)bytes.Length);
        }
        else if (bytes.Length <= ushort.MaxValue)
        {
            stream.WriteByte(0xda);
            WriteUInt16(stream, (ushort)bytes.Length);
        }
        else
        {
            stream.WriteByte(0xdb);
            WriteUInt32(stream, (uint)bytes.Length);
        }
        stream.Write(bytes);
    }

    private static void WriteNumber(Stream stream, JsonElement value)
    {
        if (value.TryGetInt64(out var integer))
        {
            if (integer >= 0)
            {
                WriteUnsignedNumber(stream, (ulong)integer);
            }
            else if (integer >= -32)
            {
                stream.WriteByte((byte)integer);
            }
            else if (integer >= sbyte.MinValue)
            {
                stream.WriteByte(0xd0);
                stream.WriteByte((byte)(sbyte)integer);
            }
            else if (integer >= short.MinValue)
            {
                stream.WriteByte(0xd1);
                WriteInt16(stream, (short)integer);
            }
            else if (integer >= int.MinValue)
            {
                stream.WriteByte(0xd2);
                WriteInt32(stream, (int)integer);
            }
            else
            {
                WriteFloat64(stream, integer);
            }
            return;
        }

        if (value.TryGetUInt64(out var unsigned))
        {
            if (unsigned <= uint.MaxValue)
            {
                WriteUnsignedNumber(stream, unsigned);
            }
            else
            {
                WriteFloat64(stream, unsigned);
            }
            return;
        }

        WriteFloat64(stream, value.GetDouble());
    }

    private static void WriteUnsignedNumber(Stream stream, ulong value)
    {
        if (value <= 127)
        {
            stream.WriteByte((byte)value);
        }
        else if (value <= byte.MaxValue)
        {
            stream.WriteByte(0xcc);
            stream.WriteByte((byte)value);
        }
        else if (value <= ushort.MaxValue)
        {
            stream.WriteByte(0xcd);
            WriteUInt16(stream, (ushort)value);
        }
        else
        {
            stream.WriteByte(0xce);
            WriteUInt32(stream, (uint)value);
        }
    }

    private static void ReadMessagePackValue(ReadOnlySpan<byte> data, ref int offset, Utf8JsonWriter writer)
    {
        var code = data[offset++];
        if (code <= 0x7f)
        {
            writer.WriteNumberValue(code);
            return;
        }
        if (code >= 0x80 && code <= 0x8f)
        {
            ReadMap(data, ref offset, writer, code & 0x0f);
            return;
        }
        if (code >= 0x90 && code <= 0x9f)
        {
            ReadArray(data, ref offset, writer, code & 0x0f);
            return;
        }
        if (code >= 0xa0 && code <= 0xbf)
        {
            writer.WriteStringValue(ReadString(data, ref offset, code & 0x1f));
            return;
        }
        if (code >= 0xe0)
        {
            writer.WriteNumberValue(unchecked((sbyte)code));
            return;
        }

        switch (code)
        {
            case 0xc0:
                writer.WriteNullValue();
                break;
            case 0xc2:
                writer.WriteBooleanValue(false);
                break;
            case 0xc3:
                writer.WriteBooleanValue(true);
                break;
            case 0xcc:
                writer.WriteNumberValue(data[offset++]);
                break;
            case 0xcd:
                writer.WriteNumberValue(ReadUInt16(data, ref offset));
                break;
            case 0xce:
                writer.WriteNumberValue(ReadUInt32(data, ref offset));
                break;
            case 0xcf:
                writer.WriteNumberValue(ReadUInt64(data, ref offset));
                break;
            case 0xd0:
                writer.WriteNumberValue(unchecked((sbyte)data[offset++]));
                break;
            case 0xd1:
                writer.WriteNumberValue(ReadInt16(data, ref offset));
                break;
            case 0xd2:
                writer.WriteNumberValue(ReadInt32(data, ref offset));
                break;
            case 0xd3:
                writer.WriteNumberValue(ReadInt64(data, ref offset));
                break;
            case 0xcb:
                writer.WriteNumberValue(ReadFloat64(data, ref offset));
                break;
            case 0xd9:
                writer.WriteStringValue(ReadString(data, ref offset, data[offset++]));
                break;
            case 0xda:
                writer.WriteStringValue(ReadString(data, ref offset, ReadUInt16(data, ref offset)));
                break;
            case 0xdb:
                writer.WriteStringValue(ReadString(data, ref offset, checked((int)ReadUInt32(data, ref offset))));
                break;
            case 0xdc:
                ReadArray(data, ref offset, writer, ReadUInt16(data, ref offset));
                break;
            case 0xdd:
                ReadArray(data, ref offset, writer, checked((int)ReadUInt32(data, ref offset)));
                break;
            case 0xde:
                ReadMap(data, ref offset, writer, ReadUInt16(data, ref offset));
                break;
            case 0xdf:
                ReadMap(data, ref offset, writer, checked((int)ReadUInt32(data, ref offset)));
                break;
            default:
                throw new NotSupportedException($"Unsupported MessagePack code 0x{code:x2}.");
        }
    }

    private static void ReadArray(ReadOnlySpan<byte> data, ref int offset, Utf8JsonWriter writer, int count)
    {
        writer.WriteStartArray();
        for (var i = 0; i < count; i++)
        {
            ReadMessagePackValue(data, ref offset, writer);
        }
        writer.WriteEndArray();
    }

    private static void ReadMap(ReadOnlySpan<byte> data, ref int offset, Utf8JsonWriter writer, int count)
    {
        writer.WriteStartObject();
        for (var i = 0; i < count; i++)
        {
            var key = ReadMessagePackKey(data, ref offset);
            writer.WritePropertyName(key);
            ReadMessagePackValue(data, ref offset, writer);
        }
        writer.WriteEndObject();
    }

    private static string ReadMessagePackKey(ReadOnlySpan<byte> data, ref int offset)
    {
        var code = data[offset++];
        if (code >= 0xa0 && code <= 0xbf)
        {
            return ReadString(data, ref offset, code & 0x1f);
        }
        return code switch
        {
            0xd9 => ReadString(data, ref offset, data[offset++]),
            0xda => ReadString(data, ref offset, ReadUInt16(data, ref offset)),
            0xdb => ReadString(data, ref offset, checked((int)ReadUInt32(data, ref offset))),
            _ => throw new NotSupportedException($"Unsupported MessagePack map key code 0x{code:x2}."),
        };
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        var value = Encoding.UTF8.GetString(data.Slice(offset, length));
        offset += length;
        return value;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt16(Stream stream, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteFloat64(Stream stream, double value)
    {
        stream.WriteByte(0xcb);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static short ReadInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(offset, 8));
        offset += 8;
        return value;
    }

    private static long ReadInt64(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadInt64BigEndian(data.Slice(offset, 8));
        offset += 8;
        return value;
    }

    private static double ReadFloat64(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadDoubleBigEndian(data.Slice(offset, 8));
        offset += 8;
        return value;
    }
}
