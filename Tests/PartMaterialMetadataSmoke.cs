using PjskBundle2Parts.Models;
using PjskBundle2Parts.Services;
using System.Text.Json;

namespace PjskBundle2Parts.Tests;

public static class PartMaterialMetadataSmoke
{
    public static void Run()
    {
        var bodyMaterial = SkinMaterial("body_skin");
        var faceMaterial = SkinMaterial("face_skin");

        var bodyProxy = SekaiMaterialMetadata.BuildBodyProxy(new[] { bodyMaterial });
        Expect(bodyProxy.BodyColor == "#fdf5eb", "body proxy uses exported default skin color");
        Expect(bodyProxy.ShadowColor == "#e3c4cb", "body proxy uses exported shadow skin color");
        var bodyManifestJson = JsonSerializer.Serialize(new
        {
            proxy = new
            {
                bodyColor = bodyProxy.BodyColor,
                shadowColor = bodyProxy.ShadowColor,
                bodyScale = bodyProxy.BodyScale,
                torsoLength = bodyProxy.TorsoLength,
                shoulderWidth = bodyProxy.ShoulderWidth,
            },
        });
        Expect(bodyManifestJson.Contains("\"proxy\""), "part manifest writes a proxy object");
        Expect(bodyManifestJson.Contains("\"bodyColor\":\"#fdf5eb\""), "part body manifest writes exported body color");
        Expect(bodyManifestJson.Contains("\"shadowColor\":\"#e3c4cb\""), "part body manifest writes exported shadow color");

        var headProxy = SekaiMaterialMetadata.BuildHeadProxy(new[] { faceMaterial });
        Expect(headProxy.FaceColor == "#fdf5eb", "head proxy uses exported default skin color");
        Expect(headProxy.FaceShadeColor == "#e3c4cb", "head proxy uses exported first shadow skin color");
        Expect(headProxy.SkinColor2 == "#cb97a2", "head proxy uses exported second shadow skin color");
        var headManifestJson = JsonSerializer.Serialize(new
        {
            proxy = new
            {
                faceColor = headProxy.FaceColor,
                faceShadeColor = headProxy.FaceShadeColor,
                skinColorDefault = headProxy.SkinColorDefault,
                skinColor1 = headProxy.SkinColor1,
                skinColor2 = headProxy.SkinColor2,
                hairColor = headProxy.HairColor,
                hairShadowColor = headProxy.HairShadowColor,
            },
        });
        Expect(headManifestJson.Contains("\"faceColor\":\"#fdf5eb\""), "part head manifest writes exported face color");
        Expect(headManifestJson.Contains("\"skinColor2\":\"#cb97a2\""), "part head manifest writes exported second shadow color");

        var lighting = SekaiMaterialMetadata.BuildLightingSettings(bodyMaterial);
        Expect(lighting.FadeMode == 1, "lighting reads fade mode");
        Expect(Math.Abs(lighting.HueSinAngle - 0.25f) < 0.0001f, "lighting reads hue sin angle");
        Expect(Math.Abs(lighting.HueCosAngle - 0.75f) < 0.0001f, "lighting reads hue cos angle");
        Expect(Math.Abs(lighting.Saturation - 0.5f) < 0.0001f, "lighting reads saturation");
        Expect(Math.Abs(lighting.Value - 0.6f) < 0.0001f, "lighting reads value");
        Expect(Math.Abs(lighting.Contrast - 0.7f) < 0.0001f, "lighting reads contrast");
        Expect(Math.Abs(lighting.OutlineWidth - 0.001f) < 0.0001f, "lighting reads outline width");
        Expect(Math.Abs(lighting.DistortionFps - 12f) < 0.0001f, "lighting reads distortion FPS");
        Expect(Math.Abs(lighting.LightInfluence - 1f) < 0.0001f, "lighting reads light influence");
        Expect(Math.Abs(lighting.SekaiShadowThreshold!.Value - 0.40625f) < 0.0001f, "lighting reads the official Sekai shadow threshold");
        Expect(lighting.UseLambert == true, "lighting preserves the official Lambert feature state");
        Expect(lighting.UseValueTex == true, "lighting preserves the official value texture feature state");
        Expect(lighting.UseFaceSdf == true, "lighting preserves the official face SDF feature state");
        Expect(lighting.UseFaceShadowLimiter == true, "lighting reads face shadow limiter state");
        Expect(Math.Abs(lighting.RangeLimit!.Value - 0.25f) < 0.0001f, "lighting reads face shadow range limit");
        Expect(Math.Abs(lighting.HeadNormalBlend!.Value - 0.7f) < 0.0001f, "lighting reads the official hair head-normal blend");

        var keywordLighting = SekaiMaterialMetadata.BuildLightingSettings(KeywordMaterial("hair"));
        Expect(keywordLighting.UseLambert == true, "lighting reads Lambert from the serialized shader keyword");
        Expect(keywordLighting.HairShadow == true, "lighting reads hair shadow from the serialized shader keyword");
        Expect(keywordLighting.UseFaceSdf == true, "lighting reads FaceSDF from the serialized shader keyword");
        Expect(keywordLighting.UseFaceShadowLimiter == true, "lighting reads the face range limiter from the serialized shader keyword");
        Expect(Math.Abs(keywordLighting.UseOutlineSecondNormal - 1f) < 0.0001f, "lighting reads the second-normal outline variant from the serialized shader keyword");

        var unresolvedKeywordLighting = SekaiMaterialMetadata.BuildLightingSettings(
            KeywordMaterial("body") with
            {
                ValidKeywords = Array.Empty<string>(),
                InvalidKeywords = new[] { "_LAMBERT" },
            });
        Expect(
            unresolvedKeywordLighting.UseLambert == true,
            "lighting preserves enabled keywords whose external shader space was unresolved");

        var unknownFeatureLighting = SekaiMaterialMetadata.BuildLightingSettings(SkinMaterial("legacy"));
        Expect(unknownFeatureLighting.HairShadow is null, "lighting does not invent feature state when keyword metadata is unavailable");

        var raw = SekaiMaterialMetadata.BuildRawMaterialProperties(RawMaterial("future_shader"));
        Expect(raw.ShaderName == "Sekai/FutureCharacter", "raw material preserves the shader identity");
        Expect(raw.ShaderKey == "ref:1:99", "raw material preserves the shader object reference");
        Expect(raw.TextureProperties.Single().Name == "_FutureTex", "raw material preserves unknown texture properties");
        Expect(Math.Abs(raw.TextureProperties.Single().ScaleX - 2f) < 0.0001f, "raw material preserves texture scale");
        Expect(Math.Abs(raw.TextureProperties.Single().OffsetY - 0.25f) < 0.0001f, "raw material preserves texture offset");
        Expect(raw.TextureProperties.Single().ColorSpace == 1, "raw material preserves the source texture color space");
        Expect(raw.TextureProperties.Single().SourceWidth == 512, "raw material preserves the source texture width");
        Expect(raw.TextureProperties.Single().SourceHeight == 256, "raw material preserves the source texture height");
        Expect(raw.TextureProperties.Single().SourceMipCount == 7, "raw material preserves the source mip count");
        Expect(raw.TextureProperties.Single().SourceFormat == 48, "raw material preserves the Unity texture format");
        Expect(raw.TextureProperties.Single().FilterMode == 1, "raw material preserves the texture filter mode");
        Expect(raw.TextureProperties.Single().AnisoLevel == 4, "raw material preserves texture anisotropy");
        Expect(Math.Abs(raw.TextureProperties.Single().MipBias - 0.25f) < 0.0001f, "raw material preserves texture mip bias");
        Expect(raw.TextureProperties.Single().WrapU == 0, "raw material preserves texture U wrapping");
        Expect(raw.TextureProperties.Single().WrapV == 1, "raw material preserves texture V wrapping");
        Expect(raw.TextureProperties.Single().WrapW == 2, "raw material preserves texture W wrapping");
        Expect(raw.ColorProperties.Single().Name == "_FutureColor", "raw material preserves unknown color properties");
        Expect(raw.FloatProperties.Single().Name == "_FutureFloat", "raw material preserves unknown float properties");
        Expect(raw.IntProperties.Single().Name == "_FutureInt", "raw material preserves unknown integer properties");
        Expect(raw.ValidKeywords.SequenceEqual(new[] { "_FUTURE_ON" }), "raw material preserves valid shader keywords");
        Expect(raw.InvalidKeywords.SequenceEqual(new[] { "_FUTURE_OFF" }), "raw material preserves invalid shader keywords");
        Expect(raw.CustomRenderQueue == 2450, "raw material preserves the custom render queue");
        Expect(raw.StringTags["RenderType"] == "TransparentCutout", "raw material preserves string tags");
        Expect(raw.DisabledShaderPasses.SequenceEqual(new[] { "ShadowCaster" }), "raw material preserves disabled passes");
        var rawJson = JsonSerializer.Serialize(raw);
        Expect(!rawJson.Contains("TextureData", StringComparison.Ordinal), "raw material never duplicates texture payload bytes");
    }

