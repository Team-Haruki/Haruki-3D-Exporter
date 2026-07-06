using PjskBundle2Parts.Models;
using System.Text.Json.Nodes;

namespace PjskBundle2Parts.Services;

public sealed class PjskSekaiRuntimeExtensionBuilder
{
    public PjskSekaiRuntimeBuildResult Build(
        ConversionPlan plan,
        IReadOnlyDictionary<string, string> characterTexturePathByName,
        CombinedSpringBoneExport combinedSpringBone,
        VrmSpringBoneCandidate vrmSpringBoneCandidate,
        MotionExportResult? motionExport = null,
        ResolvedCharacter3dCostume? resolvedCharacter3dCostume = null,
        BundleInventory? accessoryInventory = null
    )
    {
        var bodySlots = plan.BodyManifestTemplate.BodyMaterials
            .Select(slot => new PjskSekaiRuntimeMaterialSlot(
                Part: "body",
                MeshName: slot.MeshName,
                SlotIndex: slot.SlotIndex,
                MaterialKey: slot.MaterialKey,
                MaterialFileId: slot.MaterialFileId,
                MaterialPathId: slot.MaterialPathId,
                MaterialName: slot.MaterialName,
                MaterialKind: slot.MaterialKind,
                MainTex: RewriteCharacterTexturePath("body", slot.MainTex, characterTexturePathByName),
                ShadowTex: RewriteCharacterTexturePath("body", slot.ShadowTex, characterTexturePathByName),
                ValueTex: RewriteCharacterTexturePath("body", slot.ValueTex, characterTexturePathByName),
                FaceShadowTex: null,
                RenderOrder: 0,
                ShaderPipeline: plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.BodyPipeline,
                Lighting: slot.Lighting
            ))
            .ToList();
        var headSlots = plan.HeadManifestTemplate.FaceMaterials
            .Select(slot => new PjskSekaiRuntimeMaterialSlot(
                Part: "head",
                MeshName: slot.MeshName,
                SlotIndex: slot.SlotIndex,
                MaterialKey: slot.MaterialKey,
                MaterialFileId: slot.MaterialFileId,
                MaterialPathId: slot.MaterialPathId,
                MaterialName: slot.MaterialName,
                MaterialKind: slot.MaterialKind,
                MainTex: RewriteCharacterTexturePath("head", slot.MainTex, characterTexturePathByName),
                ShadowTex: RewriteCharacterTexturePath("head", slot.ShadowTex, characterTexturePathByName),
                ValueTex: RewriteCharacterTexturePath("head", slot.ValueTex, characterTexturePathByName),
                FaceShadowTex: RewriteCharacterTexturePath("head", slot.FaceShadowTex, characterTexturePathByName),
                RenderOrder: GetHeadRenderOrder(slot.MaterialKind),
                ShaderPipeline: ResolveHeadShaderPipeline(plan, slot.MaterialKind),
                Lighting: slot.Lighting
            ))
            .ToList();
        var accessorySlots = BuildAccessoryMaterialSlots(
            plan,
            accessoryInventory,
            characterTexturePathByName
        );
        var missingTextureRoles = new List<PjskSekaiRuntimeMissingTextureRole>();
        var textureRoles = new List<PjskSekaiRuntimeTextureRole>();
        foreach (var slot in bodySlots.Concat(headSlots).Concat(accessorySlots))
        {
            AddTextureRole(textureRoles, missingTextureRoles, slot, "main", slot.MainTex);
            AddTextureRole(textureRoles, missingTextureRoles, slot, "shadow", slot.ShadowTex);
            AddTextureRole(textureRoles, missingTextureRoles, slot, "value", slot.ValueTex);
            AddTextureRole(textureRoles, missingTextureRoles, slot, "faceShadow", slot.FaceShadowTex);
        }

        var warnings = new List<string>();
        if (missingTextureRoles.Count > 0)
        {
            warnings.Add("Some optional texture roles are absent; see missingTextureRoles.");
        }

        var runtimeUnitySetup = BuildRuntimeUnitySetup(combinedSpringBone, vrmSpringBoneCandidate);
        var bodyMotionBindings = EnrichBodyMotionBindings(
            motionExport?.BodyMotionBindings,
            runtimeUnitySetup
        );

        var extension = new PjskSekaiRuntimeExtension(
            SpecVersion: "1.0",
            ProfileVersion: plan.SekaiVrmProfile.Version,
            Character: new PjskSekaiRuntimeCharacter(
                CharacterId: plan.Summary.CharacterId,
                SkeletonId: plan.Summary.SkeletonId,
                CharacterHeightMeters: plan.BodyManifestTemplate.CharacterHeightMeters,
                BodyBundlePath: plan.Summary.ResolvedBodyBundlePath,
                HeadBundlePath: plan.Summary.ResolvedHeadBundlePath,
                Costume: BuildCostumeMetadata(resolvedCharacter3dCostume)
            ),
            Container: new PjskSekaiRuntimeContainer(
                SourceGlb: null,
                PrefabRuntimeGlb: null,
                UnityRuntimeJson: Path.GetRelativePath(
                    plan.Summary.OutputDirectory,
                    plan.CharacterUnityRuntimeJsonPath
                ).Replace('\\', '/'),
                PreferredContainer: plan.SekaiVrmProfile.TargetContainer.Preferred,
                FallbackContainer: plan.SekaiVrmProfile.TargetContainer.Fallback,
                Phase: plan.SekaiVrmProfile.TargetContainer.CurrentPhase,
                RequiresCustomViewerForExactRender: plan.SekaiVrmProfile.SekaiRuntimeExtras.RequiredForExactViewerRender
            ),
            BodyHeadAssembly: new PjskSekaiRuntimeAssembly(
                BodyRootNodeName: plan.BodyManifestTemplate.Skeleton.RootNodeName,
                HeadRootNodeName: plan.HeadManifestTemplate.Assembly.RootNodeName,
                BodyNeckAttach: plan.BodyManifestTemplate.Skeleton.NeckAttach,
                HeadAttachOrigin: plan.HeadManifestTemplate.Assembly.AttachOrigin,
                BoneRemap: plan.HeadManifestTemplate.Assembly.BoneRemap,
                StitchMode: "bone_linked"
            ),
            SekaiRuntimeMaterialProfile: plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile,
            BodyManifest: plan.BodyManifestTemplate,
            HeadManifest: plan.HeadManifestTemplate,
            MaterialSlots: new PjskSekaiRuntimeMaterialSlots(
                Body: bodySlots,
                Head: headSlots,
                Accessory: accessorySlots.Count > 0 ? accessorySlots : null
            ),
            TextureRoles: textureRoles,
            CharacterTextures: characterTexturePathByName
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    pair => pair.Key,
                    pair => ToOutputRootTexturePath(pair.Value),
                    StringComparer.OrdinalIgnoreCase
                ),
            MorphChannelBindings: plan.HeadManifestTemplate.MorphChannelBindings,
            MotionPackage: motionExport?.SourcePath is null
                ? null
                : new PjskSekaiRuntimeMotionPackage(
                    SourcePath: motionExport.SourcePath,
                    BodyMotionGlb: null,
                    UnityMotionJson: motionExport.UnityMotionJsonPath is null
                        ? null
                        : Path.GetRelativePath(
                            plan.Summary.OutputDirectory,
                            motionExport.UnityMotionJsonPath
                        ).Replace('\\', '/'),
                    BodyMotionBindings: bodyMotionBindings,
                    FaceMotion: motionExport.FaceMotion,
                    LightMotion: motionExport.LightMotion
                ),
            CharacterControllers: new PjskSekaiRuntimeCharacterControllers(
                Hair: combinedSpringBone.Head.CharacterHair,
                Eye: combinedSpringBone.Head.CharacterEye
            ),
            PjskSpringBone: new PjskSekaiRuntimeSpringBonePayload(
                Raw: combinedSpringBone,
                VrmCandidate: vrmSpringBoneCandidate,
                RuntimeUnitySetup: runtimeUnitySetup
            ),
            SpringBoneSourceMetadata: new PjskSekaiRuntimeSpringBoneSourceMetadata(
                RawSpringBoneJson: Path.GetFileName(plan.CombinedSpringBonePath),
                VrmSpringBoneCandidateJson: Path.GetFileName(plan.VrmSpringBoneCandidatePath),
                VrmcSpringBoneExtensionJson: Path.GetFileName(plan.VrmcSpringBoneExtensionPath),
                EmbeddedExtension: "VRMC_springBone"
            ),
            ViewerHints: new PjskSekaiRuntimeViewerHints(
                MaterialBindingMode: "manifest",
                FaceMode: plan.HeadManifestTemplate.DefaultFaceMode,
                BodyPipeline: plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.BodyPipeline,
                FacePipeline: plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.FacePipeline,
                LayerPipeline: plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.LayerPipeline,
                PreserveGltfMaterialsAsFallback: false
            ),
            Notes: new[]
            {
                "Unity runtime JSON is the authoritative container for Transform, mesh, material, motion, and SpringBone data.",
                "GLB and VRM containers are not emitted by the pure Unity runtime export path.",
                "Face expressions are still driven by PJSK morph hash/channel bindings until VRM expression mapping is implemented.",
            }
        );

