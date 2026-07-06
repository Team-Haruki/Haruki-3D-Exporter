using PjskBundle2Parts.Models;

namespace PjskBundle2Parts.Services;

public static class SekaiMaterialMetadata
{
    public static BodyProxySettings BuildBodyProxy(IEnumerable<MaterialInventory> materials)
    {
        var tintSource = materials.FirstOrDefault(HasSkinColorProperty);
        var bodyColor = FindColorProperty(tintSource, "_DefaultSkinColor")
            ?? FindColorProperty(tintSource, "_SkinColorDefault")
            ?? "#f2d0c3";
        var shadowColor = FindColorProperty(tintSource, "_Shadow1SkinColor")
            ?? bodyColor;
        return new BodyProxySettings(
            BodyColor: bodyColor,
            ShadowColor: shadowColor,
            BodyScale: 1.0f,
            TorsoLength: 2.2f,
            ShoulderWidth: 1.1f
        );
    }

    public static HeadProxySettings BuildHeadProxy(IEnumerable<MaterialInventory> materials)
    {
        var tintSource = materials.FirstOrDefault(HasSkinColorProperty);
        var skinColorDefault = FindColorProperty(tintSource, "_SkinColorDefault")
            ?? FindColorProperty(tintSource, "_DefaultSkinColor")
            ?? FindColorProperty(tintSource, "_Shadow1SkinColor")
            ?? "#fde2d9";
        var skinColor1 = FindColorProperty(tintSource, "_Shadow1SkinColor")
            ?? skinColorDefault;
        var skinColor2 = FindColorProperty(tintSource, "_Shadow2SkinColor")
            ?? skinColor1;
        return new HeadProxySettings(
            FaceColor: skinColorDefault,
            FaceShadeColor: skinColor1,
            SkinColorDefault: skinColorDefault,
            SkinColor1: skinColor1,
            SkinColor2: skinColor2,
            HairColor: "#7b5b4a",
            HairShadowColor: "#513d33",
            HeadRadius: 0.74f,
            FaceDepth: 0.82f,
            HairArc: 0.98f
        );
    }

    public static MaterialLightingSettings BuildLightingSettings(MaterialInventory? material)
    {
        return new MaterialLightingSettings(
            SpecularPower: FindFloatProperty(material, "_SpecularPower") ?? 0f,
            RimThreshold:
                FindFloatProperty(material, "_SpecularStrength") ??
                FindFloatProperty(material, "_RimThreshold") ??
                0.2f,
            ShadowTexWeight: FindFloatProperty(material, "_ShadowTexWeight") ?? 1f,
            FadeMode: (int)(FindFloatProperty(material, "_FadeMode") ?? 0f),
            HueSinAngle: FindFloatProperty(material, "_HueSinAngle") ?? 0f,
            HueCosAngle: FindFloatProperty(material, "_HueCosAngle") ?? 1f,
            Saturation: FindFloatProperty(material, "_Saturation") ?? 0.5f,
            Value: FindFloatProperty(material, "_Value") ?? 0.5f,
            Contrast: FindFloatProperty(material, "_Contrast") ?? 0.5f,
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

    public static string? FindTextureSlot(MaterialInventory? material, string slotName)
    {
        return material?.TextureSlots
            .FirstOrDefault(slot => string.Equals(slot.SlotName, slotName, StringComparison.OrdinalIgnoreCase))
            ?.TextureName;
    }

    public static string? FindColorProperty(MaterialInventory? material, string propertyName)
    {
        var color = material?.ColorProperties
            .FirstOrDefault(entry => string.Equals(entry.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return color is null ? null : ToHex(color.R, color.G, color.B);
    }

    public static float? FindFloatProperty(MaterialInventory? material, string propertyName)
    {
        return material?.FloatProperties
            .FirstOrDefault(entry => string.Equals(entry.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    public static bool HasSkinColorProperty(MaterialInventory material)
    {
        return material.ColorProperties.Any(entry =>
            string.Equals(entry.Name, "_DefaultSkinColor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "_SkinColorDefault", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "_Shadow1SkinColor", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.Name, "_Shadow2SkinColor", StringComparison.OrdinalIgnoreCase));
    }

    private static string ToHex(float r, float g, float b)
    {
        static int ClampByte(float value) => Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
        return $"#{ClampByte(r):X2}{ClampByte(g):X2}{ClampByte(b):X2}".ToLowerInvariant();
    }
}
