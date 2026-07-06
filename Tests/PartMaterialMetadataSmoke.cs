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
            }
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
