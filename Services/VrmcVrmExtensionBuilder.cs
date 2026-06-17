using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public sealed class VrmcVrmExtensionBuilder
{
    private static readonly string[] RequiredHumanBones =
    {
        "hips",
        "spine",
        "head",
        "leftUpperLeg",
        "leftLowerLeg",
        "leftFoot",
        "rightUpperLeg",
        "rightLowerLeg",
        "rightFoot",
        "leftUpperArm",
        "leftLowerArm",
        "leftHand",
        "rightUpperArm",
        "rightLowerArm",
        "rightHand",
    };

    private static readonly string[] PresetExpressionNames =
    {
        "happy",
        "angry",
        "sad",
        "relaxed",
        "surprised",
        "aa",
        "ih",
        "ou",
        "ee",
        "oh",
        "blink",
        "blinkLeft",
        "blinkRight",
        "lookUp",
        "lookDown",
        "lookLeft",
        "lookRight",
        "neutral",
    };

    public VrmcVrmBuildResult Build(
        SekaiVrmMigrationProfile profile,
        ConversionPlanSummary summary,
        string glbPath
    )
    {
        var nodeResolver = GltfNodePathResolver.FromGlb(glbPath);
        var resolvedHumanBones = new Dictionary<string, VrmcVrmHumanBone>(StringComparer.Ordinal);
        var resolvedReport = new List<VrmcVrmHumanBoneResolve>();
        var skipped = new List<VrmcVrmSkippedHumanBone>();
        var warnings = new List<string>();

        foreach (var mapping in profile.StandardVrmFallback.HumanoidBoneMap.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (nodeResolver.TryResolveName(mapping.Value, out var resolved, out var matchMode))
            {
                resolvedHumanBones[mapping.Key] = new VrmcVrmHumanBone(resolved.NodeIndex);
                resolvedReport.Add(new VrmcVrmHumanBoneResolve(
                    HumanBone: mapping.Key,
                    SourceNodeName: mapping.Value,
                    ResolvedNode: resolved.NodeIndex,
                    ResolvedPath: resolved.NodePath,
                    MatchMode: matchMode
                ));
                continue;
            }

            skipped.Add(new VrmcVrmSkippedHumanBone(
                HumanBone: mapping.Key,
                SourceNodeName: mapping.Value,
                Reason: matchMode
            ));
        }

        var missingRequired = RequiredHumanBones
            .Where(required => !resolvedHumanBones.ContainsKey(required))
            .ToList();
        if (missingRequired.Count > 0)
        {
            warnings.Add(
                "VRMC_vrm humanoid is incomplete; missing required bones: " +
                string.Join(", ", missingRequired)
            );
        }

        var extension = new VrmcVrmExtension(
            SpecVersion: "1.0",
            Meta: BuildMeta(summary),
            Humanoid: new VrmcVrmHumanoid(resolvedHumanBones),
            FirstPerson: new VrmcVrmFirstPerson(Array.Empty<VrmcVrmMeshAnnotation>()),
            LookAt: BuildLookAt(),
            Expressions: BuildExpressions()
        );
        var report = new VrmcVrmResolveReport(
            Version: 1,
            GlbPath: glbPath,
            NodeCount: nodeResolver.NodeCount,
            CandidateHumanBoneCount: profile.StandardVrmFallback.HumanoidBoneMap.Count,
            ResolvedHumanBoneCount: resolvedHumanBones.Count,
            RequiredHumanBones: RequiredHumanBones,
            MissingRequiredHumanBones: missingRequired,
            ResolvedHumanBones: resolvedReport
                .OrderBy(x => x.HumanBone, StringComparer.Ordinal)
                .ToList(),
            SkippedHumanBones: skipped
                .OrderBy(x => x.HumanBone, StringComparer.Ordinal)
                .ToList(),
            Warnings: warnings
        );

        return new VrmcVrmBuildResult(extension, report);
    }

    private static VrmcVrmMeta BuildMeta(ConversionPlanSummary summary)
    {
        return new VrmcVrmMeta(
            Name: $"PJSK character {summary.CharacterId}",
            Version: "0.0.0",
            Authors: new[] { "PjskBundle2Parts" },
            CopyrightInformation: "Source assets belong to their respective rights holders.",
            ContactInformation: string.Empty,
            References: new[] { "PJSK AssetBundle conversion candidate" },
            ThirdPartyLicenses: string.Empty,
            LicenseUrl: "https://vrm.dev/licenses/1.0/",
            AvatarPermission: "onlyAuthor",
            AllowExcessivelyViolentUsage: false,
            AllowExcessivelySexualUsage: false,
            CommercialUsage: "personalNonProfit",
            AllowPoliticalOrReligiousUsage: false,
            AllowAntisocialOrHateUsage: false,
            CreditNotation: "required",
            AllowRedistribution: false,
            Modification: "prohibited",
            OtherLicenseUrl: string.Empty
        );
    }

    private static VrmcVrmLookAt BuildLookAt()
    {
        return new VrmcVrmLookAt(
            OffsetFromHeadBone: new[] { 0f, 0.06f, 0.02f },
            Type: "bone",
            RangeMapHorizontalInner: new VrmcVrmLookAtRangeMap(90f, 10f),
            RangeMapHorizontalOuter: new VrmcVrmLookAtRangeMap(90f, 10f),
            RangeMapVerticalDown: new VrmcVrmLookAtRangeMap(90f, 10f),
            RangeMapVerticalUp: new VrmcVrmLookAtRangeMap(90f, 10f)
        );
    }

    private static VrmcVrmExpressions BuildExpressions()
    {
        var presets = PresetExpressionNames.ToDictionary(
            name => name,
            _ => BuildEmptyExpression(),
            StringComparer.Ordinal
        );
        return new VrmcVrmExpressions(
            Preset: presets,
            Custom: new Dictionary<string, VrmcVrmExpression>(StringComparer.Ordinal)
        );
    }

    private static VrmcVrmExpression BuildEmptyExpression()
    {
        return new VrmcVrmExpression(
            MorphTargetBinds: Array.Empty<VrmcVrmMorphTargetBind>(),
            MaterialColorBinds: Array.Empty<VrmcVrmMaterialColorBind>(),
            TextureTransformBinds: Array.Empty<VrmcVrmTextureTransformBind>(),
            IsBinary: false,
            OverrideBlink: "none",
            OverrideLookAt: "none",
            OverrideMouth: "none"
        );
    }
}
