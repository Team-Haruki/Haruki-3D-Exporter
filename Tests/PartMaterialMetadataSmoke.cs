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
        Expect(lighting.UseSkinColor == true, "lighting preserves the official skin color feature state");
        Expect(lighting.SkinMaskMode == 1, "lighting reads the official skin mask mode");
        Expect(Math.Abs(lighting.FaceSdfMirror!.Value - 1f) < 0.0001f, "lighting reads face SDF mirror");
        Expect(Math.Abs(lighting.FaceSdfBias!.Value - 0.125f) < 0.0001f, "lighting reads face SDF bias");
        Expect(lighting.UseFaceShadowLimiter == true, "lighting reads face shadow limiter state");
        Expect(Math.Abs(lighting.RangeLimit!.Value - 0.25f) < 0.0001f, "lighting reads face shadow range limit");
        Expect(Math.Abs(lighting.FaceSkinShadowStrength!.Value - 0.1f) < 0.0001f, "lighting reads face skin shadow strength");
        Expect(Math.Abs(lighting.FaceSphereShadowEdge!.Value - 0.2f) < 0.0001f, "lighting reads face sphere shadow edge");
        Expect(Math.Abs(lighting.FaceSphereShadowSmoothness!.Value - 0.3f) < 0.0001f, "lighting reads face sphere shadow smoothness");
        Expect(Math.Abs(lighting.FaceSphereShadowWeight!.Value - 0.4f) < 0.0001f, "lighting reads face sphere shadow weight");

        var keywordLighting = SekaiMaterialMetadata.BuildLightingSettings(KeywordMaterial("hair"));
        Expect(keywordLighting.UseLambert == true, "lighting reads Lambert from the serialized shader keyword");
        Expect(keywordLighting.HairShadow == true, "lighting reads hair shadow from the serialized shader keyword");

        var unknownFeatureLighting = SekaiMaterialMetadata.BuildLightingSettings(SkinMaterial("legacy"));
        Expect(unknownFeatureLighting.HairShadow is null, "lighting does not invent feature state when keyword metadata is unavailable");
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
                new FloatPropertyInventory("_UseSkinColor", 1f),
                new FloatPropertyInventory("_SkinMaskMode", 1f),
                new FloatPropertyInventory("_FaceSdfMirror", 1f),
                new FloatPropertyInventory("_FaceSdfBias", 0.125f),
                new FloatPropertyInventory("_UseFaceShadowLimiter", 1f),
                new FloatPropertyInventory("_RangeLimit", 0.25f),
                new FloatPropertyInventory("_FaceSkinShadowStrength", 0.1f),
                new FloatPropertyInventory("_FaceSphereShadowEdge", 0.2f),
                new FloatPropertyInventory("_FaceSphereShadowSmoothness", 0.3f),
                new FloatPropertyInventory("_FaceSphereShadowWeight", 0.4f),
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
            ValidKeywords: new[] { "_HAIR_SHADOW", "_LAMBERT" },
            InvalidKeywords: Array.Empty<string>()
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