    private static MaterialInventory SkinMaterial(string name)
    {
        return new MaterialInventory(
            MaterialFileId: 0,
            MaterialPathId: 1,
            MaterialKey: MaterialIdentityLookup.BuildMaterialKey(0, 1),
            Name: name,
            ShaderName: "Sekai/Character",
            TextureSlots: Array.Empty<TextureSlotInventory>(),
            ColorProperties: new[]
            {
                Color("_DefaultSkinColor", 253, 245, 235),
                Color("_SkinColorDefault", 253, 245, 235),
                Color("_Shadow1SkinColor", 227, 196, 203),
                Color("_Shadow2SkinColor", 203, 151, 162),
            },
            FloatProperties: new[]
            {
                new FloatPropertyInventory("_FadeMode", 1f),
                new FloatPropertyInventory("_HueSinAngle", 0.25f),
                new FloatPropertyInventory("_HueCosAngle", 0.75f),
                new FloatPropertyInventory("_Saturation", 0.5f),
                new FloatPropertyInventory("_Value", 0.6f),
                new FloatPropertyInventory("_Contrast", 0.7f),
                new FloatPropertyInventory("_OutlineWidth", 0.001f),
                new FloatPropertyInventory("_DistortionFPS", 12f),
                new FloatPropertyInventory("_LightInfluence", 1f),
                new FloatPropertyInventory("_SekaiShadowThreshold", 0.40625f),
                new FloatPropertyInventory("_UseLambert", 1f),
                new FloatPropertyInventory("_UseValueTex", 1f),
                new FloatPropertyInventory("_UseFaceSDF", 1f),
                new FloatPropertyInventory("_UseFaceShadowLimiter", 1f),
                new FloatPropertyInventory("_RangeLimit", 0.25f),
                new FloatPropertyInventory("_HeadNormalBlend", 0.7f),
            }
        );
    }

