using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AssetStudio;
using PjskBundle2Parts.Models;
using Object = AssetStudio.Object;

namespace PjskBundle2Parts.Services;

public sealed class MotionPackageExporter
{
    private const string SekaiUnityVersion = "2022.3.21f1";
    private const float BakeSampleRate = 120f;
    private const uint SekaiBlendShapeCrc = 2770785369;

    private static readonly IReadOnlyDictionary<string, string> SekaiLightCurveProperties = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["intensity"] = "intensity",
        ["ambientColor.r"] = "ambientColor.r",
        ["ambientColor.g"] = "ambientColor.g",
        ["ambientColor.b"] = "ambientColor.b",
        ["shadowColor.r"] = "shadowColor.r",
        ["shadowColor.g"] = "shadowColor.g",
        ["shadowColor.b"] = "shadowColor.b",
        ["outlineColor.r"] = "outlineColor.r",
        ["outlineColor.g"] = "outlineColor.g",
        ["outlineColor.b"] = "outlineColor.b",
        ["outlineBlending"] = "outlineBlending",
        ["rimColor.r"] = "rimColor.r",
        ["rimColor.g"] = "rimColor.g",
        ["rimColor.b"] = "rimColor.b",
        ["shadowRimColor.r"] = "shadowRimColor.r",
        ["shadowRimColor.g"] = "shadowRimColor.g",
        ["shadowRimColor.b"] = "shadowRimColor.b",
        ["range"] = "range",
        ["lightInfluence"] = "lightInfluence",
        ["emission"] = "emission",
        ["edgeSmoothness"] = "edgeSmoothness",
        ["shadowSharpness"] = "shadowSharpness",
        ["faceShadowLimitRange"] = "faceShadowLimitRange",
        ["useFaceShadowLimiter"] = "useFaceShadowLimiter",
        ["isUseShadowColor"] = "isUseShadowColor",
    };

    private static readonly Lazy<IReadOnlyDictionary<uint, string>> SekaiLightCurvePropertiesByCrc = new(
        () => SekaiLightCurveProperties.ToDictionary(
            pair => CalculateCrc32(pair.Key),
            pair => pair.Value
        )
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public MotionExportResult Export(
        string? motionPath,
        string outputDirectory,
        IImported? bodyModel = null
    )
    {
        if (string.IsNullOrWhiteSpace(motionPath))
        {
            return new MotionExportResult(null, null, null, null, null);
        }

        var normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(motionPath));
        Directory.CreateDirectory(outputDirectory);

        if (Directory.Exists(normalized))
        {
            return ExportFromFolder(normalized, outputDirectory, bodyModel);
        }

        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException($"Motion input not found: {motionPath}");
        }

        if (bodyModel is null)
        {
            throw new InvalidOperationException("Direct motion bundle export requires the body model hierarchy.");
        }

        return ExportFromBundle(normalized, outputDirectory, bodyModel);
    }

    public string ExportFaceMotion(
        string motionPath,
        string outputPath,
        string? sourcePath = null
    )
    {
        if (string.IsNullOrWhiteSpace(motionPath))
        {
            throw new ArgumentException("Motion input path is required.", nameof(motionPath));
        }

        var normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(motionPath));
        var clips = Directory.Exists(normalized)
            ? ReadDecodedClipsFromFolder(normalized)
            : File.Exists(normalized) && Path.GetExtension(normalized).Equals(".json", StringComparison.OrdinalIgnoreCase)
                ? ReadDecodedClipsFromJsonFile(normalized)
                : DecodeUnityClipsFromBundle(normalized);

        var faceClips = clips
            .Where(clip => clip.Name is "face" or "face_loop")
            .Select(BuildFaceMotionClip)
            .Where(clip => clip.Curves.Count > 0)
            .ToList();

        var faceMotion = new PjskFaceMotionSet(
            string.IsNullOrWhiteSpace(sourcePath) ? normalized : sourcePath,
            faceClips
        );
        var resolvedOutputPath = ResolveFaceMotionOutputPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedOutputPath)!);
        File.WriteAllText(
            resolvedOutputPath,
            JsonSerializer.Serialize(faceMotion, new JsonSerializerOptions { WriteIndented = true })
        );
        return resolvedOutputPath;
    }

    private static MotionExportResult ExportFromFolder(
        string motionFolder,
        string outputDirectory,
        IImported? bodyModel
    )
    {
        var unityMotionJson = FindFile(motionFolder, "unity-motion.json");
        var faceJson = FindFile(motionFolder, "face_motion.json");
        var lightJson = FindFile(motionFolder, "light_motion.json");
        var unityMotionOutput = default(string);
        var bodyMotionBindings = default(PjskBodyMotionBindingSet);

        if (unityMotionJson is not null)
        {
            unityMotionOutput = Path.Combine(outputDirectory, "unity-motion.json");
            var runtime = JsonSerializer.Deserialize<JsonNode>(
                File.ReadAllText(unityMotionJson),
                JsonOptions
            ) ?? throw new InvalidDataException($"Unity motion runtime is empty: {unityMotionJson}");
            RuntimeJsonWriter.Write(
                unityMotionOutput,
                runtime,
                JsonOptions,
                binaryArraySchema: RuntimeBinaryArraySchema.UnityMotion
            );
            unityMotionOutput = RuntimeJsonWriter.PrimaryPath(unityMotionOutput);
        }
        else
        {
            var decodedClips = ReadDecodedClipsFromFolder(motionFolder);
            if (decodedClips.Count > 0)
            {
                if (bodyModel is null)
                {
                    throw new InvalidOperationException("Decoded motion folder export requires the body model hierarchy.");
                }

                var decodedExport = ExportDecodedClips(
                    decodedClips,
                    motionFolder,
                    outputDirectory,
                    bodyModel
                );

                return new MotionExportResult(
                    SourcePath: motionFolder,
                    UnityMotionJsonPath: decodedExport.UnityMotionOutput,
                    BodyMotionBindings: decodedExport.BodyMotionBindings,
                    FaceMotion: decodedExport.FaceMotion,
                    LightMotion: decodedExport.LightMotion
                );
            }
        }

        var faceMotion = faceJson is null
            ? null
            : JsonSerializer.Deserialize<PjskFaceMotionSet>(
                File.ReadAllText(faceJson),
                JsonOptions
            );
        var lightMotion = lightJson is null
            ? null
            : JsonSerializer.Deserialize<PjskLightMotionSet>(
                File.ReadAllText(lightJson),
                JsonOptions
            );

        return new MotionExportResult(
            SourcePath: motionFolder,
            UnityMotionJsonPath: unityMotionOutput,
            BodyMotionBindings: bodyMotionBindings,
            FaceMotion: faceMotion,
            LightMotion: lightMotion
        );
    }

    private static MotionExportResult ExportFromBundle(
        string bundlePath,
        string outputDirectory,
        IImported bodyModel
    )
    {
        var decodedClips = DecodeUnityClipsFromBundle(bundlePath);
        (var unityMotionOutput, var bodyMotionBindings, var faceMotion, var lightMotion) =
            ExportDecodedClips(decodedClips, bundlePath, outputDirectory, bodyModel);

        return new MotionExportResult(
            SourcePath: bundlePath,
            UnityMotionJsonPath: unityMotionOutput,
            BodyMotionBindings: bodyMotionBindings,
            FaceMotion: faceMotion,
            LightMotion: lightMotion
        );
    }

    private static (
        string? UnityMotionOutput,
        PjskBodyMotionBindingSet? BodyMotionBindings,
        PjskFaceMotionSet? FaceMotion,
        PjskLightMotionSet? LightMotion
    ) ExportDecodedClips(
        IReadOnlyList<DecodedUnityClip> decodedClips,
        string sourcePath,
        string outputDirectory,
        IImported bodyModel
    )
    {
        var bodyClips = decodedClips
            .Where(clip => clip.Name is "motion" or "motion_loop")
            .ToList();
        var unityMotionOutput = default(string);
        var bodyMotionBindings = default(PjskBodyMotionBindingSet);
        if (bodyClips.Count > 0)
        {
            unityMotionOutput = Path.Combine(outputDirectory, "unity-motion.json");
            bodyMotionBindings = WriteUnityBodyMotionRuntime(
                bodyClips,
                bodyModel.RootFrame,
                unityMotionOutput
            );
            unityMotionOutput = RuntimeJsonWriter.PrimaryPath(unityMotionOutput);
        }

        var faceClips = decodedClips
            .Where(clip => clip.Name is "face" or "face_loop")
            .Select(BuildFaceMotionClip)
            .Where(clip => clip.Curves.Count > 0)
            .ToList();
        var faceMotion = faceClips.Count == 0
            ? null
            : new PjskFaceMotionSet(sourcePath, faceClips);
        var lightClips = decodedClips
            .Where(clip => clip.Name is not ("motion" or "motion_loop" or "face" or "face_loop"))
            .Select(BuildLightMotionClip)
            .Where(clip => clip.Curves.Count > 0)
            .ToList();
        var lightMotion = lightClips.Count == 0
            ? null
            : new PjskLightMotionSet(sourcePath, lightClips);

        return (unityMotionOutput, bodyMotionBindings, faceMotion, lightMotion);
    }

    private static IReadOnlyList<DecodedUnityClip> DecodeUnityClipsFromBundle(string bundlePath)
    {
        if (!File.Exists(bundlePath))
        {
            throw new FileNotFoundException($"Motion input not found: {bundlePath}");
        }

        using var readableBundle = new SekaiBundleDecryptor().PrepareReadableBundle(bundlePath);
        var manager = new AssetsManager
        {
            MeshLazyLoad = false,
        };
        manager.Options.CustomUnityVersion = new UnityVersion(SekaiUnityVersion);
        manager.SetAssetFilter(ClassIDType.AnimationClip);
        manager.LoadFilesAndFolders(readableBundle.Path);

        var clips = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .OfType<AnimationClip>()
            .Where(IsSupportedMotionClip)
            .OrderBy(clip => clip.m_Name is "motion" ? 0
                : clip.m_Name is "motion_loop" ? 1
                : clip.m_Name is "face" ? 2
                : clip.m_Name is "face_loop" ? 3
                : 4)
            .ToList();

        if (clips.Count == 0)
        {
            throw new InvalidDataException($"No supported AnimationClip assets found in {bundlePath}");
        }

        var decodedClips = new List<DecodedUnityClip>();
        foreach (var clip in clips)
        {
            try
            {
                decodedClips.Add(DecodeUnityClip(clip));
            }
            catch (InvalidDataException ex)
            {
                Console.Error.WriteLine($"[Motion] Skipping AnimationClip {clip.m_Name}: {ex.Message}");
            }
        }
        return decodedClips;
    }

    private static IReadOnlyList<DecodedUnityClip> ReadDecodedClipsFromFolder(string motionFolder)
    {
        var result = new List<DecodedUnityClip>();
        foreach (var file in Directory.EnumerateFiles(motionFolder, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("unity-motion.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("face_motion.json", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("light_motion.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var clip = JsonSerializer.Deserialize<DecodedUnityClip>(
                    File.ReadAllText(file),
                    JsonOptions
                );
                if (clip?.Curves is { Count: > 0 })
                {
                    result.Add(clip);
                }
            }
            catch (JsonException)
            {
                // Ignore unrelated JSON files in updater output folders.
            }
        }

        return result
            .OrderBy(clip => clip.Name is "motion" ? 0
                : clip.Name is "motion_loop" ? 1
                : clip.Name is "face" ? 2
                : clip.Name is "face_loop" ? 3
                : 4)
            .ThenBy(clip => clip.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<DecodedUnityClip> ReadDecodedClipsFromJsonFile(string file)
    {
        try
        {
            var clip = JsonSerializer.Deserialize<DecodedUnityClip>(
                File.ReadAllText(file),
                JsonOptions
            );
            return clip?.Curves is { Count: > 0 }
                ? new[] { clip }
                : Array.Empty<DecodedUnityClip>();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Decoded AnimationClip JSON is invalid: {file}", ex);
        }
    }

    private static string ResolveFaceMotionOutputPath(string outputPath)
    {
        var normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputPath));
        if (Directory.Exists(normalized) ||
            string.IsNullOrEmpty(Path.GetExtension(normalized)) ||
            normalized.EndsWith(Path.DirectorySeparatorChar) ||
            normalized.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return Path.Combine(normalized, "face_motion.json");
        }
        return normalized;
    }

    private static bool IsSupportedMotionClip(AnimationClip clip)
    {
        return true;
    }

    private static DecodedUnityClip DecodeUnityClip(AnimationClip source)
    {
        if (source.m_MuscleClip?.m_Clip?.data is null)
        {
            throw new InvalidDataException($"AnimationClip {source.m_Name} does not contain post-build clip data.");
        }

        var clip = source.m_MuscleClip.m_Clip.data;
        var bindings = source.m_ClipBindingConstant ?? clip.ConvertValueArrayToGenericBinding();
        if (bindings?.genericBindings is null || bindings.genericBindings.Count == 0)
        {
            throw new InvalidDataException($"AnimationClip {source.m_Name} has no generic bindings.");
        }

        var ranges = BuildBindingRanges(bindings.genericBindings);
        var curves = new Dictionary<UnityBinding, UnityCurve>();

        UnityCurve GetCurve(GenericBinding binding)
        {
            var key = new UnityBinding(binding.path, binding.attribute, binding.typeID);
            if (!curves.TryGetValue(key, out var curve))
            {
                curve = new UnityCurve(key);
                curves[key] = curve;
            }
            return curve;
        }

        var streamedFrames = clip.m_StreamedClip.ReadData();
        RecomputeStreamedInSlopes(streamedFrames);
        foreach (var frame in streamedFrames)
        {
            for (var curveIndex = 0; curveIndex < frame.keyList.Count;)
            {
                var binding = FindBinding(ranges, frame.keyList[curveIndex].index);
                var dimension = BindingDimension(binding);
                var values = new float[dimension];
                var inSlopes = new float[dimension];
                var outSlopes = new float[dimension];
                for (var component = 0; component < dimension && curveIndex < frame.keyList.Count; component++)
                {
                    var key = frame.keyList[curveIndex++];
                    values[component] = key.value;
                    inSlopes[component] = key.inSlope;
                    outSlopes[component] = key.outSlope;
                }
                GetCurve(binding).Keys.Add(new UnityCurveKey(
                    frame.time,
                    values,
                    inSlopes,
                    outSlopes,
                    IsDense: false,
                    IsConstant: false
                ));
            }
        }

        var denseClip = clip.m_DenseClip;
        var denseCurveOffset = (int)clip.m_StreamedClip.curveCount;
        for (var frameIndex = 0; frameIndex < denseClip.m_FrameCount; frameIndex++)
        {
            var time = denseClip.m_BeginTime + frameIndex / denseClip.m_SampleRate;
            var frameOffset = frameIndex * (int)denseClip.m_CurveCount;
            for (var curveIndex = 0; curveIndex < denseClip.m_CurveCount;)
            {
                var binding = FindBinding(ranges, denseCurveOffset + (int)curveIndex);
                var dimension = BindingDimension(binding);
                var values = new float[dimension];
                for (var component = 0; component < dimension; component++)
                {
                    values[component] = denseClip.m_SampleArray[frameOffset + curveIndex++];
                }
                GetCurve(binding).Keys.Add(new UnityCurveKey(
                    time,
                    values,
                    ZeroSlopes(dimension),
                    ZeroSlopes(dimension),
                    IsDense: true,
                    IsConstant: false
                ));
            }
        }

        if (clip.m_ConstantClip?.data is { Length: > 0 } constantValues)
        {
            var constantCurveOffset = denseCurveOffset + (int)denseClip.m_CurveCount;
            foreach (var time in new[] { 0f, source.m_MuscleClip.m_StopTime })
            {
                for (var curveIndex = 0; curveIndex < constantValues.Length;)
                {
                    var binding = FindBinding(ranges, constantCurveOffset + curveIndex);
                    var dimension = BindingDimension(binding);
                    var values = new float[dimension];
                    for (var component = 0; component < dimension; component++)
                    {
                        values[component] = constantValues[curveIndex++];
                    }
                    GetCurve(binding).Keys.Add(new UnityCurveKey(
                        time,
                        values,
                        ZeroSlopes(dimension),
                        ZeroSlopes(dimension),
                        IsDense: false,
                        IsConstant: true
                    ));
                }
            }
        }

        foreach (var curve in curves.Values)
        {
            curve.Keys.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        var duration = source.m_MuscleClip.m_StopTime;
        if (duration <= 0)
        {
            duration = curves.Values
                .SelectMany(curve => curve.Keys)
                .Select(key => key.Time)
                .DefaultIfEmpty(0)
                .Max();
        }

        return new DecodedUnityClip(
            source.m_Name,
            source.m_SampleRate > 0 ? source.m_SampleRate : BakeSampleRate,
            duration,
            curves.Values.ToList()
        );
    }

    private static List<BindingRange> BuildBindingRanges(IReadOnlyList<GenericBinding> bindings)
    {
        var ranges = new List<BindingRange>(bindings.Count);
        var start = 0;
        foreach (var binding in bindings)
        {
            var dimension = BindingDimension(binding);
            ranges.Add(new BindingRange(binding, start, dimension));
            start += dimension;
        }
        return ranges;
    }

    private static GenericBinding FindBinding(IReadOnlyList<BindingRange> ranges, int curveIndex)
    {
        foreach (var range in ranges)
        {
            if (curveIndex >= range.Start && curveIndex < range.Start + range.Dimension)
            {
                return range.Binding;
            }
        }
        throw new InvalidDataException($"Animation curve index {curveIndex} has no generic binding.");
    }

    private static int BindingDimension(GenericBinding binding)
    {
        if (binding.typeID != ClassIDType.Transform)
        {
            return 1;
        }

        return binding.attribute switch
        {
            1 or 3 or 4 => 3,
            2 => 4,
            _ => 1,
        };
    }

    private static float[] ZeroSlopes(int dimension)
    {
        return new float[dimension];
    }

    private static void RecomputeStreamedInSlopes(IReadOnlyList<StreamedClip.StreamedFrame> frames)
    {
        var previousKeys = new Dictionary<int, (StreamedClip.StreamedCurveKey Key, float Time)>();
        foreach (var frame in frames)
        {
            foreach (var key in frame.keyList)
            {
                key.inSlope = float.PositiveInfinity;
                if (previousKeys.TryGetValue(key.index, out var previous))
                {
                    key.inSlope = previous.Key.CalculateNextInSlope(frame.time - previous.Time, key);
                }
                previousKeys[key.index] = (key, frame.time);
            }
        }
    }

    private static PjskBodyMotionBindingSet WriteUnityBodyMotionRuntime(
        IReadOnlyList<DecodedUnityClip> clips,
        ImportedFrame rootFrame,
        string outputUnityJsonPath
    )
    {
        var crcToBinding = BuildCrcToBodyMotionBinding(rootFrame);
        var bakedClips = clips
            .Select(clip => BakeBodyClip(clip, crcToBinding))
            .Where(clip => clip.Tracks.Count > 0)
            .ToList();

        if (bakedClips.Count == 0)
        {
            throw new InvalidDataException("Motion bundle did not produce any bindable body animation tracks.");
        }

        WriteUnityMotionRuntimeJson(bakedClips, outputUnityJsonPath);
        var usedPathCrcs = bakedClips
            .SelectMany(clip => clip.Tracks)
            .Select(track => track.PathCrc)
            .Distinct()
            .ToHashSet();
        var bindings = crcToBinding.Values
            .Where(binding => usedPathCrcs.Contains(binding.PathCrc))
            .OrderBy(binding => binding.ImportedPath ?? binding.LeafName, StringComparer.Ordinal)
            .Select(binding => new PjskBodyMotionBinding(
                PathCrc: binding.PathCrc,
                NodeKey: binding.NodeKey,
                LeafName: binding.LeafName,
                ImportedPath: binding.ImportedPath,
                SourceRest: binding.SourceRest,
                TargetCount: 0,
                Targets: Array.Empty<PjskBodyMotionTarget>()
            ))
            .ToList();
        return new PjskBodyMotionBindingSet(
            Version: "0414",
            BindingMode: "unityPathCrcToPrefabActiveTargets",
            ClipNames: bakedClips.Select(clip => clip.Name).ToList(),
            Bindings: bindings,
            Warnings: Array.Empty<string>()
        );
    }

    private static void WriteUnityMotionRuntimeJson(
        IReadOnlyList<BakedAnimationClip> clips,
        string outputPath
    )
    {
        var runtime = new PjskUnityMotionRuntime(
            Version: "0414",
            UnityVersion: SekaiUnityVersion,
            CoordinateSpace: new PjskUnityRuntimeCoordinateSpace(
                Source: "unity-left-handed",
                Viewer: "three-js-right-handed",
                PositionConversion: "viewer_mirror_x",
                RotationConversion: "viewer_negate_quaternion_yz",
                ScaleConversion: "identity",
                Notes: new[]
                {
                    "Animation curve values are stored in Unity source space.",
                    "The viewer must convert transform animation values when applying them to Three.js nodes."
                }
            ),
            SampleRate: BakeSampleRate,
            Clips: clips
                .Select(clip => new PjskUnityMotionClip(
                    Name: clip.Name,
                    Tracks: clip.Tracks
                        .Select(track => new PjskUnityMotionTrack(
                            NodeKey: track.NodeName,
                            PathCrc: track.PathCrc,
                            Property: track.TargetPath,
                            ComponentCount: track.ComponentCount,
                            Times: track.Times,
                            Values: track.Values
                        ))
                        .ToList()
                ))
                .ToList()
        );
        RuntimeJsonWriter.Write(
            outputPath,
            runtime,
            new JsonSerializerOptions { WriteIndented = true },
            binaryArraySchema: RuntimeBinaryArraySchema.UnityMotion
        );
    }

    private static Dictionary<uint, BodyMotionBindingDraft> BuildCrcToBodyMotionBinding(ImportedFrame rootFrame)
    {
        var result = new Dictionary<uint, BodyMotionBindingDraft>();

        void Visit(ImportedFrame frame)
        {
            var path = frame.Path;
            while (!string.IsNullOrEmpty(path))
            {
                var pathCrc = CalculateCrc32(path);
                result[pathCrc] = new BodyMotionBindingDraft(
                    PathCrc: pathCrc,
                    NodeKey: BuildBodyMotionNodeKey(pathCrc),
                    LeafName: frame.Name,
                    ImportedPath: path,
                    SourceRest: BuildBodyMotionRest(frame)
                );
                var slash = path.IndexOf('/', StringComparison.Ordinal);
                if (slash < 0)
                {
                    break;
                }
                path = path[(slash + 1)..];
            }

            for (var i = 0; i < frame.Count; i++)
            {
                Visit(frame[i]);
            }
        }

        Visit(rootFrame);
        result[0] = new BodyMotionBindingDraft(
            PathCrc: 0,
            NodeKey: BuildBodyMotionNodeKey(0),
            LeafName: rootFrame.Name,
            ImportedPath: rootFrame.Path,
            SourceRest: BuildBodyMotionRest(rootFrame)
        );
        return result;
    }

    private static PjskBodyMotionRestTransform BuildBodyMotionRest(ImportedFrame frame)
    {
        var rotation = Fbx.EulerToQuaternion(frame.LocalRotation);
        return new PjskBodyMotionRestTransform(
            Position: new PjskMotionVector3(frame.LocalPosition.X, frame.LocalPosition.Y, frame.LocalPosition.Z),
            Rotation: new PjskMotionQuaternion(rotation.X, rotation.Y, rotation.Z, rotation.W),
            Scale: new PjskMotionVector3(frame.LocalScale.X, frame.LocalScale.Y, frame.LocalScale.Z)
        );
    }

    private static BakedAnimationClip BakeBodyClip(
        DecodedUnityClip source,
        IReadOnlyDictionary<uint, BodyMotionBindingDraft> crcToBinding
    )
    {
        var fullTimes = BuildBakeTimes(source.Duration);
        var tracks = new List<BakedAnimationTrack>();
        var usedTargets = new HashSet<string>(StringComparer.Ordinal);

        foreach (var curve in source.Curves)
        {
            if (curve.Binding.TypeId != ClassIDType.Transform)
            {
                continue;
            }
            if (!crcToBinding.TryGetValue(curve.Binding.Path, out var binding))
            {
                Console.Error.WriteLine($"[Motion] {source.Name}: unbound transform CRC {curve.Binding.Path}");
                continue;
            }

            var targetPath = curve.Binding.Attribute switch
            {
                1 => "translation",
                2 or 4 => "rotation",
                3 => "scale",
                _ => null,
            };
            if (targetPath is null)
            {
                continue;
            }

            var targetKey = $"{binding.NodeKey}.{targetPath}";
            if (!usedTargets.Add(targetKey))
            {
                Console.Error.WriteLine($"[Motion] {source.Name}: duplicate track target {targetKey}, keeping first track.");
                continue;
            }

            var componentCount = targetPath == "rotation" ? 4 : 3;
            var values = new List<float>(fullTimes.Count * componentCount);
            foreach (var time in fullTimes)
            {
                values.AddRange(ConvertUnityPrefabCurveValue(curve.Binding.Attribute, SampleCurve(curve, time)));
            }

            var trackTimes = fullTimes;
            if (CanCollapseTrack(values, componentCount))
            {
                trackTimes = new List<float> { 0f, source.Duration };
                values = values.Take(componentCount).Concat(values.Take(componentCount)).ToList();
            }

            tracks.Add(new BakedAnimationTrack(
                NodeName: binding.NodeKey,
                PathCrc: binding.PathCrc,
                TargetPath: targetPath,
                ComponentCount: componentCount,
                Times: trackTimes,
                Values: values
            ));
        }

        return new BakedAnimationClip(source.Name, tracks);
    }

    private static string BuildBodyMotionNodeKey(uint pathCrc)
    {
        return $"ucrc_{pathCrc}";
    }

    private static PjskFaceMotionClip BuildFaceMotionClip(DecodedUnityClip source)
    {
        var times = BuildBakeTimes(source.Duration);
        var curves = new List<PjskFaceMotionCurve>();

        foreach (var curve in source.Curves)
        {
            if (curve.Binding.TypeId != ClassIDType.SkinnedMeshRenderer ||
                curve.Binding.Path != SekaiBlendShapeCrc)
            {
                continue;
            }

            var keyframes = times
                .Select(time => new PjskFaceMotionKeyframe(time, SampleCurve(curve, time)[0]))
                .ToList();
            if (CanCollapseScalarCurve(keyframes))
            {
                var value = keyframes[0].Value;
                keyframes = new List<PjskFaceMotionKeyframe>
                {
                    new(0f, value),
                    new(source.Duration, value),
                };
            }

            curves.Add(new PjskFaceMotionCurve(curve.Binding.Attribute, keyframes));
        }

        return new PjskFaceMotionClip(
            source.Name,
            BakeSampleRate,
            source.Duration,
            curves
        );
    }

    private static PjskLightMotionClip BuildLightMotionClip(DecodedUnityClip source)
    {
        var times = BuildBakeTimes(source.Duration);
        var curves = new List<PjskLightMotionCurve>();
        var lightPropertiesByCrc = SekaiLightCurvePropertiesByCrc.Value;

        foreach (var curve in source.Curves)
        {
            var propertyName = default(string);
            if (lightPropertiesByCrc.TryGetValue(curve.Binding.Attribute, out var scalarPropertyName))
            {
                propertyName = scalarPropertyName;
            }
            else if (curve.Binding.TypeId == ClassIDType.Transform && curve.Binding.Attribute == 4)
            {
                AddVectorLightCurves(curves, curve, times, source.Duration, "rotationEuler", 3);
                continue;
            }

            if (propertyName is null)
            {
                continue;
            }

            var keyframes = times
                .Select(time => new PjskFaceMotionKeyframe(time, SampleCurve(curve, time)[0]))
                .ToList();
            if (CanCollapseScalarCurve(keyframes))
            {
                var value = keyframes[0].Value;
                keyframes = new List<PjskFaceMotionKeyframe>
                {
                    new(0f, value),
                    new(source.Duration, value),
                };
            }

            curves.Add(new PjskLightMotionCurve(
                Property: propertyName,
                CurveHash: curve.Binding.Attribute,
                PathHash: curve.Binding.Path,
                TypeId: curve.Binding.TypeId.ToString(),
                Keyframes: keyframes
            ));
        }

        return new PjskLightMotionClip(
            source.Name,
            InferLightControllerKind(source.Name, curves),
            BakeSampleRate,
            source.Duration,
            curves
        );
    }

    private static void AddVectorLightCurves(
        List<PjskLightMotionCurve> curves,
        UnityCurve curve,
        IReadOnlyList<float> times,
        float duration,
        string propertyPrefix,
        int componentCount
    )
    {
        var componentNames = new[] { "x", "y", "z", "w" };
        for (var component = 0; component < componentCount; component++)
        {
            var componentIndex = component;
            var keyframes = times
                .Select(time =>
                {
                    var values = SampleCurve(curve, time);
                    var value = componentIndex < values.Length ? values[componentIndex] : 0f;
                    return new PjskFaceMotionKeyframe(time, value);
                })
                .ToList();
            if (CanCollapseScalarCurve(keyframes))
            {
                var value = keyframes[0].Value;
                keyframes = new List<PjskFaceMotionKeyframe>
                {
                    new(0f, value),
                    new(duration, value),
                };
            }

            curves.Add(new PjskLightMotionCurve(
                Property: $"{propertyPrefix}.{componentNames[component]}",
                CurveHash: curve.Binding.Attribute,
                PathHash: curve.Binding.Path,
                TypeId: curve.Binding.TypeId.ToString(),
                Keyframes: keyframes
            ));
        }
    }

    private static string InferLightControllerKind(
        string clipName,
        IReadOnlyList<PjskLightMotionCurve> curves
    )
    {
        var normalizedName = clipName.Replace('-', '_').ToLowerInvariant();
        if (normalizedName.Contains("character_rim") || normalizedName.Contains("chara_rim"))
        {
            return "character_rim";
        }
        if (normalizedName.Contains("character_ambient") || normalizedName.Contains("chara_ambient"))
        {
            return "character_ambient";
        }
        if (normalizedName.Contains("directional") || normalizedName.Contains("direction"))
        {
            return "directional";
        }
        if (normalizedName.Contains("ambient"))
        {
            return "ambient";
        }
        if (normalizedName.Contains("rim"))
        {
            return "character_rim";
        }

        var properties = curves.Select(curve => curve.Property).ToHashSet(StringComparer.Ordinal);
        if (
            properties.Contains("rimColor.r") ||
            properties.Contains("rimColor.g") ||
            properties.Contains("rimColor.b") ||
            properties.Contains("shadowRimColor.r") ||
            properties.Contains("shadowRimColor.g") ||
            properties.Contains("shadowRimColor.b") ||
            properties.Contains("edgeSmoothness") ||
            properties.Contains("shadowSharpness") ||
            properties.Contains("isUseShadowColor")
        )
        {
            return "character_rim";
        }
        if (
            properties.Contains("shadowColor.r") ||
            properties.Contains("shadowColor.g") ||
            properties.Contains("shadowColor.b") ||
            properties.Contains("outlineColor.r") ||
            properties.Contains("outlineColor.g") ||
            properties.Contains("outlineColor.b") ||
            properties.Contains("outlineBlending") ||
            properties.Contains("rotationEuler.x") ||
            properties.Contains("rotationEuler.y") ||
            properties.Contains("rotationEuler.z")
        )
        {
            return "directional";
        }
        if (properties.Contains("ambientColor.r") || properties.Contains("ambientColor.g") || properties.Contains("ambientColor.b"))
        {
            return "ambient";
        }
        return "unknown";
    }

    private static List<float> BuildBakeTimes(float duration)
    {
        var frameCount = Math.Max(2, (int)MathF.Round(duration * BakeSampleRate) + 1);
        var times = new List<float>(frameCount);
        for (var frame = 0; frame < frameCount; frame++)
        {
            var time = frame / BakeSampleRate;
            if (frame == frameCount - 1 || time > duration)
            {
                time = duration;
            }
            times.Add(time);
        }
        return times;
    }

    private static float[] SampleCurve(UnityCurve curve, float time)
    {
        if (curve.Keys.Count == 0)
        {
            return Array.Empty<float>();
        }
        if (time <= curve.Keys[0].Time)
        {
            return CopyValues(curve.Keys[0].Values);
        }

        for (var i = 1; i < curve.Keys.Count; i++)
        {
            var lhs = curve.Keys[i - 1];
            var rhs = curve.Keys[i];
            if (time <= rhs.Time)
            {
                return InterpolateCurveValue(lhs, rhs, time);
            }
        }

        return CopyValues(curve.Keys[^1].Values);
    }

    private static float[] InterpolateCurveValue(UnityCurveKey lhs, UnityCurveKey rhs, float time)
    {
        if (lhs.IsConstant)
        {
            return CopyValues(lhs.Values);
        }

        var dx = rhs.Time - lhs.Time;
        if (dx <= 1e-6f)
        {
            return CopyValues(rhs.Values);
        }

        var t = (time - lhs.Time) / dx;
        var values = new float[lhs.Values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (lhs.IsDense)
            {
                values[i] = Lerp(lhs.Values[i], rhs.Values[i], t);
                continue;
            }

            var outSlope = lhs.OutSlopes[i];
            var inSlope = rhs.InSlopes[i];
            if (float.IsInfinity(outSlope) || float.IsInfinity(inSlope))
            {
                values[i] = lhs.Values[i];
                continue;
            }

            values[i] = CubicHermite(
                t,
                lhs.Values[i],
                outSlope * dx,
                inSlope * dx,
                rhs.Values[i]
            );
        }
        return values;
    }

    private static float CubicHermite(float t, float p0, float m0, float m1, float p1)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p0
            + (t3 - 2 * t2 + t) * m0
            + (-2 * t3 + 3 * t2) * p1
            + (t3 - t2) * m1;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }

    private static float[] CopyValues(float[] values)
    {
        var copy = new float[values.Length];
        Array.Copy(values, copy, values.Length);
        return copy;
    }

    private static float[] ConvertBodyCurveValue(uint attribute, float[] values)
    {
        return attribute switch
        {
            1 => new[] { -values[0], values[1], values[2] },
            2 => NormalizeQuaternion(new[] { values[0], -values[1], -values[2], values[3] }),
            3 => new[] { values[0], values[1], values[2] },
            4 => ConvertUnityEulerDegreesToExportQuaternion(values[0], values[1], values[2]),
            _ => values,
        };
    }

    private static float[] ConvertUnityPrefabCurveValue(uint attribute, float[] values)
    {
        return attribute switch
        {
            1 => new[] { -values[0], values[1], values[2] },
            2 => NormalizeQuaternion(new[] { values[0], -values[1], -values[2], values[3] }),
            3 => new[] { values[0], values[1], values[2] },
            4 => ConvertUnityEulerDegreesToExportQuaternion(values[0], values[1], values[2]),
            _ => values,
        };
    }

    private static float[] NormalizeQuaternion(float[] q)
    {
        var length = MathF.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
        if (length <= 1e-8f)
        {
            return new[] { 0f, 0f, 0f, 1f };
        }
        return new[] { q[0] / length, q[1] / length, q[2] / length, q[3] / length };
    }

    private static float[] ConvertUnityEulerDegreesToExportQuaternion(float xDegrees, float yDegrees, float zDegrees)
    {
        var quaternion = Fbx.EulerToQuaternion(new AssetStudio.Vector3(xDegrees, yDegrees, zDegrees));
        return NormalizeQuaternion(new[] { quaternion.X, -quaternion.Y, -quaternion.Z, quaternion.W });
    }

    private static float[] ConvertUnityEulerDegreesToPrefabQuaternion(float xDegrees, float yDegrees, float zDegrees)
    {
        var quaternion = Fbx.EulerToQuaternion(new AssetStudio.Vector3(xDegrees, yDegrees, zDegrees));
        return NormalizeQuaternion(new[] { quaternion.X, quaternion.Y, quaternion.Z, quaternion.W });
    }

    private static bool CanCollapseTrack(IReadOnlyList<float> values, int componentCount)
    {
        if (values.Count <= componentCount * 2)
        {
            return false;
        }

        for (var offset = componentCount; offset < values.Count; offset += componentCount)
        {
            for (var component = 0; component < componentCount; component++)
            {
                if (MathF.Abs(values[component] - values[offset + component]) > 1e-5f)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static bool CanCollapseScalarCurve(IReadOnlyList<PjskFaceMotionKeyframe> keyframes)
    {
        if (keyframes.Count <= 2)
        {
            return false;
        }

        var value = keyframes[0].Value;
        return keyframes.All(keyframe => MathF.Abs(keyframe.Value - value) <= 1e-5f);
    }

    private static uint CalculateCrc32(string value)
    {
        var crc = 0xffffffffu;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(value))
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
        }
        return ~crc;
    }

    private sealed record BindingRange(GenericBinding Binding, int Start, int Dimension);

    private sealed record BakedAnimationClip(
        string Name,
        List<BakedAnimationTrack> Tracks
    );

    private sealed record BakedAnimationTrack(
        string NodeName,
        uint PathCrc,
        string TargetPath,
        int ComponentCount,
        IReadOnlyList<float> Times,
        IReadOnlyList<float> Values
    );

    private sealed record BodyMotionBindingDraft(
        uint PathCrc,
        string NodeKey,
        string LeafName,
        string? ImportedPath,
        PjskBodyMotionRestTransform SourceRest
    );

    private static string? FindFile(string folder, string fileName)
    {
        return Directory
            .EnumerateFiles(folder, fileName, SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

}