        var report = new PjskSekaiRuntimeResolveReport(
            Version: 1,
            ExtensionName: plan.SekaiVrmProfile.SekaiRuntimeExtras.ExtensionName,
            SourceGlb: null,
            BodyMaterialSlotCount: bodySlots.Count,
            HeadMaterialSlotCount: headSlots.Count,
            TextureRoleCount: textureRoles.Count,
            CharacterTextureCount: characterTexturePathByName.Count,
            MorphChannelBindingCount: plan.HeadManifestTemplate.MorphChannelBindings.Count,
            EmbeddedFaceMotionClipCount: motionExport?.FaceMotion?.Clips.Count ?? 0,
            BodyMotionGlb: null,
            MissingTextureRoles: missingTextureRoles,
            Warnings: warnings
        );

        return new PjskSekaiRuntimeBuildResult(extension, report);
    }

    private static PjskBodyMotionBindingSet? EnrichBodyMotionBindings(
        PjskBodyMotionBindingSet? source,
        PjskSpringBoneRuntimeUnitySetup runtimeUnitySetup
    )
    {
        if (source is null)
        {
            return null;
        }

        var targetByPathCrc = BuildPrefabMotionTargetLookup(runtimeUnitySetup);
        var warnings = new List<string>(source.Warnings);
        var bindings = source.Bindings
            .Select(binding =>
            {
                var targets = targetByPathCrc.TryGetValue(binding.PathCrc, out var matches)
                    ? matches
                    : new List<PjskBodyMotionTarget>();
                if (targets.Count == 0)
                {
                    warnings.Add($"Body motion binding {binding.NodeKey} ({binding.LeafName}) has no active prefab target.");
                }
                return binding with
                {
                    TargetCount = targets.Count,
                    Targets = targets,
                };
            })
            .ToList();

        return source with
        {
            Bindings = bindings,
            Warnings = warnings
                .Distinct(StringComparer.Ordinal)
                .OrderBy(warning => warning, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private static IReadOnlyDictionary<uint, List<PjskBodyMotionTarget>> BuildPrefabMotionTargetLookup(
        PjskSpringBoneRuntimeUnitySetup runtimeUnitySetup
    )
    {
        var rootPriority = runtimeUnitySetup.ActiveRootProfile.ActiveRoots
            .Select((root, index) => new { root, index })
            .ToDictionary(entry => entry.root, entry => entry.index, StringComparer.Ordinal);
        var activeRoots = rootPriority.Keys.ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<uint, List<PjskBodyMotionTarget>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var graph in runtimeUnitySetup.PrefabGraphs)
        {
            foreach (var transform in graph.Transforms)
            {
                if (string.IsNullOrWhiteSpace(transform.TransformPath) ||
                    string.IsNullOrWhiteSpace(transform.PoseRoot) ||
                    !activeRoots.Contains(transform.PoseRoot))
                {
                    continue;
                }

                foreach (var candidatePath in BuildMotionBindingPathCandidates(transform.TransformPath, transform.PoseRoot))
                {
                    var path = candidatePath;
                    while (!string.IsNullOrEmpty(path))
                    {
                        var pathCrc = CalculateCrc32(path);
                        var key = $"{pathCrc}:{transform.PathId}";
                        if (seen.Add(key))
                        {
                            if (!result.TryGetValue(pathCrc, out var targets))
                            {
                                targets = new List<PjskBodyMotionTarget>();
                                result[pathCrc] = targets;
                            }
                            targets.Add(new PjskBodyMotionTarget(
                                PoseRoot: transform.PoseRoot,
                                TransformPath: transform.TransformPath,
                                PathId: transform.PathId,
                                Rest: BuildBodyMotionRest(transform)
                            ));
                        }

                        var slash = path.IndexOf('/', StringComparison.Ordinal);
                        if (slash < 0)
                        {
                            break;
                        }
                        path = path[(slash + 1)..];
                    }
                }
            }
        }

        foreach (var pair in result)
        {
            pair.Value.Sort((a, b) =>
            {
                var priorityA = rootPriority.TryGetValue(a.PoseRoot, out var pa) ? pa : int.MaxValue;
                var priorityB = rootPriority.TryGetValue(b.PoseRoot, out var pb) ? pb : int.MaxValue;
                var priorityCompare = priorityA.CompareTo(priorityB);
                return priorityCompare != 0
                    ? priorityCompare
                    : string.CompareOrdinal(a.TransformPath, b.TransformPath);
            });
        }

        return result;
    }

    private static IEnumerable<string> BuildMotionBindingPathCandidates(string transformPath, string poseRoot)
    {
        yield return transformPath;

        var positionHipPrefix = $"{poseRoot}/Position/Hip";
        if (transformPath.StartsWith(positionHipPrefix, StringComparison.Ordinal))
        {
            yield return $"{poseRoot}/Position/PositionOffset/Hip{transformPath[positionHipPrefix.Length..]}";
        }
    }

    private static PjskBodyMotionRestTransform BuildBodyMotionRest(SpringPrefabTransform transform)
    {
        return new PjskBodyMotionRestTransform(
            Position: new PjskMotionVector3(
                -transform.LocalPosition.X,
                transform.LocalPosition.Y,
                transform.LocalPosition.Z
            ),
            Rotation: new PjskMotionQuaternion(
                transform.LocalRotation.X,
                -transform.LocalRotation.Y,
                -transform.LocalRotation.Z,
                transform.LocalRotation.W
            ),
            Scale: new PjskMotionVector3(
                transform.LocalScale.X,
                transform.LocalScale.Y,
                transform.LocalScale.Z
            )
        );
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

    private static PjskSpringBoneRuntimeUnitySetup BuildRuntimeUnitySetup(
        CombinedSpringBoneExport raw,
        VrmSpringBoneCandidate candidate
    )
    {
        var managers = raw.Body.Managers.Select(manager => BuildRuntimeManager("Body", manager, raw.Body))
            .Concat(raw.Head.Managers.Select(manager => BuildRuntimeManager("Head", manager, raw.Head)))
            .ToList();
        var bones = raw.Body.Bones.Select(bone => BuildRuntimeBone("Body", bone))
            .Concat(raw.Head.Bones.Select(bone => BuildRuntimeBone("Head", bone)))
            .ToList();
        var colliders = candidate.VrmExtensionDraft.Colliders
            .Select(collider => new PjskSpringBoneRuntimeCollider(
                PartKind: collider.PartKind,
                Index: collider.Index,
                PathId: collider.SourcePathId,
                ScriptName: collider.ScriptName,
                NodeName: collider.NodeName,
                NodePath: collider.NodePath,
                PoseRoot: collider.PoseRoot,
                Enabled: collider.Enabled,
                LinkedRenderer: collider.LinkedRenderer,
                LinkedRendererEnabled: collider.LinkedRendererEnabled,
                Shape: collider.Shape
            ))
            .ToList();
        var bindings = candidate.VrmExtensionDraft.ColliderGroups
            .Select(group => new PjskSpringBoneRuntimeColliderBinding(
                SourceKind: group.SourceKind ?? "direct",
                PartKind: group.PartKind,
                SourceSpringBonePathId: group.SourceSpringBonePathId,
                ColliderFlag: group.ColliderFlag,
                MatchedPrefixes: group.MatchedPrefixes,
                CollidersByRoot: group.CollidersByRoot,
                DefaultRoot: group.DefaultRoot,
                SourceColliderPathIds: group.SourceColliderPathIds,
                Colliders: group.Colliders
            ))
            .ToList();
        var bodyRoots = raw.Body.Managers
            .Select(manager => ExtractPoseRoot(manager.GameObject?.TransformPath))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var defaultBodyRoot = bodyRoots.Contains("body", StringComparer.Ordinal)
            ? "body"
            : bodyRoots.FirstOrDefault() ?? "body";
        var prefabGraphs = new[] { raw.Body.PrefabGraph, raw.Head.PrefabGraph };
        var rootSelectionProfile = BuildRootSelectionProfile(
            defaultBodyRoot,
            managers,
            bones,
            colliders,
            prefabGraphs
        );
        var setupPlan = BuildSetupPlan(managers, bindings);
        var bindingDecisions = BuildBindingDecisions(bones, bindings);
        var candidateRoots = rootSelectionProfile.RootCandidates
            .Select(candidate => candidate.Root)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var activeRoots = new[] { defaultBodyRoot, "face" }
            .Where(root => candidateRoots.Contains(root))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(root => RootPriority(root, defaultBodyRoot))
            .ThenBy(root => root, StringComparer.Ordinal)
            .ToList();

        return new PjskSpringBoneRuntimeUnitySetup(
            Version: "0414",
            UnityVersion: "2022.3.21f1",
            CoordinateSpace: new PjskUnityRuntimeCoordinateSpace(
                Source: "unity-left-handed",
                Viewer: "three-js-right-handed",
                PositionConversion: "viewer_mirror_x",
                RotationConversion: "viewer_negate_quaternion_yz",
                ScaleConversion: "identity",
                Notes: new[]
                {
                    "Prefab graph transforms and runtime SpringBone metadata are stored in Unity source space.",
                    "Three.js viewers must convert source transforms before rendering or simulation in viewer space.",
                }
            ),
            PrefabGraphs: prefabGraphs,
            BodyHeadAssembly: BuildBodyHeadAssembly(defaultBodyRoot, prefabGraphs),
            RootSelectionProfile: rootSelectionProfile,
            SetupPlan: setupPlan,
            BindingDecisions: bindingDecisions,
            ActiveRootProfile: new PjskSpringBoneActiveRootProfile(
                DefaultBodyRoot: defaultBodyRoot,
                ActiveRoots: activeRoots.Count == 0 ? new[] { defaultBodyRoot } : activeRoots,
                InactiveRoots: rootSelectionProfile.RootCandidates
                    .Select(candidate => candidate.Root)
                    .Distinct(StringComparer.Ordinal)
                    .Where(root => !activeRoots.Contains(root, StringComparer.Ordinal))
                    .ToList()
            ),
            FUnit: MergeFUnitSummaries(raw.Body.FUnit, raw.Head.FUnit),
            ConstraintSetup: BuildConstraintSetup(
                "combined_body_head_runtime",
                prefabGraphs
            ),
            ManagerColliderCaches: BuildManagerColliderCaches(managers, colliders),
            Managers: managers,
            Bones: bones,
            Colliders: colliders,
            ColliderBindings: bindings,
            Warnings: candidate.Warnings
        );
    }

    private static SpringFUnitSummary MergeFUnitSummaries(params SpringFUnitSummary[] summaries)
    {
        var active = summaries.Where(summary => summary.Present).ToList();
        return new SpringFUnitSummary(
            Present: active.Count > 0,
            ScriptCount: active.Sum(summary => summary.ScriptCount),
            SpringManagerCount: active.Sum(summary => summary.SpringManagerCount),
            SpringBoneCount: active.Sum(summary => summary.SpringBoneCount),
            SphereColliderCount: active.Sum(summary => summary.SphereColliderCount),
            CapsuleColliderCount: active.Sum(summary => summary.CapsuleColliderCount),
            PanelColliderCount: active.Sum(summary => summary.PanelColliderCount),
            DetectedScripts: active
                .SelectMany(summary => summary.DetectedScripts)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToList(),
            Policy: "metadata_only; do not merge with UTJ/Sekai SpringBone runtime"
        );
    }

    public static PjskUnityRuntimeConstraintSetup BuildConstraintSetup(
        string sourceKind,
        IReadOnlyList<SpringPrefabGraph> prefabGraphs
    )
    {
        var constraints = prefabGraphs
            .SelectMany(graph => graph.Constraints.Select(constraint => new { graph.PartKind, constraint }))
            .Select(entry => BuildConstraint(entry.PartKind, entry.constraint))
            .ToList();
        IReadOnlyList<string> warnings = constraints.Count == 0
            ? new[]
            {
                BuildMissingConstraintWarning(prefabGraphs),
            }
            : constraints
                .Where(constraint => constraint.Status != "resolved")
                .Select(constraint => $"Constraint {constraint.Type} at {constraint.OwnerPath ?? constraint.OwnerName ?? constraint.PathId.ToString()} is {constraint.Status}: {constraint.Reason}")
                .Distinct(StringComparer.Ordinal)
                .ToList();

        return new PjskUnityRuntimeConstraintSetup(
            Version: "0414",
            SourceKind: sourceKind,
            Constraints: constraints,
            Warnings: warnings
        );
    }

    private static PjskUnityRuntimeConstraint BuildConstraint(
        string partKind,
        SpringPrefabConstraint constraint
    )
    {
        var sources = constraint.Sources
            .Select(source => new PjskUnityRuntimeConstraintSource(
                SourcePathId: source.SourcePathId,
                SourceName: source.SourceName,
                SourcePath: source.SourcePath,
                Weight: source.Weight,
                TranslationOffset: source.TranslationOffset,
                RotationOffset: source.RotationOffset
            ))
            .ToList();
        var hasResolvedSource = sources.Count > 0 &&
            sources.All(source => !string.IsNullOrWhiteSpace(source.SourcePath) || !string.IsNullOrWhiteSpace(source.SourceName));
        var hasOwner = !string.IsNullOrWhiteSpace(constraint.OwnerPath) || !string.IsNullOrWhiteSpace(constraint.OwnerName);
        var status = hasOwner && hasResolvedSource ? "resolved" : "unresolved_source";
        return new PjskUnityRuntimeConstraint(
            PartKind: partKind,
            Type: constraint.Type,
            PathId: constraint.PathId,
            OwnerPath: constraint.OwnerPath,
            OwnerName: constraint.OwnerName,
            Enabled: constraint.Enabled,
            Active: constraint.Active,
            TranslationAtRest: constraint.TranslationAtRest,
            RotationAtRest: constraint.RotationAtRest,
            TranslationOffset: constraint.TranslationOffset,
            RotationOffset: constraint.RotationOffset,
            AimVector: constraint.AimVector,
            UpVector: constraint.UpVector,
            WorldUpVector: constraint.WorldUpVector,
            WorldUpType: constraint.WorldUpType,
            WorldUpObjectPathId: constraint.WorldUpObjectPathId,
            WorldUpObjectName: constraint.WorldUpObjectName,
            WorldUpObjectPath: constraint.WorldUpObjectPath,
            Sources: sources,
            Status: status,
            Reason: status == "resolved"
                ? "Constraint source transforms were exported by the AssetStudio runtime payload."
                : "Constraint component was detected, but owner/source transform path or name is incomplete in the normalized payload."
        );
    }

    private static string BuildMissingConstraintWarning(IReadOnlyList<SpringPrefabGraph> prefabGraphs)
    {
        var capabilities = prefabGraphs
            .Select(graph => graph.ConstraintCapability)
            .ToList();
        var supported = capabilities
            .SelectMany(capability => capability.SupportedTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();
        var missing = capabilities
            .SelectMany(capability => capability.MissingTypes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToList();
        return supported.Count == 0
            ? "No Unity RotationConstraint/ParentConstraint/AimConstraint ClassID is exposed by the current AssetStudio runtime; runtime constraint repair is inactive for this package."
            : $"No Unity constraint components were found in this package. AssetStudio supports [{string.Join(", ", supported)}]; missing [{string.Join(", ", missing)}].";
    }
    private static PjskUnityRuntimeBodyHeadAssembly BuildBodyHeadAssembly(
        string defaultBodyRoot,
        IReadOnlyList<SpringPrefabGraph> prefabGraphs
    )
    {
        var transformPaths = prefabGraphs
            .SelectMany(graph => graph.Transforms)
            .Select(transform => transform.TransformPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var parentAttachPath = ResolveFirstExistingPath(transformPaths, new[]
        {
            $"{defaultBodyRoot}/Position/PositionOffset/Hip/Waist/Spine/Chest/Neck",
            $"{defaultBodyRoot}/Position/Hip/Waist/Spine/Chest/Neck",
        });
        var childRootPath = ResolveFirstExistingPath(transformPaths, new[] { "face" });
        var childOriginPath = ResolveFirstExistingPath(transformPaths, new[]
        {
            "face/Position/Hip/Waist/Spine/Chest/Neck",
        });
        var parentHeadPath = parentAttachPath is null ? null : $"{parentAttachPath}/Head";
        var childHeadPath = childOriginPath is null ? null : $"{childOriginPath}/Head";

        return new PjskUnityRuntimeBodyHeadAssembly(
            Version: "0414",
            SourceKind: "unity-source-prefab-assembly",
            ParentRootPath: ResolveFirstExistingPath(transformPaths, new[] { defaultBodyRoot }),
            ParentAttachPath: parentAttachPath,
            ChildRootPath: childRootPath,
            ChildOriginPath: childOriginPath,
            RuntimeMountPath: null,
            ParentingMode: "model_combine_setup",
            CoordinateSpace: "unity-left-handed",
            Notes: new[]
            {
                "Body and head prefab roots are stored as separate Unity prefab graphs.",
                "Runtime must reproduce Sekai.Live.ModelUtility.ModelCombineSetup: graft face Neck/Head into body bones.",
                "Body Head children are drained to face Head; body renderer Neck/Head slots resolve to face Neck/Head.",
            },
            FaceRendererName: "Face",
            CombineNodeAName: "Neck",
            CombineNodeBName: "Head",
            ChildMoveSuffix: "_target",
            ParentCombineNodeAPath: parentAttachPath,
            ParentCombineNodeBPath: parentHeadPath is not null && transformPaths.Contains(parentHeadPath) ? parentHeadPath : null,
            ChildCombineNodeAPath: childOriginPath,
            ChildCombineNodeBPath: childHeadPath is not null && transformPaths.Contains(childHeadPath) ? childHeadPath : null
        );
    }

    private static string? ResolveFirstExistingPath(
        IReadOnlySet<string> transformPaths,
        IEnumerable<string> candidates
    )
    {
        return candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            transformPaths.Contains(candidate)
        );
    }

    private static PjskSpringBoneRootSelectionProfile BuildRootSelectionProfile(
        string defaultBodyRoot,
        IReadOnlyList<PjskSpringBoneRuntimeManager> managers,
        IReadOnlyList<PjskSpringBoneRuntimeBone> bones,
        IReadOnlyList<PjskSpringBoneRuntimeCollider> colliders,
        IReadOnlyList<SpringPrefabGraph> prefabGraphs
    )
    {
        var roots = managers.Select(manager => manager.PoseRoot)
            .Concat(bones.Select(bone => bone.PoseRoot))
            .Concat(colliders.Select(collider => collider.PoseRoot))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(root => RootPriority(root, defaultBodyRoot))
            .ThenBy(root => root, StringComparer.Ordinal)
            .ToList();
        var candidates = roots
            .Select(root =>
            {
                var rootManagers = managers
                    .Where(manager => string.Equals(manager.PoseRoot, root, StringComparison.Ordinal))
                    .ToList();
                var rootBones = bones
                    .Where(bone => string.Equals(bone.PoseRoot, root, StringComparison.Ordinal))
                    .ToList();
                var rootColliders = colliders
                    .Where(collider => string.Equals(collider.PoseRoot, root, StringComparison.Ordinal))
                    .ToList();
                var rendererPathIds = prefabGraphs
                    .SelectMany(graph => graph.Renderers)
                    .Where(renderer => string.Equals(renderer.PoseRoot, root, StringComparison.Ordinal))
                    .Select(renderer => renderer.PathId)
                    .Distinct()
                    .OrderBy(pathId => pathId)
                    .ToList();
                var staticActiveValues = rootManagers
                    .Select(manager => manager.ActiveInHierarchy)
                    .Concat(prefabGraphs
                        .SelectMany(graph => graph.GameObjects)
                        .Where(gameObject => string.Equals(ExtractPoseRoot(gameObject.TransformPath), root, StringComparison.Ordinal))
                        .Select(gameObject => gameObject.ActiveInHierarchy))
                    .Where(value => value is not null)
                    .Cast<bool>()
                    .ToList();
                return new PjskSpringBoneRootCandidate(
                    Root: root,
                    PartKind: root == "face" ? "Head" : "Body",
                    StaticActive: staticActiveValues.Count == 0 ? null : staticActiveValues.Any(value => value),
                    DefaultPriority: RootPriority(root, defaultBodyRoot),
                    ManagerPathIds: rootManagers.Select(manager => manager.PathId).OrderBy(pathId => pathId).ToList(),
                    BonePathIds: rootBones.Select(bone => bone.PathId).OrderBy(pathId => pathId).ToList(),
                    ColliderIndexes: rootColliders.Select(collider => collider.Index).OrderBy(index => index).ToList(),
                    RendererPathIds: rendererPathIds,
                    Reason: root == defaultBodyRoot
                        ? "default body root candidate; not a final runtime active decision"
                        : "prefab root candidate; runtime selection must choose one compatible root per spring bone"
                );
            })
            .ToList();

        return new PjskSpringBoneRootSelectionProfile(
            Policy: "candidates_plus_default; static active is evidence only",
            DefaultBodyRoot: defaultBodyRoot,
            RootCandidates: candidates
        );
    }

    private static int RootPriority(string root, string defaultBodyRoot)
    {
        if (string.Equals(root, defaultBodyRoot, StringComparison.Ordinal))
        {
            return 0;
        }
        return root switch
        {
            "face" => 10,
            "sit_body" => 20,
            "guitar_body" => 30,
            _ => 100,
        };
    }

    private static PjskSpringBoneSetupPlan BuildSetupPlan(
        IReadOnlyList<PjskSpringBoneRuntimeManager> managers,
        IReadOnlyList<PjskSpringBoneRuntimeColliderBinding> bindings
    )
    {
        return new PjskSpringBoneSetupPlan(
            DiscoveryMode: "Unity prefab PathID graph; SpringManager.springBones references remain authoritative",
            RootPolicy: "managerColliderCaches are the collider binding anchor; viewer/runtime must constrain colliderFlag candidates by manager cache before selecting one body/sit_body/guitar_body root",
            ManagerPathIds: managers.Select(manager => manager.PathId).OrderBy(pathId => pathId).ToList(),
            OrderedSteps: new[]
            {
                "ModelUtility.ModelCombineSetup",
                "CharacterModel.SetupSpringBone",
                "ModelUtility.SpringBoneSetup",
                "ModelUtility.ConstraintSetup",
                "SpringManager.FindSpringBones(true)",
                "SpringManager.SetupCollider",
                "SpringBone.Initialize",
            },
            DirectBindingCount: bindings.Count(binding => binding.SourceKind == "direct"),
            ColliderFlagBindingCount: bindings.Count(binding => binding.SourceKind == "colliderFlag")
        );
    }

    public static IReadOnlyList<PjskSpringBoneBindingDecision> BuildBindingDecisions(
        IReadOnlyList<PjskSpringBoneRuntimeBone> bones,
        IReadOnlyList<PjskSpringBoneRuntimeColliderBinding> bindings
    )
    {
        var boneByPathId = bones.ToDictionary(bone => bone.PathId);
        return bindings
            .Select(binding =>
            {
                boneByPathId.TryGetValue(binding.SourceSpringBonePathId, out var bone);
                var candidateRoots = binding.CollidersByRoot is not null && binding.CollidersByRoot.Count > 0
                    ? binding.CollidersByRoot
                    : new Dictionary<string, IReadOnlyList<int>>
                    {
                        [binding.DefaultRoot ?? bone?.PoseRoot ?? "unknown"] = binding.Colliders,
                    };
                return new PjskSpringBoneBindingDecision(
                    SourceKind: binding.SourceKind,
                    PartKind: binding.PartKind,
                    SourceSpringBonePathId: binding.SourceSpringBonePathId,
                    NodePath: bone?.NodePath,
                    PoseRoot: bone?.PoseRoot,
                    ColliderFlag: binding.ColliderFlag,
                    DirectColliderPathIds: binding.SourceKind == "direct"
                        ? binding.SourceColliderPathIds
                        : Array.Empty<long>(),
                    CandidateRoots: candidateRoots,
                    DefaultRoot: binding.DefaultRoot,
                    SelectedColliderIndexes: binding.Colliders,
                    Reason: binding.SourceKind == "colliderFlag"
                        ? "colliderFlag binding exports all root candidates; viewer must constrain by managerColliderCaches before selecting one root"
                        : "direct serialized collider references"
                );
            })
            .ToList();
    }

    private static IReadOnlyList<PjskSpringBoneRuntimeManagerColliderCache> BuildManagerColliderCaches(
        IReadOnlyList<PjskSpringBoneRuntimeManager> managers,
        IReadOnlyList<PjskSpringBoneRuntimeCollider> colliders
    )
    {
        return managers
            .Select(manager =>
            {
                var cacheColliders = SelectRuntimeManagerCacheColliders(manager, colliders);
                return new PjskSpringBoneRuntimeManagerColliderCache(
                    ManagerPathId: manager.PathId,
                    PartKind: manager.PartKind,
                    SourcePoseRoot: manager.PoseRoot,
                    RuntimeRoot: "CharacterModel",
                    ManagerNodeName: manager.NodeName,
                    ManagerNodePath: manager.NodePath,
                    SpringBonePathIds: manager.BonePathIds,
                    SphereColliderIndexes: cacheColliders
                        .Where(collider => IsSpringColliderScript(collider, "SpringSphereCollider"))
                        .Select(collider => collider.Index)
                        .Distinct()
                        .OrderBy(index => index)
                        .ToList(),
                    CapsuleColliderIndexes: cacheColliders
                        .Where(collider => IsSpringColliderScript(collider, "SpringCapsuleCollider"))
                        .Select(collider => collider.Index)
                        .Distinct()
                        .OrderBy(index => index)
                        .ToList(),
                    PanelColliderIndexes: cacheColliders
                        .Where(collider => IsSpringColliderScript(collider, "SpringPanelCollider"))
                        .Select(collider => collider.Index)
                        .Distinct()
                        .OrderBy(index => index)
                        .ToList(),
                    Reason: BuildManagerColliderCacheReason(manager)
                );
            })
            .ToList();
    }

    private static IReadOnlyList<PjskSpringBoneRuntimeCollider> SelectRuntimeManagerCacheColliders(
        PjskSpringBoneRuntimeManager manager,
        IReadOnlyList<PjskSpringBoneRuntimeCollider> colliders
    )
    {
        var managerName = manager.NodeName ?? string.Empty;
        var sourceRoot = manager.PoseRoot;
        var sameRootColliders = colliders
            .Where(collider => string.Equals(collider.PoseRoot, sourceRoot, StringComparison.Ordinal))
            .Where(collider => collider.Enabled)
            .ToList();

        if (string.Equals(managerName, "Neck", StringComparison.Ordinal))
        {
            return colliders
                .Where(collider => string.Equals(collider.PoseRoot, "body", StringComparison.Ordinal))
                .Where(collider => collider.Enabled)
                .Where(IsNeckRuntimeCacheCollider)
                .ToList();
        }

        if (string.Equals(managerName, "Hip", StringComparison.Ordinal))
        {
            return sameRootColliders;
        }

        return sameRootColliders;
    }

    private static bool IsNeckRuntimeCacheCollider(PjskSpringBoneRuntimeCollider collider)
    {
        return StartsWithColliderName(collider, "CL_Chest") ||
            StartsWithColliderName(collider, "CL_Left_Arm") ||
            StartsWithColliderName(collider, "CL_Right_Arm");
    }

    private static bool StartsWithColliderName(
        PjskSpringBoneRuntimeCollider collider,
        string prefix
    )
    {
        var name = collider.NodeName ?? string.Empty;
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpringColliderScript(
        PjskSpringBoneRuntimeCollider collider,
        string scriptName
    )
    {
        return string.Equals(collider.ScriptName, scriptName, StringComparison.Ordinal) ||
            collider.ScriptName.EndsWith(scriptName, StringComparison.Ordinal);
    }

    private static string BuildManagerColliderCacheReason(PjskSpringBoneRuntimeManager manager)
    {
        return manager.NodeName switch
        {
            "Neck" => "6.5.5 Frida rootmap: Neck runtime cache resolves body CharacterModel chest/head/arm CL_* colliders for head/face spring bones.",
            "Hip" => "6.5.5 Frida rootmap: Hip runtime cache resolves active same-root body colliders; colliderFlag binding must intersect this cache before root selection.",
            _ => "Conservative fallback: active colliders under the same static pose root as the manager."
        };
    }

    private static PjskSpringBoneRuntimeManager BuildRuntimeManager(
        string partKind,
        SpringMonoBehaviourEntry manager,
        SpringBoneExport part
    )
    {
        var managerPath = manager.GameObject?.TransformPath;
        var bonePathIds = part.Bones
            .Where(bone => IsSameOrDescendant(bone.GameObject?.TransformPath, managerPath))
            .OrderBy(bone => PathDepth(bone.GameObject?.TransformPath))
            .ThenBy(bone => bone.GameObject?.TransformPath, StringComparer.Ordinal)
            .Select(bone => bone.PathId)
            .ToList();
        return new PjskSpringBoneRuntimeManager(
            PartKind: partKind,
            PathId: manager.PathId,
            NodeName: manager.GameObject?.Name,
            NodePath: managerPath,
            PoseRoot: ExtractPoseRoot(managerPath),
            ActiveSelf: manager.GameObject?.ActiveSelf,
            ActiveInHierarchy: manager.GameObject?.ActiveInHierarchy,
            Enabled: ReadBool(manager.Raw, "m_Enabled") ?? true,
            AutomaticUpdates: ReadBool(manager.Raw, "automaticUpdates") ?? true,
            EnableLengthLimits: ReadBool(manager.Raw, "enableLengthLimits") ?? true,
            EnableAngleLimits: ReadBool(manager.Raw, "enableAngleLimits") ?? true,
            EnableCollision: ReadBool(manager.Raw, "enableCollision") ?? true,
            CollideWithGround: ReadBool(manager.Raw, "collideWithGround") ?? true,
            GroundHeight: ReadFloat(manager.Raw, "groundHeight") ?? 0f,
            IsSumOfForcesOnBone: ReadBool(manager.Raw, "isSumOfForcesOnBone") ?? true,
            IsPaused: ReadBool(manager.Raw, "isPaused") ?? false,
            DynamicRatio: Clamp01(ReadFloat(manager.Raw, "dynamicRatio") ?? 0.5f),
            SimulationFrameRate: Math.Max(0, ReadInt(manager.Raw, "simulationFrameRate") ?? 60),
            SlowMotionScale: ReadFloat(manager.Raw, "slowMotionScale") ?? 1f,
            Bounce: ReadFloat(manager.Raw, "bounce") ?? 0f,
            Friction: ReadFloat(manager.Raw, "friction") ?? 1f,
            AnimatedBoneNames: ReadStringList(manager.Raw, "animatedBoneNames"),
            RawGravity: ReadVector3(manager.Raw, "gravity"),
            ForceProviders: BuildRuntimeForceProviders(part, manager),
            BonePathIds: bonePathIds
        );
    }

    private static PjskSpringBoneRuntimeBone BuildRuntimeBone(string partKind, SpringBoneEntry bone)
    {
        var springForce = bone.SpringForce ?? new SpringVector3(0f, 0f, 0f);
        var gravityPower = Magnitude(springForce);
        var gravityDir = gravityPower > 0.00001f
            ? new[]
            {
                springForce.X / gravityPower,
                springForce.Y / gravityPower,
                springForce.Z / gravityPower,
            }
            : new[] { 0f, -1f, 0f };

        return new PjskSpringBoneRuntimeBone(
            PartKind: partKind,
            PathId: bone.PathId,
            NodeName: bone.GameObject?.Name,
            NodePath: bone.GameObject?.TransformPath,
            PoseRoot: ExtractPoseRoot(bone.GameObject?.TransformPath),
            ActiveSelf: bone.GameObject?.ActiveSelf,
            ActiveInHierarchy: bone.GameObject?.ActiveInHierarchy,
            Enabled: ReadBool(bone.Raw, "m_Enabled") ?? true,
            PivotNodePath: bone.PivotNode?.TransformPath,
            PivotNodeName: bone.PivotNode?.Name,
            PivotSourcePathId: bone.PivotNode?.PathId,
            HitRadius: MathF.Max(0f, bone.Radius ?? 0.05f),
            Stiffness: Clamp((bone.StiffnessForce ?? 300f) / 300f, 0f, 4f),
            DragForce: Clamp(bone.DragForce ?? 0.4f, 0f, 1f),
            GravityPower: gravityPower,
            GravityDir: gravityDir,
            RawStiffnessForce: bone.StiffnessForce,
            RawDragForce: bone.DragForce,
            RawSpringForce: bone.SpringForce,
            RawWindInfluence: bone.WindInfluence,
            RawAngularStiffness: ReadFloat(bone.Raw, "angularStiffness"),
            RawSpringConstant: ReadFloat(bone.Raw, "SpringConstant"),
            LengthLimitTargets: bone.LengthLimitTargets
                .Select(target => new VrmSpringBoneLengthLimitTargetCandidate(
                    NodeName: target.Name,
                    NodePath: target.TransformPath,
                    SourcePathId: target.PathId
                ))
                .ToList(),
            RawAngleLimits: new VrmSpringBoneAngleLimitsCandidate(
                Y: ReadAxisLimit(bone.Raw, "yAngleLimits"),
                Z: ReadAxisLimit(bone.Raw, "zAngleLimits")
            ),
            DirectColliderPathIds: ReadObjectPathIds(bone.Raw, "colliders")
                .Concat(ReadObjectPathIds(bone.Raw, "sphereColliders"))
                .Concat(ReadObjectPathIds(bone.Raw, "capsuleColliders"))
                .Concat(ReadObjectPathIds(bone.Raw, "panelColliders"))
                .Distinct()
                .ToList(),
            ColliderFlag: ReadInt(bone.Raw, "colliderFlag") ?? 0
        );
    }

    private static IReadOnlyList<VrmSpringBoneForceProviderCandidate> BuildRuntimeForceProviders(
        SpringBoneExport part,
        SpringMonoBehaviourEntry manager
    )
    {
        return part.ForceProviders
            .Where(provider =>
            {
                var managerPathId = ReadObjectPathIds(provider.Raw, "springManager")
                    .Concat(ReadObjectPathIds(provider.Raw, "springManagers"))
                    .FirstOrDefault();
                return managerPathId == manager.PathId ||
                    (managerPathId == 0 && string.Equals(
                        ExtractPoseRoot(provider.GameObject?.TransformPath),
                        ExtractPoseRoot(manager.GameObject?.TransformPath),
                        StringComparison.Ordinal));
            })
            .Select(provider => new VrmSpringBoneForceProviderCandidate(
                SourcePathId: provider.PathId,
                ScriptName: provider.ScriptName,
                NodeName: provider.GameObject?.Name,
                NodePath: provider.GameObject?.TransformPath,
                ActiveSelf: provider.GameObject?.ActiveSelf,
                ActiveInHierarchy: provider.GameObject?.ActiveInHierarchy,
                SpringManagerPathId: manager.PathId,
                Raw: provider.Raw
            ))
            .ToList();
    }

    private static bool IsSameOrDescendant(string? path, string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }
        return string.Equals(path, rootPath, StringComparison.Ordinal) ||
            path.StartsWith(rootPath + "/", StringComparison.Ordinal);
    }

    private static int PathDepth(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? int.MaxValue
            : path.Count(ch => ch == '/');
    }

    private static string? ExtractPoseRoot(string? transformPath)
    {
        if (string.IsNullOrWhiteSpace(transformPath))
        {
            return null;
        }
        var slashIndex = transformPath.IndexOf('/');
        return slashIndex < 0
            ? transformPath
            : transformPath[..slashIndex];
    }

    private static IEnumerable<long> ReadObjectPathIds(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value) || value is not JsonArray array)
        {
            return Array.Empty<long>();
        }
        return array
            .OfType<JsonObject>()
            .Select(item => ReadLong(item, "m_PathID"))
            .Where(pathId => pathId is not null && pathId.Value != 0)
            .Select(pathId => pathId!.Value);
    }

    private static bool? ReadBool(JsonObject raw, string key)
    {
        return raw.TryGetPropertyValue(key, out var value)
            ? ReadBool(value)
            : null;
    }

    private static bool? ReadBool(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node.GetValueKind() == System.Text.Json.JsonValueKind.True)
        {
            return true;
        }
        if (node.GetValueKind() == System.Text.Json.JsonValueKind.False)
        {
            return false;
        }
        return ReadLongValue(node) switch
        {
            0 => false,
            1 => true,
            _ => null,
        };
    }

    private static int? ReadInt(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value))
        {
            return null;
        }
        var longValue = ReadLongValue(value);
        return longValue is null ? null : (int)longValue.Value;
    }

    private static float? ReadFloat(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value) || value is null)
        {
            return null;
        }
        try
        {
            return value.GetValue<float>();
        }
        catch
        {
            try
            {
                return (float)value.GetValue<double>();
            }
            catch
            {
                return null;
            }
        }
    }

    private static SpringVector3? ReadVector3(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value) || value is not JsonObject vector)
        {
            return null;
        }
        return new SpringVector3(
            ReadFloat(vector, "x") ?? ReadFloat(vector, "X") ?? 0f,
            ReadFloat(vector, "y") ?? ReadFloat(vector, "Y") ?? 0f,
            ReadFloat(vector, "z") ?? ReadFloat(vector, "Z") ?? 0f
        );
    }

    private static IReadOnlyList<string> ReadStringList(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value) || value is not JsonArray array)
        {
            return Array.Empty<string>();
        }
        return array
            .Select(item =>
            {
                try
                {
                    return item?.GetValue<string>();
                }
                catch
                {
                    return null;
                }
            })
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToList();
    }

    private static VrmSpringBoneAxisLimitCandidate? ReadAxisLimit(JsonObject raw, string key)
    {
        if (!raw.TryGetPropertyValue(key, out var value) || value is not JsonObject axis)
        {
            return null;
        }
        return new VrmSpringBoneAxisLimitCandidate(
            Active: ReadBool(axis, "active") ?? ReadBool(axis, "enabled") ?? false,
            Min: ReadFloat(axis, "min"),
            Max: ReadFloat(axis, "max")
        );
    }

    private static float Magnitude(SpringVector3 vector)
    {
        return MathF.Sqrt(vector.X * vector.X + vector.Y * vector.Y + vector.Z * vector.Z);
    }

    private static float Clamp01(float value)
    {
        return Clamp(value, 0f, 1f);
    }

    private static float Clamp(float value, float min, float max)
    {
        return MathF.Min(max, MathF.Max(min, value));
    }

    private static long? ReadLong(JsonObject raw, string key)
    {
        return raw.TryGetPropertyValue(key, out var value)
            ? ReadLongValue(value)
            : null;
    }

    private static long? ReadLongValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        try
        {
            return node.GetValue<long>();
        }
        catch
        {
            try
            {
                return node.GetValue<int>();
            }
            catch
            {
                return null;
            }
        }
    }

    private static PjskSekaiRuntimeCostumeMetadata? BuildCostumeMetadata(
        ResolvedCharacter3dCostume? costume
    )
    {
        if (costume is null)
        {
            return null;
        }

        return new PjskSekaiRuntimeCostumeMetadata(
            Character3dId: costume.Character3dId,
            CharacterName: costume.CharacterName,
            Unit: costume.Unit,
            BodyCostume3dId: costume.BodyCostume3dId,
            BodyAssetbundleName: costume.BodyAssetbundleName,
            BodyColorAssetbundleName: costume.BodyColorAssetbundleName,
            BodyColorVariationPath: costume.BodyColorVariationPath,
            HairCostume3dId: costume.HairCostume3dId,
            HairAssetbundleName: costume.HairAssetbundleName,
            HairColorAssetbundleName: costume.HairColorAssetbundleName,
            HairBundleKind: costume.HairBundleKind,
            HairVariantGroupKey: costume.HairVariantGroupKey,
            HeadCostume3dId: costume.HeadCostume3dId,
            HeadAssetbundleName: costume.HeadAssetbundleName,
            HeadColorAssetbundleName: costume.HeadColorAssetbundleName,
            HeadBundleKind: costume.HeadBundleKind,
            HeadVariantGroupKey: costume.HeadVariantGroupKey,
            HeadCompositionKind: costume.HeadCompositionKind,
            MainHeadAssetbundleName: costume.MainHeadAssetbundleName,
            MainHeadColorVariationPath: costume.MainHeadColorVariationPath,
            MainHeadMode: costume.MainHeadMode,
            MainHeadCostumeType: costume.MainHeadCostumeType,
            HeadTextureFallbackAssetbundleName: costume.HeadTextureFallbackAssetbundleName,
            AccessoryHeadAssetbundleName: costume.AccessoryHeadAssetbundleName,
            AccessoryAttachNode: costume.AccessoryAttachNode,
            AccessoryColorAssetbundleName: costume.AccessoryColorAssetbundleName,
            AccessoryColorVariationPath: costume.AccessoryColorVariationPath
        );
    }

    private static void AddTextureRole(
        List<PjskSekaiRuntimeTextureRole> textureRoles,
        List<PjskSekaiRuntimeMissingTextureRole> missingTextureRoles,
        PjskSekaiRuntimeMaterialSlot slot,
        string role,
        string? uri
    )
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            if (IsRequiredTextureRole(slot.MaterialKind, role))
            {
                missingTextureRoles.Add(new PjskSekaiRuntimeMissingTextureRole(
                    Part: slot.Part,
                    MeshName: slot.MeshName,
                    SlotIndex: slot.SlotIndex,
                    MaterialKey: slot.MaterialKey,
                    MaterialFileId: slot.MaterialFileId,
                    MaterialPathId: slot.MaterialPathId,
                    MaterialName: slot.MaterialName,
                    MaterialKind: slot.MaterialKind,
                    Role: role
                ));
            }
            return;
        }

        textureRoles.Add(new PjskSekaiRuntimeTextureRole(
            Part: slot.Part,
            MaterialKey: slot.MaterialKey,
            MaterialFileId: slot.MaterialFileId,
            MaterialPathId: slot.MaterialPathId,
            MaterialName: slot.MaterialName,
            MaterialKind: slot.MaterialKind,
            Role: role,
            Uri: uri
        ));
    }

    private static IReadOnlyList<PjskSekaiRuntimeMaterialSlot> BuildAccessoryMaterialSlots(
        ConversionPlan plan,
        BundleInventory? accessoryInventory,
        IReadOnlyDictionary<string, string> characterTexturePathByName
    )
    {
        if (accessoryInventory is null)
        {
            return Array.Empty<PjskSekaiRuntimeMaterialSlot>();
        }

        var materialLookup = MaterialIdentityLookup.FromInventory(accessoryInventory.Materials);
        return accessoryInventory.SkinnedMeshes
            .Concat(accessoryInventory.StaticMeshes)
            .SelectMany(mesh => mesh.MaterialSlots.Select(slot =>
            {
                var material = materialLookup.Require(slot);
                return new PjskSekaiRuntimeMaterialSlot(
                    Part: "accessory",
                    MeshName: mesh.MeshName,
                    SlotIndex: slot.SlotIndex,
                    MaterialKey: slot.MaterialKey,
                    MaterialFileId: slot.MaterialFileId,
                    MaterialPathId: slot.MaterialPathId,
                    MaterialName: slot.MaterialName,
                    MaterialKind: "accessory",
                    MainTex: RewriteCharacterTexturePath(
                        "accessory",
                        FindTextureSlot(material, "_MainTex"),
                        characterTexturePathByName
                    ),
                    ShadowTex: RewriteCharacterTexturePath(
                        "accessory",
                        FindTextureSlot(material, "_ShadowTex"),
                        characterTexturePathByName
                    ),
                    ValueTex: null,
                    FaceShadowTex: null,
                    RenderOrder: GetHeadRenderOrder("accessory"),
                    ShaderPipeline: plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.BodyPipeline,
                    Lighting: BuildLightingSettings(material)
                );
            }))
            .DistinctBy(
                slot => $"{slot.MeshName}::{slot.SlotIndex}::{slot.MaterialKey}",
                StringComparer.Ordinal
            )
            .ToList();
    }

    private static MaterialLightingSettings BuildLightingSettings(MaterialInventory? material)
    {
        return new MaterialLightingSettings(
            SpecularPower: FindFloatProperty(material, "_SpecularPower") ?? 0f,
            RimThreshold:
                FindFloatProperty(material, "_SpecularStrength") ??
                FindFloatProperty(material, "_RimThreshold") ??
                0.2f,
            ShadowTexWeight: FindFloatProperty(material, "_ShadowTexWeight") ?? 1f,
            Saturation: FindFloatProperty(material, "_Saturation") ?? 0.5f,
            PartsAmbientColor: FindColorProperty(material, "_PartsAmbientColor") ?? "#ffffff",
            ReflectionBlendColor: FindColorProperty(material, "_ReflectionBlendColor") ?? "#ffffff",
            OutlineWidth: FindFloatProperty(material, "_OutlineWidth") ?? 0.001f,
            OutlineOffset: FindFloatProperty(material, "_OutlineOffset") ?? 0f,
            OutlineLightness: FindFloatProperty(material, "_OutlineL") ?? 0.5f,
            ShadowWidth: FindFloatProperty(material, "_ShadowWidth") ?? 0f,
            UseOutlineSecondNormal: FindFloatProperty(material, "_UseOutlineSecondNormal") ?? 0f,
            DistortionFps: FindFloatProperty(material, "_DistortionFPS") ?? 12f,
            DistortionIntensity: FindFloatProperty(material, "_DistortionIntensity") ?? 0f,
            DistortionIntensityX: FindFloatProperty(material, "_DistortionIntensityX") ?? 0f,
            DistortionIntensityY: FindFloatProperty(material, "_DistortionIntensityY") ?? 0f,
            DistortionOffsetX: FindFloatProperty(material, "_DistortionOffsetX") ?? 0f,
            DistortionOffsetY: FindFloatProperty(material, "_DistortionOffsetY") ?? 0f,
            DistortionScrollSpeed: FindFloatProperty(material, "_DistortionScrollSpeed") ?? 1f,
            DistortionScrollX: FindFloatProperty(material, "_DistortionScrollX") ?? 0f,
            DistortionScrollY: FindFloatProperty(material, "_DistortionScrollY") ?? 0f,
            DistortionTexTilingX: FindFloatProperty(material, "_DistortionTexTilingX") ?? 1f,
            DistortionTexTilingY: FindFloatProperty(material, "_DistortionTexTilingY") ?? 1f,
            Threshold: FindFloatProperty(material, "_Threshold") ?? 0.5f,
            LightInfluence: FindFloatProperty(material, "_LightInfluence") ?? 1f,
            LightInfluenceForEyeHighlight: FindFloatProperty(material, "_LightInfluenceForEyeHighlight") ?? 1f
        );
    }

    private static float? FindFloatProperty(MaterialInventory? material, string propertyName)
    {
        return material?.FloatProperties
            .FirstOrDefault(entry => string.Equals(entry.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static string? FindTextureSlot(MaterialInventory? material, string slotName)
    {
        return material?.TextureSlots
            .FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase))
            ?.TextureName;
    }

    private static string? FindColorProperty(MaterialInventory? material, string propertyName)
    {
        var color = material?.ColorProperties
            .FirstOrDefault(entry => string.Equals(entry.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return color is null ? null : ToHex(color.R, color.G, color.B);
    }

    private static string ToHex(float r, float g, float b)
    {
        static int ClampByte(float value) => Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        return $"#{ClampByte(r):X2}{ClampByte(g):X2}{ClampByte(b):X2}".ToLowerInvariant();
    }

    private static bool IsRequiredTextureRole(string materialKind, string role)
    {
        return role switch
        {
            "main" => true,
            "shadow" => materialKind is "body" or "hair" or "accessory" or "face_sdf",
            "value" => materialKind is "body",
            "faceShadow" => materialKind is "face_sdf",
            _ => false,
        };
    }

    private static string ResolveHeadShaderPipeline(
        ConversionPlan plan,
        string materialKind
    )
    {
        return materialKind switch
        {
            "eye" or "eyelight" or "eyelash" or "eyebrow" => plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.LayerPipeline,
            "face_sdf" or "face" => plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.FacePipeline,
            _ => plan.SekaiVrmProfile.SekaiRuntimeMaterialProfile.BodyPipeline,
        };
    }

    private static int GetHeadRenderOrder(string materialKind)
    {
        return materialKind switch
        {
            "face_sdf" => 10,
            "accessory" or "hair" => 12,
            "eyelash" or "eyebrow" => 20,
            "eye" => 24,
            "eyelight" => 28,
            _ => 0,
        };
    }

    private static string? RewriteCharacterTexturePath(
        string part,
        string? manifestPath,
        IReadOnlyDictionary<string, string> characterTexturePathByName
    )
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            return null;
        }

        var textureName = Path.GetFileName(manifestPath);
        var prefixedName = $"{part}_{textureName}";
        var textureStem = Path.GetFileNameWithoutExtension(textureName);
        var prefixedStem = $"{part}_{textureStem}";
        if (characterTexturePathByName.TryGetValue(prefixedName, out var prefixedPath))
        {
            return ToOutputRootTexturePath(prefixedPath);
        }
        if (!string.IsNullOrWhiteSpace(prefixedStem) &&
            characterTexturePathByName.TryGetValue(prefixedStem, out var prefixedStemPath))
        {
            return ToOutputRootTexturePath(prefixedStemPath);
        }
        if (characterTexturePathByName.TryGetValue(textureName, out var directPath))
        {
            return ToOutputRootTexturePath(directPath);
        }
        if (!string.IsNullOrWhiteSpace(textureStem) &&
            characterTexturePathByName.TryGetValue(textureStem, out var directStemPath))
        {
            return ToOutputRootTexturePath(directStemPath);
        }

        return manifestPath;
    }

    private static string ToOutputRootTexturePath(string characterRelativePath)
    {
        return Path.Combine("character", characterRelativePath).Replace('\\', '/');
    }
}