    private static MaterialInventory KeywordMaterial(string name)
    {
        return new MaterialInventory(
            MaterialFileId: 0,
            MaterialPathId: 2,
            MaterialKey: MaterialIdentityLookup.BuildMaterialKey(0, 2),
            Name: name,
            ShaderName: "Sekai/Character",
            TextureSlots: Array.Empty<TextureSlotInventory>(),
            ColorProperties: Array.Empty<ColorPropertyInventory>(),
            FloatProperties: Array.Empty<FloatPropertyInventory>(),
            ValidKeywords: new[]
            {
                "_HAIR_SHADOW",
                "_LAMBERT",
                "_USE_FACE_SDF",
                "_FACE_SHADOW_RANGE_LIMIT",
                "_OUTLINE_SECOND_NORMAL",
            },
            InvalidKeywords: Array.Empty<string>()
        );
    }

    private static MaterialInventory RawMaterial(string name)
    {
        return new MaterialInventory(
            MaterialFileId: 1,
            MaterialPathId: 3,
            MaterialKey: MaterialIdentityLookup.BuildMaterialKey(1, 3),
            Name: name,
            ShaderName: "Sekai/FutureCharacter",
            TextureSlots: new[]
            {
                new TextureSlotInventory(
                    "_FutureTex",
                    "future_texture",
                    TextureFileId: 1,
                    TexturePathId: 4,
                    TextureKey: "ref:1:4",
                    TextureData: new byte[] { 1, 2, 3 },
                    ScaleX: 2f,
                    ScaleY: 3f,
                    OffsetX: 0.5f,
                    OffsetY: 0.25f,
                    ColorSpace: 1,
                    SourceWidth: 512,
                    SourceHeight: 256,
                    SourceMipCount: 7,
                    SourceFormat: 48,
                    FilterMode: 1,
                    AnisoLevel: 4,
                    MipBias: 0.25f,
                    WrapU: 0,
                    WrapV: 1,
                    WrapW: 2
                ),
            },
            ColorProperties: new[]
            {
                new ColorPropertyInventory("_FutureColor", 0.1f, 0.2f, 0.3f, 0.4f),
            },
            FloatProperties: new[]
            {
                new FloatPropertyInventory("_FutureFloat", 12.5f),
            },
            ValidKeywords: new[] { "_FUTURE_ON" },
            InvalidKeywords: new[] { "_FUTURE_OFF" },
            IntProperties: new[]
            {
                new IntPropertyInventory("_FutureInt", 7),
            },
            LightmapFlags: 5,
            EnableInstancingVariants: true,
            DoubleSidedGi: true,
            CustomRenderQueue: 2450,
            StringTags: new Dictionary<string, string>
            {
                ["RenderType"] = "TransparentCutout",
            },
            DisabledShaderPasses: new[] { "ShadowCaster" },
            ShaderFileId: 1,
            ShaderPathId: 99,
            ShaderKey: "ref:1:99"
        );
    }

    private static ColorPropertyInventory Color(string name, int r, int g, int b)
    {
        return new ColorPropertyInventory(name, r / 255f, g / 255f, b / 255f, 1f);
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}
