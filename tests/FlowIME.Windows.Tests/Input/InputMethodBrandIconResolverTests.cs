using FlowIME.Windows.Input;
using SkiaSharp;

namespace FlowIME.Windows.Tests.Input;

public sealed class InputMethodBrandIconResolverTests
{
    [Fact]
    public void Build_profile_registry_path_uses_the_exact_tsf_identity()
    {
        var profile = MicrosoftPinyinProfile();

        var path = InputMethodBrandIconResolver.BuildProfileRegistryPath(profile);

        Assert.Equal(
            @"SOFTWARE\Microsoft\CTF\TIP\{81d4e9c9-1d3b-41bc-9e6c-4b40bf79e35e}\LanguageProfile\0x00000804\{fa550b04-5ad7-411f-a5ac-ca038ec515d7}",
            path,
            ignoreCase: true);
    }

    [Fact]
    public void Resolve_and_load_uses_the_icon_registered_by_the_active_input_method()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var registration = InputMethodBrandIconResolver.ResolveRegistration(MicrosoftPinyinProfile());
        if (registration is null)
        {
            // Microsoft Pinyin is optional on Windows Server, language-neutral,
            // and reduced-footprint images. Deterministic path logic is covered above.
            return;
        }

        Assert.True(File.Exists(registration.Value.FilePath));

        using var icon = InputMethodBrandIconResolver.TryLoad(MicrosoftPinyinProfile(), 32);
        Assert.NotNull(icon);
        Assert.True(ContainsVisiblePixel(icon));
    }

    [Fact]
    public void Theme_mask_removes_an_opaque_icon_tile_but_preserves_the_brand_glyph()
    {
        using var source = new SKBitmap(8, 8);
        source.Erase(SKColors.White);
        for (var y = 2; y < 6; y++)
        {
            source.SetPixel(3, y, SKColors.Black);
        }

        using var mask = InputMethodBrandIconResolver.CreateThemeMask(source);

        Assert.Equal(0, mask.GetPixel(0, 0).Alpha);
        Assert.True(mask.GetPixel(3, 3).Alpha > 240);
    }

    private static bool ContainsVisiblePixel(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static TsfProfileSnapshot MicrosoftPinyinProfile() => new(
        Success: true,
        HResult: 0,
        ProfileType: 1,
        LanguageId: 0x0804,
        Clsid: new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E"),
        ProfileGuid: new Guid("FA550B04-5AD7-411F-A5AC-CA038EC515D7"),
        CategoryId: Guid.Empty,
        SubstituteKeyboardLayout: 0,
        Capabilities: 0,
        KeyboardLayout: 0,
        Flags: 0,
        Error: null);
}
