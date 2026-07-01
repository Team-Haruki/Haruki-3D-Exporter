using System.Collections;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using AssetStudio;
using PjskBundle2Parts.Models;
using Object = AssetStudio.Object;

namespace PjskBundle2Parts.Services;

public sealed class SpringBoneExporter
{
    private const string SekaiUnityVersion = "2022.3.21f1";
    private static readonly HashSet<string> SpringScriptNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpringManager",
        "SpringBone",
        "SpringSphereCollider",
        "SpringCapsuleCollider",
        "SpringPanelCollider",
        "SpringBonePivot",
        "ExtraBone",
        "WindVolumeOneSelf",
        "SekaiCharacterHair",
        "SekaiCharacterEye",
    };

    public SpringBoneExport Export(ResolvedBundleInput input)
    {
        using var readableBundle = new SekaiBundleDecryptor().PrepareReadableBundle(input.ResolvedBundlePath);
        var manager = new AssetsManager
        {
            MeshLazyLoad = false,
        };
        manager.Options.CustomUnityVersion = new UnityVersion(SekaiUnityVersion);
        manager.SetAssetFilter(
            ClassIDType.GameObject,
            ClassIDType.Transform,
            ClassIDType.Animator,
            ClassIDType.MonoBehaviour,
            ClassIDType.MeshRenderer,
            ClassIDType.SkinnedMeshRenderer
        );
        manager.LoadFilesAndFolders(readableBundle.Path);

        var objects = manager.AssetsFileList
            .SelectMany(file => file.Objects)
            .ToList();
        return Export(input, objects);
    }

    public SpringBoneExport Export(ResolvedBundleInput input, IReadOnlyList<Object> objects)
    {
        var objectRefsByPathId = BuildObjectRefIndex(objects);
        var rendererRawByPathId = objects
            .OfType<Renderer>()
            .ToDictionary(renderer => renderer.m_PathID, renderer => ConvertToJsonObject(renderer.ToType()) ?? new JsonObject());
        var rendererEnabledByPathId = objects
            .OfType<Renderer>()
            .ToDictionary(renderer => renderer.m_PathID, renderer => ReadRendererEnabled(rendererRawByPathId, renderer.m_PathID));
        var allMonoBehaviours = objects
            .OfType<MonoBehaviour>()
            .Select(mono => new SpringMonoRaw(
                Mono: mono,
                ScriptName: ResolveScriptName(mono),
                Raw: ConvertToJsonObject(mono.ToType()) ?? new JsonObject()
            ))
            .ToList();
        var managerReferencedBonePathIds = allMonoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SpringManager", StringComparison.OrdinalIgnoreCase))
            .SelectMany(entry => ReadObjectArray(entry.Raw, "springBones"))
            .Select(value => ResolveObjectRef(value, objectRefsByPathId)?.PathId)
            .Where(pathId => pathId is not null)
            .Select(pathId => pathId!.Value)
            .ToHashSet();
        var monoBehaviours = allMonoBehaviours
            .Where(entry => SpringScriptNames.Contains(entry.ScriptName))
            .ToList();
        var monoByPathId = allMonoBehaviours.ToDictionary(entry => entry.Mono.m_PathID);
        var warnings = new List<string>();

        foreach (var entry in monoBehaviours)
        {
            if (entry.Raw.Count == 0)
            {
                warnings.Add(
                    $"MonoBehaviour {entry.ScriptName}:{entry.Mono.m_PathID} has no readable typetree payload."
                );
            }
        }

        var managers = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SpringManager", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new SpringMonoBehaviourEntry(
                PathId: entry.Mono.m_PathID,
                ScriptName: entry.ScriptName,
                GameObject: ResolveGameObject(entry.Mono.m_GameObject),
                Raw: entry.Raw
            ))
            .ToList();
        var bones = managerReferencedBonePathIds
            .Select(pathId => monoByPathId.TryGetValue(pathId, out var entry) ? entry : null)
            .Where(entry => entry is not null)
            .Cast<SpringMonoRaw>()
            .Select(entry => BuildSpringBoneEntry(entry, objectRefsByPathId))
            .ToList();
        var missingSpringBonePathIds = managerReferencedBonePathIds
            .Where(pathId => !monoByPathId.ContainsKey(pathId))
            .ToList();
        var sphereColliders = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SpringSphereCollider", StringComparison.OrdinalIgnoreCase))
            .Select(entry => BuildSpringColliderEntry(entry, objectRefsByPathId, rendererEnabledByPathId))
            .ToList();
        var capsuleColliders = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SpringCapsuleCollider", StringComparison.OrdinalIgnoreCase))
            .Select(entry => BuildSpringColliderEntry(entry, objectRefsByPathId, rendererEnabledByPathId))
            .ToList();
        var panelColliders = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SpringPanelCollider", StringComparison.OrdinalIgnoreCase))
            .Select(entry => BuildSpringColliderEntry(entry, objectRefsByPathId, rendererEnabledByPathId))
            .ToList();
        var forceProviderEntries = allMonoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "WindVolumeOneSelf", StringComparison.OrdinalIgnoreCase));
        var forceProviders = forceProviderEntries
            .Where(entry => entry is not null)
            .Cast<SpringMonoRaw>()
            .Select(entry => new SpringMonoBehaviourEntry(
                PathId: entry.Mono.m_PathID,
                ScriptName: entry.ScriptName,
                GameObject: ResolveGameObject(entry.Mono.m_GameObject),
                Raw: entry.Raw
            ))
            .ToList();
        var springBonePivots = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SpringBonePivot", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new SpringMonoBehaviourEntry(
                PathId: entry.Mono.m_PathID,
                ScriptName: entry.ScriptName,
                GameObject: ResolveGameObject(entry.Mono.m_GameObject),
                Raw: entry.Raw
            ))
            .ToList();
        var extraBones = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "ExtraBone", StringComparison.OrdinalIgnoreCase))
            .Select(entry => BuildExtraBoneEntry(entry, objectRefsByPathId))
            .ToList();
        var characterHair = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SekaiCharacterHair", StringComparison.OrdinalIgnoreCase))
            .Select(entry => BuildCharacterHairEntry(entry, objectRefsByPathId))
            .FirstOrDefault();
        var characterEye = monoBehaviours
            .Where(entry => string.Equals(entry.ScriptName, "SekaiCharacterEye", StringComparison.OrdinalIgnoreCase))
            .Select(BuildCharacterEyeEntry)
            .FirstOrDefault();

        if (monoBehaviours.Count == 0)
        {
            warnings.Add("No SpringManager/SpringBone/Spring*Collider MonoBehaviours found.");
        }
        if (managers.Count == 0 && bones.Count > 0)
        {
            warnings.Add("SpringBone entries were found, but SpringManager was not found.");
        }
        foreach (var missingPathId in missingSpringBonePathIds)
        {
            warnings.Add($"SpringManager referenced missing spring bone MonoBehaviour PathID {missingPathId}.");
        }

        return new SpringBoneExport(
            Version: 1,
            BundlePath: input.ResolvedBundlePath,
            PartKind: input.PartKind.ToString(),
            PrefabGraph: BuildPrefabGraph(
                input.PartKind.ToString(),
                input.ResolvedBundlePath,
                objects,
                allMonoBehaviours
            ),
            Managers: managers,
            Bones: bones,
            SphereColliders: sphereColliders,
            CapsuleColliders: capsuleColliders,
            PanelColliders: panelColliders,
            ForceProviders: forceProviders,
            SpringBonePivots: springBonePivots,
            ExtraBones: extraBones,
            CharacterHair: characterHair,
            CharacterEye: characterEye,
            Warnings: warnings
        );
    }

    private static SpringPrefabGraph BuildPrefabGraph(
        string partKind,
        string bundlePath,
        IReadOnlyList<Object> objects,
        IReadOnlyList<SpringMonoRaw> monoBehaviours
    )
    {
        var objectRefsByPathId = BuildObjectRefIndex(objects);
        var rendererRawByPathId = objects
            .OfType<Renderer>()
            .ToDictionary(renderer => renderer.m_PathID, renderer => ConvertToJsonObject(renderer.ToType()) ?? new JsonObject());
        var gameObjects = objects.OfType<GameObject>()
            .OrderBy(gameObject => BuildTransformPathOrName(gameObject), StringComparer.Ordinal)
            .ThenBy(gameObject => gameObject.m_PathID)
            .Select(gameObject =>
            {
                var transformPath = gameObject.m_Transform is null ? null : BuildTransformPath(gameObject.m_Transform);
                return new SpringPrefabGameObject(
                    PathId: gameObject.m_PathID,
                    Name: gameObject.m_Name,
                    ActiveSelf: ReadGameObjectActiveSelf(gameObject),
                    ActiveInHierarchy: ReadGameObjectActiveInHierarchy(gameObject),
                    TransformPathId: gameObject.m_Transform?.m_PathID,
                    TransformPath: transformPath,
                    ComponentPathIds: gameObject.m_Components
                        .Select(component => component.m_PathID)
                        .Where(pathId => pathId != 0)
                        .ToList()
                );
            })
            .ToList();
        var transforms = objects.OfType<Transform>()
            .OrderBy(transform => BuildTransformPath(transform), StringComparer.Ordinal)
            .ThenBy(transform => transform.m_PathID)
            .Select(transform =>
            {
                var gameObjectPathId = transform.m_GameObject.m_PathID == 0
                    ? (long?)null
                    : transform.m_GameObject.m_PathID;
                var path = BuildTransformPath(transform);
                return new SpringPrefabTransform(
                    PathId: transform.m_PathID,
                    GameObjectPathId: gameObjectPathId,
                    Name: ResolveTransformName(transform),
                    TransformPath: path,
                    PoseRoot: ExtractPoseRoot(path),
                    ParentPathId: transform.m_Father.m_PathID == 0 ? null : transform.m_Father.m_PathID,
                    ChildPathIds: transform.m_Children
                        .Select(child => child.m_PathID)
                        .Where(pathId => pathId != 0)
                        .ToList(),
                    LocalPosition: ToSpringVector3(transform.m_LocalPosition),
                    LocalRotation: ToSpringQuaternion(transform.m_LocalRotation),
                    LocalScale: ToSpringVector3(transform.m_LocalScale)
                );
            })
            .ToList();
        var renderers = objects.OfType<Renderer>()
            .OrderBy(renderer => ResolveGameObject(renderer.m_GameObject)?.TransformPath, StringComparer.Ordinal)
            .ThenBy(renderer => renderer.m_PathID)
            .Select(renderer =>
            {
                var gameObject = ResolveGameObject(renderer.m_GameObject);
                var transformPathId = gameObject?.TransformPathId;
                var meshPathId = ResolveRendererMeshPathId(renderer, gameObject);
                var meshName = ResolveRendererMeshName(renderer, gameObject);
                var skinnedMeshBones = renderer is SkinnedMeshRenderer skinnedMeshRenderer
                    ? skinnedMeshRenderer.m_Bones
                        .Select(bone => bone.m_PathID)
                        .Where(pathId => pathId != 0)
                        .ToList()
                    : new List<long>();
                var rootBonePathId = rendererRawByPathId.TryGetValue(renderer.m_PathID, out var rendererRaw)
                    ? ResolveObjectRef(ReadProperty(rendererRaw, "m_RootBone"), objectRefsByPathId)?.PathId
                    : null;
                return new SpringPrefabRenderer(
                    PathId: renderer.m_PathID,
                    TypeName: renderer.type.ToString(),
                    GameObjectPathId: renderer.m_GameObject.m_PathID == 0 ? null : renderer.m_GameObject.m_PathID,
                    TransformPathId: transformPathId,
                    Name: gameObject?.Name,
                    TransformPath: gameObject?.TransformPath,
                    PoseRoot: ExtractPoseRoot(gameObject?.TransformPath),
                    ActiveSelf: gameObject?.ActiveSelf,
                    ActiveInHierarchy: gameObject?.ActiveInHierarchy,
                    Enabled: ReadRendererEnabled(rendererRawByPathId, renderer.m_PathID) ?? false,
                    MeshPathId: meshPathId,
                    MeshName: meshName,
                    SkinnedMeshBones: skinnedMeshBones,
                    RootBonePathId: rootBonePathId,
                    MaterialFileIds: renderer.m_Materials
                        .Select(material => (long)material.m_FileID)
                        .ToList(),
                    MaterialPathIds: renderer.m_Materials
                        .Select(material => material.m_PathID)
                        .ToList()
                );
            })
            .ToList();
        var animators = objects.OfType<Animator>()
            .OrderBy(animator => ResolveGameObject(animator.m_GameObject)?.TransformPath, StringComparer.Ordinal)
            .ThenBy(animator => animator.m_PathID)
            .Select(animator =>
            {
                var gameObject = ResolveGameObject(animator.m_GameObject);
                return new SpringPrefabAnimator(
                    PathId: animator.m_PathID,
                    GameObjectPathId: animator.m_GameObject.m_PathID == 0 ? null : animator.m_GameObject.m_PathID,
                    Name: gameObject?.Name,
                    TransformPath: gameObject?.TransformPath,
                    PoseRoot: ExtractPoseRoot(gameObject?.TransformPath),
                    Enabled: animator.m_Enabled != 0,
                    HasTransformHierarchy: animator.m_HasTransformHierarchy,
                    AvatarPathId: animator.m_Avatar.m_PathID == 0 ? null : animator.m_Avatar.m_PathID,
                    ControllerPathId: animator.m_Controller.m_PathID == 0 ? null : animator.m_Controller.m_PathID
                );
            })
            .ToList();
        var monoGraph = monoBehaviours
            .OrderBy(mono => ResolveGameObject(mono.Mono.m_GameObject)?.TransformPath, StringComparer.Ordinal)
            .ThenBy(mono => mono.ScriptName, StringComparer.Ordinal)
            .ThenBy(mono => mono.Mono.m_PathID)
            .Select(mono =>
            {
                var gameObject = ResolveGameObject(mono.Mono.m_GameObject);
                return new SpringPrefabMonoBehaviour(
                    PathId: mono.Mono.m_PathID,
                    ScriptName: mono.ScriptName,
                    GameObjectPathId: mono.Mono.m_GameObject.m_PathID == 0 ? null : mono.Mono.m_GameObject.m_PathID,
                    Name: gameObject?.Name,
                    TransformPath: gameObject?.TransformPath,
                    PoseRoot: ExtractPoseRoot(gameObject?.TransformPath),
                    Enabled: ReadBool(mono.Raw, "m_Enabled"),
                    ObjectReferenceFields: CollectObjectReferenceFields(mono.Raw)
                );
            })
            .ToList();
        var rootTransformPathIds = transforms
            .Where(transform => transform.ParentPathId is null)
            .Select(transform => transform.PathId)
            .ToList();

        return new SpringPrefabGraph(
            Version: 1,
            PartKind: partKind,
            BundlePath: bundlePath,
            GameObjects: gameObjects,
            Transforms: transforms,
            Renderers: renderers,
            Animators: animators,
            MonoBehaviours: monoGraph,
            RootTransformPathIds: rootTransformPathIds
        );
    }

    private static SpringBoneEntry BuildSpringBoneEntry(
        SpringMonoRaw entry,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId
    )
    {
        return new SpringBoneEntry(
            PathId: entry.Mono.m_PathID,
            ScriptName: entry.ScriptName,
            GameObject: ResolveGameObject(entry.Mono.m_GameObject),
            PivotNode: ResolveObjectRef(ReadObject(entry.Raw, "pivotNode"), objectRefsByPathId),
            Radius: ReadFloat(entry.Raw, "radius"),
            StiffnessForce: ReadFloat(entry.Raw, "stiffnessForce"),
            DragForce: ReadFloat(entry.Raw, "dragForce"),
            WindInfluence: ReadFloat(entry.Raw, "windInfluence"),
            SpringForce: ReadVector3(entry.Raw, "springForce"),
            LengthLimitTargets: ReadObjectArray(entry.Raw, "lengthLimitTargets")
                .Select(value => ResolveObjectRef(value, objectRefsByPathId))
                .Where(reference => reference is not null)
                .Cast<SpringObjectRef>()
                .ToList(),
            Colliders: ReadColliderRefs(entry.Raw, objectRefsByPathId),
            Raw: entry.Raw
        );
    }

    private static IReadOnlyList<SpringObjectRef> ReadColliderRefs(
        JsonObject raw,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId
    )
    {
        return new[]
            {
                "colliders",
                "sphereColliders",
                "capsuleColliders",
                "panelColliders",
            }
            .SelectMany(key => ReadObjectArray(raw, key))
            .Select(value => ResolveObjectRef(value, objectRefsByPathId))
            .Where(reference => reference is not null)
            .Cast<SpringObjectRef>()
            .ToList();
    }

    private static SpringColliderEntry BuildSpringColliderEntry(
        SpringMonoRaw entry,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId,
        IReadOnlyDictionary<long, bool?> rendererEnabledByPathId
    )
    {
        var linkedRendererNode = ReadObject(entry.Raw, "linkedRenderer");
        var linkedRenderer = ResolveObjectRef(linkedRendererNode, objectRefsByPathId);
        var linkedRendererPathId = ReadObjectPathId(linkedRendererNode);
        var linkedRendererEnabled = linkedRendererPathId is not null &&
            rendererEnabledByPathId.TryGetValue(linkedRendererPathId.Value, out var isEnabled)
                ? isEnabled
                : (bool?)null;
        return new SpringColliderEntry(
            PathId: entry.Mono.m_PathID,
            ScriptName: entry.ScriptName,
            GameObject: ResolveGameObject(entry.Mono.m_GameObject),
            LinkedRenderer: linkedRenderer,
            LinkedRendererEnabled: linkedRendererEnabled,
            Radius: ReadFloat(entry.Raw, "radius"),
            Height: ReadFloat(entry.Raw, "height") ?? ReadFloat(entry.Raw, "length"),
            Center: ReadVector3(entry.Raw, "center") ?? ReadVector3(entry.Raw, "offset"),
            Direction: ReadVector3(entry.Raw, "direction"),
            Raw: entry.Raw
        );
    }

    private static SpringExtraBoneEntry BuildExtraBoneEntry(
        SpringMonoRaw entry,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId
    )
    {
        return new SpringExtraBoneEntry(
            PathId: entry.Mono.m_PathID,
            ScriptName: entry.ScriptName,
            GameObject: ResolveGameObject(entry.Mono.m_GameObject),
            ReferenceBone: ResolveObjectRef(ReadObject(entry.Raw, "referenceBone"), objectRefsByPathId),
            RotationOrder: ReadInt(entry.Raw, "rotationOrder"),
            Coefficient: ReadFloat(entry.Raw, "coefficient"),
            DefaultEulerAngles: ReadVector3(entry.Raw, "defaultEulerAngles"),
            AxisX: ReadInt(entry.Raw, "axisX"),
            AxisY: ReadInt(entry.Raw, "axisY"),
            AxisZ: ReadInt(entry.Raw, "axisZ"),
            Raw: entry.Raw
        );
    }

    private static SpringCharacterHairEntry BuildCharacterHairEntry(
        SpringMonoRaw entry,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId
    )
    {
        return new SpringCharacterHairEntry(
            PathId: entry.Mono.m_PathID,
            ScriptName: entry.ScriptName,
            GameObject: ResolveGameObject(entry.Mono.m_GameObject),
            HeadTransform: ResolveObjectRef(ReadObject(entry.Raw, "headTransform"), objectRefsByPathId),
            Offset: ReadVector3(entry.Raw, "offset"),
            Raw: entry.Raw
        );
    }

    private static SpringCharacterEyeEntry BuildCharacterEyeEntry(SpringMonoRaw entry)
    {
        return new SpringCharacterEyeEntry(
            PathId: entry.Mono.m_PathID,
            ScriptName: entry.ScriptName,
            GameObject: ResolveGameObject(entry.Mono.m_GameObject),
            LightInfluence: ReadFloat(entry.Raw, "lightInfluence"),
            LightInfluenceForEyeHighlight: ReadFloat(entry.Raw, "lightInfluenceForEyeHighlight"),
            TintColor: ReadColor(entry.Raw, "tintColor"),
            EmissionColor: ReadColor(entry.Raw, "emissionColor"),
            BaseTiling: ReadTextureTiling(entry.Raw, "baseTiling"),
            HighlightTiling: ReadTextureTiling(entry.Raw, "highlightTiling"),
            LeftEyeCloseBlendShapeValue: ReadFloat(entry.Raw, "leftEyeCloseBlendShapeValue"),
            RightEyeCloseBlendShapeValue: ReadFloat(entry.Raw, "rightEyeCloseBlendShapeValue"),
            Raw: entry.Raw
        );
    }

    private static string ResolveScriptName(MonoBehaviour mono)
    {
        return mono.m_Script.TryGet(out MonoScript script)
            ? script.m_Name
            : $"missing-script:{mono.m_Script.m_PathID}";
    }

    private static SpringObjectRef? ResolveGameObject(PPtr<GameObject> pointer)
    {
        if (!pointer.TryGet(out GameObject gameObject))
        {
            return pointer.m_PathID == 0
                ? null
                : new SpringObjectRef(pointer.m_FileID, pointer.m_PathID, null, null);
        }
        return new SpringObjectRef(
            FileId: pointer.m_FileID,
            PathId: pointer.m_PathID,
            Name: gameObject.m_Name,
            TransformPath: gameObject.m_Transform is null
                ? null
                : BuildTransformPath(gameObject.m_Transform),
            TransformPathId: gameObject.m_Transform?.m_PathID,
            ActiveSelf: ReadGameObjectActiveSelf(gameObject),
            ActiveInHierarchy: ReadGameObjectActiveInHierarchy(gameObject)
        );
    }

    private static long? ResolveRendererMeshPathId(Renderer renderer, SpringObjectRef? gameObject)
    {
        if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
        {
            return skinnedMeshRenderer.m_Mesh.m_PathID == 0 ? null : skinnedMeshRenderer.m_Mesh.m_PathID;
        }

        if (!renderer.m_GameObject.TryGet(out GameObject resolvedGameObject) ||
            resolvedGameObject.m_MeshFilter is null)
        {
            return null;
        }

        var meshPathId = resolvedGameObject.m_MeshFilter.m_Mesh.m_PathID;
        return meshPathId == 0 ? null : meshPathId;
    }

    private static string? ResolveRendererMeshName(Renderer renderer, SpringObjectRef? gameObject)
    {
        if (renderer is SkinnedMeshRenderer skinnedMeshRenderer &&
            skinnedMeshRenderer.m_Mesh.TryGet(out Mesh skinnedMesh))
        {
            return skinnedMesh.m_Name;
        }

        if (!renderer.m_GameObject.TryGet(out GameObject resolvedGameObject) ||
            resolvedGameObject.m_MeshFilter is null ||
            !resolvedGameObject.m_MeshFilter.m_Mesh.TryGet(out Mesh mesh))
        {
            return null;
        }

        return mesh.m_Name;
    }

    private static bool? ReadGameObjectActiveSelf(GameObject gameObject)
    {
        try
        {
            var raw = ConvertToJsonObject(gameObject.ToType());
            return raw is not null && TryGetProperty(raw, "m_IsActive", out var value)
                ? ReadBool(value)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadGameObjectActiveInHierarchy(GameObject gameObject)
    {
        return ReadGameObjectActiveInHierarchy(gameObject, new HashSet<long>());
    }

    private static bool? ReadGameObjectActiveInHierarchy(GameObject gameObject, HashSet<long> visited)
    {
        if (!visited.Add(gameObject.m_PathID))
        {
            return null;
        }

        var activeSelf = ReadGameObjectActiveSelf(gameObject);
        if (activeSelf == false)
        {
            return false;
        }

        if (gameObject.m_Transform is null ||
            !gameObject.m_Transform.m_Father.TryGet(out Transform father) ||
            !father.m_GameObject.TryGet(out GameObject parent))
        {
            return activeSelf;
        }

        var parentActive = ReadGameObjectActiveInHierarchy(parent, visited);
        if (parentActive == false)
        {
            return false;
        }
        if (activeSelf is null || parentActive is null)
        {
            return null;
        }
        return true;
    }

    private static SpringObjectRef? ResolveObjectRef(
        JsonNode? node,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId
    )
    {
        if (node is not JsonObject obj)
        {
            return null;
        }
        var fileId = ReadInt(obj, "m_FileID") ?? 0;
        var pathId = ReadLong(obj, "m_PathID") ?? 0;
        return ResolveObjectRef(fileId, pathId, objectRefsByPathId);
    }

    private static long? ReadObjectPathId(JsonNode? node)
    {
        return node is JsonObject obj
            ? ReadLong(obj, "m_PathID")
            : null;
    }

    private static SpringObjectRef? ResolveObjectRef(
        int fileId,
        long pathId,
        IReadOnlyDictionary<long, SpringObjectRef> objectRefsByPathId
    )
    {
        if (pathId == 0)
        {
            return null;
        }
        if (!objectRefsByPathId.TryGetValue(pathId, out var reference))
        {
            return new SpringObjectRef(fileId, pathId, null, null);
        }
        return reference with
        {
            FileId = fileId,
            PathId = pathId,
        };
    }

    private static JsonNode? ReadObject(JsonObject obj, string key)
    {
        return TryGetProperty(obj, key, out var value) ? value : null;
    }

    private static IEnumerable<JsonNode?> ReadObjectArray(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is not JsonArray array)
        {
            return Array.Empty<JsonNode?>();
        }
        return array;
    }

    private static float? ReadFloat(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value))
        {
            return null;
        }
        return ReadFloat(value);
    }

    private static float? ReadFloat(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }
        if (value.GetValueKind() == System.Text.Json.JsonValueKind.Number &&
            value.AsValue().TryGetValue<float>(out var result))
        {
            return result;
        }
        return null;
    }

    private static int? ReadInt(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is null)
        {
            return null;
        }
        if (value.GetValueKind() != System.Text.Json.JsonValueKind.Number)
        {
            return null;
        }
        if (value.AsValue().TryGetValue<int>(out var result))
        {
            return result;
        }
        if (value.AsValue().TryGetValue<long>(out var longResult))
        {
            return longResult is >= int.MinValue and <= int.MaxValue
                ? (int)longResult
                : null;
        }
        if (value.AsValue().TryGetValue<double>(out var doubleResult))
        {
            return doubleResult >= int.MinValue && doubleResult <= int.MaxValue
                ? (int)doubleResult
                : null;
        }
        return null;
    }

    private static bool? ReadBool(JsonNode? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Number when value.AsValue().TryGetValue<int>(out var result) => result != 0,
            System.Text.Json.JsonValueKind.Number when value.AsValue().TryGetValue<long>(out var result) => result != 0,
            System.Text.Json.JsonValueKind.Number when value.AsValue().TryGetValue<float>(out var result) => MathF.Abs(result) > 0.00001f,
            _ => null,
        };
    }

    private static bool? ReadBool(JsonObject obj, string key)
    {
        return TryGetProperty(obj, key, out var value)
            ? ReadBool(value)
            : null;
    }

    private static bool? ReadRendererEnabled(
        IReadOnlyDictionary<long, JsonObject> rendererRawByPathId,
        long pathId
    )
    {
        return rendererRawByPathId.TryGetValue(pathId, out var raw)
            ? ReadBool(raw, "m_Enabled")
            : null;
    }

    private static JsonNode? ReadProperty(JsonObject obj, string key)
    {
        return TryGetProperty(obj, key, out var value) ? value : null;
    }

    private static long? ReadLong(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is null)
        {
            return null;
        }
        return value.GetValueKind() == System.Text.Json.JsonValueKind.Number &&
            value.AsValue().TryGetValue<long>(out var result)
                ? result
                : null;
    }

    private static SpringVector3? ReadVector3(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is not JsonObject vector)
        {
            return null;
        }
        var x = ReadFloat(vector, "x") ?? ReadFloat(vector, "X");
        var y = ReadFloat(vector, "y") ?? ReadFloat(vector, "Y");
        var z = ReadFloat(vector, "z") ?? ReadFloat(vector, "Z");
        return x.HasValue && y.HasValue && z.HasValue
            ? new SpringVector3(x.Value, y.Value, z.Value)
            : null;
    }

    private static SpringColor? ReadColor(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is not JsonObject color)
        {
            return null;
        }
        var r = ReadFloat(color, "r") ?? ReadFloat(color, "R");
        var g = ReadFloat(color, "g") ?? ReadFloat(color, "G");
        var b = ReadFloat(color, "b") ?? ReadFloat(color, "B");
        var a = ReadFloat(color, "a") ?? ReadFloat(color, "A");
        return r.HasValue && g.HasValue && b.HasValue && a.HasValue
            ? new SpringColor(r.Value, g.Value, b.Value, a.Value)
            : null;
    }

    private static SpringTextureTiling? ReadTextureTiling(JsonObject obj, string key)
    {
        if (!TryGetProperty(obj, key, out var value) || value is not JsonObject tiling)
        {
            return null;
        }
        var tileX = ReadInt(tiling, "TileX");
        var tileY = ReadInt(tiling, "TileY");
        var sample = ReadInt(tiling, "Sample");
        return tileX.HasValue && tileY.HasValue && sample.HasValue
            ? new SpringTextureTiling(tileX.Value, tileY.Value, sample.Value)
            : null;
    }

    private static bool TryGetProperty(JsonObject obj, string key, out JsonNode? value)
    {
        if (obj.TryGetPropertyValue(key, out value))
        {
            return true;
        }
        var pair = obj.FirstOrDefault(entry =>
            string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(pair.Key))
        {
            value = pair.Value;
            return true;
        }
        value = null;
        return false;
    }

    private static JsonObject? ConvertToJsonObject(object? value)
    {
        return ConvertToJsonNode(value) as JsonObject;
    }

    private static JsonNode? ConvertToJsonNode(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonNode node:
                return node.DeepClone();
            case OrderedDictionary ordered:
            {
                var obj = new JsonObject();
                foreach (DictionaryEntry entry in ordered)
                {
                    if (entry.Key is string key)
                    {
                        obj[key] = ConvertToJsonNode(entry.Value);
                    }
                }
                return obj;
            }
            case IDictionary dictionary:
            {
                var obj = new JsonObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    obj[StringifyDictionaryKey(entry.Key)] = ConvertToJsonNode(entry.Value);
                }
                return obj;
            }
            case IEnumerable enumerable when value is not string:
            {
                var array = new JsonArray();
                foreach (var item in enumerable)
                {
                    array.Add(ConvertToJsonNode(item));
                }
                return array;
            }
            case string text:
                return JsonValue.Create(text);
            case bool boolean:
                return JsonValue.Create(boolean);
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return JsonValue.Create(Convert.ToInt64(value));
            case float single:
                return JsonValue.Create(single);
            case double number:
                return JsonValue.Create(number);
            case decimal number:
                return JsonValue.Create(number);
            default:
                return JsonValue.Create(value.ToString());
        }
    }

    private static string StringifyDictionaryKey(object? key)
    {
        return key switch
        {
            null => "null",
            string text => text,
            _ => key.ToString() ?? "unknown",
        };
    }

    private static string BuildTransformPath(Transform transform)
    {
        if (!transform.m_GameObject.TryGet(out GameObject gameObject))
        {
            return $"transform:{transform.m_PathID}";
        }

        if (!transform.m_Father.TryGet(out Transform father))
        {
            return gameObject.m_Name;
        }

        return $"{BuildTransformPath(father)}/{gameObject.m_Name}";
    }

    private static string BuildTransformPathOrName(GameObject gameObject)
    {
        return gameObject.m_Transform is null
            ? gameObject.m_Name
            : BuildTransformPath(gameObject.m_Transform);
    }

    private static string? ResolveTransformName(Transform transform)
    {
        return transform.m_GameObject.TryGet(out GameObject gameObject)
            ? gameObject.m_Name
            : null;
    }

    private static string? ExtractPoseRoot(string? transformPath)
    {
        if (string.IsNullOrWhiteSpace(transformPath))
        {
            return null;
        }
        var slashIndex = transformPath.IndexOf('/');
        return slashIndex < 0 ? transformPath : transformPath[..slashIndex];
    }

    private static SpringVector3 ToSpringVector3(Vector3 value)
    {
        return new SpringVector3(value.X, value.Y, value.Z);
    }

    private static SpringQuaternion ToSpringQuaternion(Quaternion value)
    {
        return new SpringQuaternion(value.X, value.Y, value.Z, value.W);
    }

    private static IReadOnlyList<SpringPrefabObjectReferenceField> CollectObjectReferenceFields(JsonObject raw)
    {
        var fields = new List<SpringPrefabObjectReferenceField>();
        foreach (var pair in raw)
        {
            var references = new List<SpringPrefabObjectReference>();
            CollectObjectReferences(pair.Value, references);
            if (references.Count == 0)
            {
                continue;
            }
            fields.Add(new SpringPrefabObjectReferenceField(
                FieldPath: pair.Key,
                References: references
                    .Where(reference => reference.PathId != 0)
                    .Distinct()
                    .ToList()
            ));
        }
        return fields;
    }

    private static void CollectObjectReferences(
        JsonNode? node,
        List<SpringPrefabObjectReference> references
    )
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var fileId = ReadInt(obj, "m_FileID");
                var pathId = ReadLong(obj, "m_PathID");
                if (fileId is not null && pathId is not null)
                {
                    references.Add(new SpringPrefabObjectReference(fileId.Value, pathId.Value));
                    return;
                }
                foreach (var pair in obj)
                {
                    CollectObjectReferences(pair.Value, references);
                }
                return;
            }
            case JsonArray array:
                foreach (var item in array)
                {
                    CollectObjectReferences(item, references);
                }
                return;
        }
    }

    private static IReadOnlyDictionary<long, SpringObjectRef> BuildObjectRefIndex(
        IReadOnlyList<Object> objects
    )
    {
        var refs = new Dictionary<long, SpringObjectRef>();

        foreach (var gameObject in objects.OfType<GameObject>())
        {
            var transformPath = gameObject.m_Transform is null
                ? null
                : BuildTransformPath(gameObject.m_Transform);
            var activeSelf = ReadGameObjectActiveSelf(gameObject);
            var activeInHierarchy = ReadGameObjectActiveInHierarchy(gameObject);
            refs[gameObject.m_PathID] = new SpringObjectRef(
                FileId: 0,
                PathId: gameObject.m_PathID,
                Name: gameObject.m_Name,
                TransformPath: transformPath,
                ActiveSelf: activeSelf,
                ActiveInHierarchy: activeInHierarchy
            );
            if (gameObject.m_Transform is not null)
            {
                refs[gameObject.m_Transform.m_PathID] = new SpringObjectRef(
                    FileId: 0,
                    PathId: gameObject.m_Transform.m_PathID,
                    Name: gameObject.m_Name,
                    TransformPath: transformPath,
                    ActiveSelf: activeSelf,
                    ActiveInHierarchy: activeInHierarchy
                );
            }
        }

        foreach (var mono in objects.OfType<MonoBehaviour>())
        {
            var gameObject = ResolveGameObject(mono.m_GameObject);
            refs[mono.m_PathID] = new SpringObjectRef(
                FileId: 0,
                PathId: mono.m_PathID,
                Name: gameObject?.Name ?? ResolveScriptName(mono),
                TransformPath: gameObject?.TransformPath,
                ActiveSelf: gameObject?.ActiveSelf,
                ActiveInHierarchy: gameObject?.ActiveInHierarchy
            );
        }

        foreach (var renderer in objects.OfType<Renderer>())
        {
            var gameObject = ResolveGameObject(renderer.m_GameObject);
            refs[renderer.m_PathID] = new SpringObjectRef(
                FileId: 0,
                PathId: renderer.m_PathID,
                Name: gameObject?.Name ?? renderer.type.ToString(),
                TransformPath: gameObject?.TransformPath,
                ActiveSelf: gameObject?.ActiveSelf,
                ActiveInHierarchy: gameObject?.ActiveInHierarchy
            );
        }

        return refs;
    }

    private sealed record SpringMonoRaw(
        MonoBehaviour Mono,
        string ScriptName,
        JsonObject Raw
    );
}
