using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Services;

public enum RuntimeBinaryArraySchema
{
    None,
    PartRuntime,
    UnityMotion,
}

public static class RuntimeJsonWriter
{
    public const byte BinaryArrayExtensionType = 42;
    public const int DefaultBrotliQuality = 6;
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
        CompressionLevel? brotliCompressionLevel = null,
        RuntimeBinaryArraySchema binaryArraySchema = RuntimeBinaryArraySchema.None
    )
    {
        var normalizedMode = NormalizeMode(mode);
        var parent = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        byte[]? bytes = null;
        if (normalizedMode is Json or Both)
        {
            bytes ??= JsonSerializer.SerializeToUtf8Bytes(value, options);
            WriteAllBytesAtomic(jsonPath, bytes);
        }
        if (normalizedMode is Gzip or Both)
        {
            bytes ??= JsonSerializer.SerializeToUtf8Bytes(value, options);
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(bytes);
            }
            WriteAllBytesAtomic(GzipPath(jsonPath), compressed.ToArray());
        }
        if (normalizedMode == MessagePackBrotli)
        {
            WriteMessagePackBrotli(
                jsonPath,
                value,
                options,
                brotliCompressionLevel is null
                    ? DefaultBrotliQuality
                    : BrotliQuality(brotliCompressionLevel.Value),
                binaryArraySchema
            );
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

    private static void WriteMessagePackBrotli<T>(
        string jsonPath,
        T value,
        JsonSerializerOptions options,
        int quality,
        RuntimeBinaryArraySchema binaryArraySchema
    )
    {
        using var packed = new MemoryStream();
        WriteMessagePackObject(packed, value, options, binaryArraySchema, string.Empty);
        var input = packed.GetBuffer().AsSpan(0, checked((int)packed.Length));
        var compressed = new byte[BrotliEncoder.GetMaxCompressedLength(input.Length)];
        if (!BrotliEncoder.TryCompress(input, compressed, out var bytesWritten, quality, window: 22))
        {
            throw new InvalidOperationException("Brotli failed to compress runtime MessagePack.");
        }
        WriteAllBytesAtomic(MessagePackBrotliPath(jsonPath), compressed.AsSpan(0, bytesWritten).ToArray());
    }

    private static int BrotliQuality(CompressionLevel level)
    {
        return level switch
        {
            CompressionLevel.Fastest => 1,
            CompressionLevel.SmallestSize => 11,
            CompressionLevel.NoCompression => 0,
            _ => 4,
        };
    }

    private static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath);
        }
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

    private static readonly HashSet<string> PartRuntimeFloat32ArrayPaths = new(StringComparer.Ordinal)
    {
        "nativeMeshes.meshes.positions",
        "nativeMeshes.meshes.normals",
        "nativeMeshes.meshes.colors",
        "nativeMeshes.meshes.uv0",
        "nativeMeshes.meshes.uv1",
        "nativeMeshes.meshes.skinWeights",
        "nativeMeshes.meshes.boneInverseBindMatrices",
        "nativeMeshes.meshes.morphTargets.positionDeltas",
        "nativeMeshes.meshes.morphTargets.normalDeltas",
    };

    private static readonly HashSet<string> PartRuntimeUnsignedIndexArrayPaths = new(StringComparer.Ordinal)
    {
        "nativeMeshes.meshes.skinIndices",
        "nativeMeshes.meshes.submeshes.indices",
        "nativeMeshes.meshes.morphTargets.indices",
    };

    private static readonly HashSet<string> UnityMotionFloat32ArrayPaths = new(StringComparer.Ordinal)
    {
        "clips.tracks.times",
        "clips.tracks.values",
    };

    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> SerializableProperties = new();

    private static void WriteMessagePackObject(
        Stream stream,
        object? value,
        JsonSerializerOptions options,
        RuntimeBinaryArraySchema binaryArraySchema,
        string path
    )
    {
        if (value is null)
        {
            stream.WriteByte(0xc0);
            return;
        }

        if (value is JsonElement element)
        {
            WriteMessagePackValue(stream, element, binaryArraySchema, path);
            return;
        }
        if (value is JsonNode node)
        {
            using var document = JsonDocument.Parse(node.ToJsonString(options));
            WriteMessagePackValue(stream, document.RootElement, binaryArraySchema, path);
            return;
        }
        if (value is string text)
        {
            WriteString(stream, text);
            return;
        }
        if (value is bool boolean)
        {
            stream.WriteByte(boolean ? (byte)0xc3 : (byte)0xc2);
            return;
        }
        if (value is byte[] bytes)
        {
            WriteString(stream, Convert.ToBase64String(bytes));
            return;
        }
        if (TryWriteNumber(stream, value))
        {
            return;
        }
        if (value is Enum enumValue)
        {
            if (options.Converters.Any(converter => converter is JsonStringEnumConverter))
            {
                WriteString(stream, enumValue.ToString());
            }
            else
            {
                WriteSignedNumber(stream, Convert.ToInt64(enumValue));
            }
            return;
        }
        if (TryWriteDictionary(stream, value, options, binaryArraySchema, path))
        {
            return;
        }
        if (value is IEnumerable enumerable)
        {
            if (TryWriteBinaryArray(stream, enumerable, binaryArraySchema, path))
            {
                return;
            }
            var items = enumerable.Cast<object?>().ToList();
            WriteArrayHeader(stream, items.Count);
            foreach (var item in items)
            {
                WriteMessagePackObject(stream, item, options, binaryArraySchema, path);
            }
            return;
        }

        var properties = SerializableProperties.GetOrAdd(
            value.GetType(),
            type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToArray()
        );
        var selected = properties
            .Select(property => (Property: property, Value: property.GetValue(value)))
            .Where(item => ShouldWriteProperty(item.Property, item.Value, options))
            .ToList();
        WriteMapHeader(stream, selected.Count);
        foreach (var item in selected)
        {
            var propertyName = item.Property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? options.PropertyNamingPolicy?.ConvertName(item.Property.Name)
                ?? item.Property.Name;
            WriteString(stream, propertyName);
            var childPath = path.Length == 0 ? propertyName : $"{path}.{propertyName}";
            WriteMessagePackObject(stream, item.Value, options, binaryArraySchema, childPath);
        }
    }

    private static bool ShouldWriteProperty(
        PropertyInfo property,
        object? value,
        JsonSerializerOptions options
    )
    {
        var ignore = property.GetCustomAttribute<JsonIgnoreAttribute>();
        var condition = ignore?.Condition ?? options.DefaultIgnoreCondition;
        if (ignore is not null && condition == JsonIgnoreCondition.Always)
        {
            return false;
        }
        if (condition == JsonIgnoreCondition.WhenWritingNull && value is null)
        {
            return false;
        }
        if (condition == JsonIgnoreCondition.WhenWritingDefault && IsDefaultValue(value, property.PropertyType))
        {
            return false;
        }
        return true;
    }

    private static bool IsDefaultValue(object? value, Type type)
    {
        if (value is null)
        {
            return true;
        }
        return type.IsValueType && value.Equals(Activator.CreateInstance(type));
    }

    private static bool TryWriteDictionary(
        Stream stream,
        object value,
        JsonSerializerOptions options,
        RuntimeBinaryArraySchema binaryArraySchema,
        string path
    )
    {
        if (value is IDictionary dictionary)
        {
            WriteMapHeader(stream, dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = Convert.ToString(entry.Key) ?? string.Empty;
                WriteString(stream, key);
                var childPath = path.Length == 0 ? key : $"{path}.{key}";
                WriteMessagePackObject(stream, entry.Value, options, binaryArraySchema, childPath);
            }
            return true;
        }

        var dictionaryInterface = value.GetType().GetInterfaces()
            .FirstOrDefault(type =>
                type.IsGenericType &&
                type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>) &&
                type.GetGenericArguments()[0] == typeof(string));
        if (dictionaryInterface is null || value is not IEnumerable entries)
        {
            return false;
        }
        var items = entries.Cast<object>().ToList();
        WriteMapHeader(stream, items.Count);
        foreach (var item in items)
        {
            var itemType = item.GetType();
            var key = (string?)itemType.GetProperty("Key")?.GetValue(item) ?? string.Empty;
            var itemValue = itemType.GetProperty("Value")?.GetValue(item);
            WriteString(stream, key);
            var childPath = path.Length == 0 ? key : $"{path}.{key}";
            WriteMessagePackObject(stream, itemValue, options, binaryArraySchema, childPath);
        }
        return true;
    }

    private static bool TryWriteBinaryArray(
        Stream stream,
        IEnumerable values,
        RuntimeBinaryArraySchema binaryArraySchema,
        string path
    )
    {
        var isFloat32Array = binaryArraySchema switch
        {
            RuntimeBinaryArraySchema.PartRuntime => PartRuntimeFloat32ArrayPaths.Contains(path),
            RuntimeBinaryArraySchema.UnityMotion => UnityMotionFloat32ArrayPaths.Contains(path),
            _ => false,
        };
        if (isFloat32Array)
        {
            var numbers = values.Cast<object?>().Select(Convert.ToSingle).ToArray();
            if (numbers.Length < 8 || numbers.Any(number => !float.IsFinite(number)))
            {
                return false;
            }
            var payload = new byte[1 + numbers.Length * sizeof(float)];
            payload[0] = 1;
            for (var index = 0; index < numbers.Length; index += 1)
            {
                BinaryPrimitives.WriteSingleLittleEndian(
                    payload.AsSpan(1 + index * sizeof(float), sizeof(float)),
                    numbers[index]
                );
            }
            WriteExtension(stream, payload);
            return true;
        }

        if (binaryArraySchema != RuntimeBinaryArraySchema.PartRuntime ||
            !PartRuntimeUnsignedIndexArrayPaths.Contains(path))
        {
            return false;
        }
        var indexes = values.Cast<object?>().Select(Convert.ToUInt32).ToArray();
        if (indexes.Length < 16)
        {
            return false;
        }
        var useUInt16 = indexes.All(number => number <= ushort.MaxValue);
        var width = useUInt16 ? sizeof(ushort) : sizeof(uint);
        var integerPayload = new byte[1 + indexes.Length * width];
        integerPayload[0] = useUInt16 ? (byte)2 : (byte)3;
        for (var index = 0; index < indexes.Length; index += 1)
        {
            var target = integerPayload.AsSpan(1 + index * width, width);
            if (useUInt16)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)indexes[index]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target, indexes[index]);
            }
        }
        WriteExtension(stream, integerPayload);
        return true;
    }

    private static bool TryWriteNumber(Stream stream, object value)
    {
        switch (value)
        {
            case byte number:
                WriteUnsignedNumber(stream, number);
                return true;
            case ushort number:
                WriteUnsignedNumber(stream, number);
                return true;
            case uint number:
                WriteUnsignedNumber(stream, number);
                return true;
            case ulong number:
                WriteUnsignedNumber(stream, number);
                return true;
            case sbyte number:
                WriteSignedNumber(stream, number);
                return true;
            case short number:
                WriteSignedNumber(stream, number);
                return true;
            case int number:
                WriteSignedNumber(stream, number);
                return true;
            case long number:
                WriteSignedNumber(stream, number);
                return true;
            case float number:
                WriteFloat64(stream, number);
                return true;
            case double number:
                WriteFloat64(stream, number);
                return true;
            case decimal number:
                WriteFloat64(stream, (double)number);
                return true;
            default:
                return false;
        }
    }

    private static void WriteMessagePackValue(
        Stream stream,
        JsonElement value,
        RuntimeBinaryArraySchema binaryArraySchema,
        string path
    )
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject().ToArray();
                WriteMapHeader(stream, properties.Length);
                foreach (var property in properties)
                {
                    WriteString(stream, property.Name);
                    var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                    WriteMessagePackValue(stream, property.Value, binaryArraySchema, childPath);
                }
                break;
            case JsonValueKind.Array:
                if (TryWriteBinaryArray(stream, value, binaryArraySchema, path))
                {
                    break;
                }
                var items = value.EnumerateArray().ToArray();
                WriteArrayHeader(stream, items.Length);
                foreach (var item in items)
                {
                    WriteMessagePackValue(stream, item, binaryArraySchema, path);
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

    private static bool TryWriteBinaryArray(
        Stream stream,
        JsonElement value,
        RuntimeBinaryArraySchema binaryArraySchema,
        string path
    )
    {
        var items = value.EnumerateArray().ToArray();
        var isFloat32Array = binaryArraySchema switch
        {
            RuntimeBinaryArraySchema.PartRuntime => PartRuntimeFloat32ArrayPaths.Contains(path),
            RuntimeBinaryArraySchema.UnityMotion => UnityMotionFloat32ArrayPaths.Contains(path),
            _ => false,
        };
        if (isFloat32Array && items.Length >= 8)
        {
            var payload = new byte[1 + items.Length * sizeof(float)];
            payload[0] = 1;
            for (var index = 0; index < items.Length; index += 1)
            {
                if (items[index].ValueKind != JsonValueKind.Number)
                {
                    return false;
                }
                var number = items[index].GetSingle();
                if (!float.IsFinite(number))
                {
                    return false;
                }
                BinaryPrimitives.WriteSingleLittleEndian(
                    payload.AsSpan(1 + index * sizeof(float), sizeof(float)),
                    number
                );
            }
            WriteExtension(stream, payload);
            return true;
        }

        if (binaryArraySchema != RuntimeBinaryArraySchema.PartRuntime ||
            !PartRuntimeUnsignedIndexArrayPaths.Contains(path) ||
            items.Length < 16)
        {
            return false;
        }
        var indexes = new uint[items.Length];
        var useUInt16 = true;
        for (var index = 0; index < items.Length; index += 1)
        {
            if (!items[index].TryGetUInt32(out var number))
            {
                return false;
            }
            indexes[index] = number;
            useUInt16 &= number <= ushort.MaxValue;
        }
        var width = useUInt16 ? sizeof(ushort) : sizeof(uint);
        var integerPayload = new byte[1 + indexes.Length * width];
        integerPayload[0] = useUInt16 ? (byte)2 : (byte)3;
        for (var index = 0; index < indexes.Length; index += 1)
        {
            var target = integerPayload.AsSpan(1 + index * width, width);
            if (useUInt16)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)indexes[index]);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(target, indexes[index]);
            }
        }
        WriteExtension(stream, integerPayload);
        return true;
    }

    private static void WriteExtension(Stream stream, byte[] payload)
    {
        if (payload.Length <= byte.MaxValue)
        {
            stream.WriteByte(0xc7);
            stream.WriteByte((byte)payload.Length);
        }
        else if (payload.Length <= ushort.MaxValue)
        {
            stream.WriteByte(0xc8);
            WriteUInt16(stream, (ushort)payload.Length);
        }
        else
        {
            stream.WriteByte(0xc9);
            WriteUInt32(stream, (uint)payload.Length);
        }
        stream.WriteByte(BinaryArrayExtensionType);
        stream.Write(payload);
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
            WriteSignedNumber(stream, integer);
            return;
        }

        if (value.TryGetUInt64(out var unsigned))
        {
            WriteUnsignedNumber(stream, unsigned);
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
            if (value <= uint.MaxValue)
            {
                stream.WriteByte(0xce);
                WriteUInt32(stream, (uint)value);
            }
            else
            {
                stream.WriteByte(0xcf);
                WriteUInt64(stream, value);
            }
        }
    }

    private static void WriteSignedNumber(Stream stream, long value)
    {
        if (value >= 0)
        {
            WriteUnsignedNumber(stream, (ulong)value);
        }
        else if (value >= -32)
        {
            stream.WriteByte(unchecked((byte)value));
        }
        else if (value >= sbyte.MinValue)
        {
            stream.WriteByte(0xd0);
            stream.WriteByte(unchecked((byte)(sbyte)value));
        }
        else if (value >= short.MinValue)
        {
            stream.WriteByte(0xd1);
            WriteInt16(stream, (short)value);
        }
        else if (value >= int.MinValue)
        {
            stream.WriteByte(0xd2);
            WriteInt32(stream, (int)value);
        }
        else
        {
            stream.WriteByte(0xd3);
            WriteInt64(stream, value);
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
            case 0xc7:
                ReadExtension(data, ref offset, writer, data[offset++]);
                break;
            case 0xc8:
                ReadExtension(data, ref offset, writer, ReadUInt16(data, ref offset));
                break;
            case 0xc9:
                ReadExtension(data, ref offset, writer, checked((int)ReadUInt32(data, ref offset)));
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

    private static void ReadExtension(ReadOnlySpan<byte> data, ref int offset, Utf8JsonWriter writer, int length)
    {
        var type = data[offset++];
        var payload = data.Slice(offset, length);
        offset += length;
        if (type != BinaryArrayExtensionType || payload.Length < 1)
        {
            throw new NotSupportedException($"Unsupported MessagePack extension type {type}.");
        }

        writer.WriteStartArray();
        var bytes = payload[1..];
        switch (payload[0])
        {
            case 1 when bytes.Length % sizeof(float) == 0:
                for (var index = 0; index < bytes.Length; index += sizeof(float))
                {
                    writer.WriteNumberValue(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(index, sizeof(float))));
                }
                break;
            case 2 when bytes.Length % sizeof(ushort) == 0:
                for (var index = 0; index < bytes.Length; index += sizeof(ushort))
                {
                    writer.WriteNumberValue(BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(index, sizeof(ushort))));
                }
                break;
            case 3 when bytes.Length % sizeof(uint) == 0:
                for (var index = 0; index < bytes.Length; index += sizeof(uint))
                {
                    writer.WriteNumberValue(BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(index, sizeof(uint))));
                }
                break;
            default:
                throw new InvalidDataException($"Invalid runtime binary array payload type {payload[0]} with {bytes.Length} byte(s).");
        }
        writer.WriteEndArray();
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

    private static void WriteUInt64(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(buffer, value);
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
