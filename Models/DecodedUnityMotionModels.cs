using System.Text.Json.Serialization;
using AssetStudio;

namespace PjskBundle2Parts.Models;

public sealed record UnityBinding(
    [property: JsonPropertyName("path")] uint Path,
    [property: JsonPropertyName("attribute")] uint Attribute,
    [property: JsonPropertyName("typeId")] ClassIDType TypeId
);

public sealed class UnityCurve
{
    [JsonPropertyName("binding")]
    public UnityBinding Binding { get; set; }

    [JsonPropertyName("keys")]
    public List<UnityCurveKey> Keys { get; set; } = new();

    public UnityCurve(UnityBinding binding)
    {
        Binding = binding;
    }

    public UnityCurve()
        : this(new UnityBinding(0, 0, 0))
    {
    }
}

public sealed record UnityCurveKey(
    [property: JsonPropertyName("time")] float Time,
    [property: JsonPropertyName("values")] float[] Values,
    [property: JsonPropertyName("inSlopes")] float[] InSlopes,
    [property: JsonPropertyName("outSlopes")] float[] OutSlopes,
    [property: JsonPropertyName("isDense")] bool IsDense,
    [property: JsonPropertyName("isConstant")] bool IsConstant
);

public sealed record DecodedUnityClip(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sampleRate")] float SampleRate,
    [property: JsonPropertyName("duration")] float Duration,
    [property: JsonPropertyName("curves")] List<UnityCurve> Curves
);
