using System.Text.Json.Serialization;

namespace PjskBundle2Parts.Models;

public sealed record MotionExportResult(
    string? SourcePath,
    string? UnityMotionJsonPath,
    PjskBodyMotionBindingSet? BodyMotionBindings,
    PjskFaceMotionSet? FaceMotion,
    PjskLightMotionSet? LightMotion
);

public sealed record PjskUnityMotionRuntime(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("unityVersion")] string UnityVersion,
    [property: JsonPropertyName("coordinateSpace")] PjskUnityRuntimeCoordinateSpace CoordinateSpace,
    [property: JsonPropertyName("sampleRate")] float SampleRate,
    [property: JsonPropertyName("clips")] IReadOnlyList<PjskUnityMotionClip> Clips
);

public sealed record PjskUnityMotionClip(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("tracks")] IReadOnlyList<PjskUnityMotionTrack> Tracks
);

public sealed record PjskUnityMotionTrack(
    [property: JsonPropertyName("nodeKey")] string NodeKey,
    [property: JsonPropertyName("pathCrc")] uint PathCrc,
    [property: JsonPropertyName("property")] string Property,
    [property: JsonPropertyName("componentCount")] int ComponentCount,
    [property: JsonPropertyName("times")] IReadOnlyList<float> Times,
    [property: JsonPropertyName("values")] IReadOnlyList<float> Values
);

public sealed record PjskBodyMotionBindingSet(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("bindingMode")] string BindingMode,
    [property: JsonPropertyName("clipNames")] IReadOnlyList<string> ClipNames,
    [property: JsonPropertyName("bindings")] IReadOnlyList<PjskBodyMotionBinding> Bindings,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings
);

public sealed record PjskBodyMotionBinding(
    [property: JsonPropertyName("pathCrc")] uint PathCrc,
    [property: JsonPropertyName("nodeKey")] string NodeKey,
    [property: JsonPropertyName("leafName")] string LeafName,
    [property: JsonPropertyName("importedPath")] string? ImportedPath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("sourceRest")] PjskBodyMotionRestTransform? SourceRest,
    [property: JsonPropertyName("targetCount")] int TargetCount,
    [property: JsonPropertyName("targets")] IReadOnlyList<PjskBodyMotionTarget> Targets
);

public sealed record PjskBodyMotionTarget(
    [property: JsonPropertyName("poseRoot")] string PoseRoot,
    [property: JsonPropertyName("transformPath")] string TransformPath,
    [property: JsonPropertyName("pathId")] long PathId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonPropertyName("rest")] PjskBodyMotionRestTransform? Rest
);

public sealed record PjskBodyMotionRestTransform(
    [property: JsonPropertyName("position")] PjskMotionVector3 Position,
    [property: JsonPropertyName("rotation")] PjskMotionQuaternion Rotation,
    [property: JsonPropertyName("scale")] PjskMotionVector3 Scale
);

public sealed record PjskMotionVector3(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z
);

public sealed record PjskMotionQuaternion(
    [property: JsonPropertyName("x")] float X,
    [property: JsonPropertyName("y")] float Y,
    [property: JsonPropertyName("z")] float Z,
    [property: JsonPropertyName("w")] float W
);

public sealed record PjskFaceMotionSet(
    [property: JsonPropertyName("bundlePath")] string BundlePath,
    [property: JsonPropertyName("clips")] IReadOnlyList<PjskFaceMotionClip> Clips
);

public sealed record PjskFaceMotionClip(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sampleRate")] float SampleRate,
    [property: JsonPropertyName("duration")] float Duration,
    [property: JsonPropertyName("curves")] IReadOnlyList<PjskFaceMotionCurve> Curves
);

public sealed record PjskFaceMotionCurve(
    [property: JsonPropertyName("curveHash")] uint CurveHash,
    [property: JsonPropertyName("keyframes")] IReadOnlyList<PjskFaceMotionKeyframe> Keyframes
);

public sealed record PjskFaceMotionKeyframe(
    [property: JsonPropertyName("time")] float Time,
    [property: JsonPropertyName("value")] float Value
);

public sealed record PjskLightMotionSet(
    [property: JsonPropertyName("bundlePath")] string BundlePath,
    [property: JsonPropertyName("clips")] IReadOnlyList<PjskLightMotionClip> Clips
);

public sealed record PjskLightMotionClip(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("controllerKind")] string ControllerKind,
    [property: JsonPropertyName("sampleRate")] float SampleRate,
    [property: JsonPropertyName("duration")] float Duration,
    [property: JsonPropertyName("curves")] IReadOnlyList<PjskLightMotionCurve> Curves
);

public sealed record PjskLightMotionCurve(
    [property: JsonPropertyName("property")] string Property,
    [property: JsonPropertyName("curveHash")] uint CurveHash,
    [property: JsonPropertyName("pathHash")] uint PathHash,
    [property: JsonPropertyName("typeId")] string TypeId,
    [property: JsonPropertyName("keyframes")] IReadOnlyList<PjskFaceMotionKeyframe> Keyframes
);
