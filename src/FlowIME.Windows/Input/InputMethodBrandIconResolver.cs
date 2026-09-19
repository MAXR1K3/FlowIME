using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SkiaSharp;

namespace FlowIME.Windows.Input;

internal readonly record struct InputMethodIconRegistration(string FilePath, int IconIndex);

internal static class InputMethodBrandIconResolver
{
    private const string TipRoot = @"SOFTWARE\Microsoft\CTF\TIP";

    internal static InputMethodIconRegistration? ResolveRegistration(TsfProfileSnapshot profile)
    {
        if (!profile.Success || profile.Clsid == Guid.Empty || profile.ProfileGuid == Guid.Empty)
        {
            return null;
        }

        var relativePath = BuildProfileRegistryPath(profile);
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(relativePath);
            var rawPath = key?.GetValue("IconFile") as string;
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            var expandedPath = Environment.ExpandEnvironmentVariables(rawPath.Trim().Trim('"'));
            var rawIndex = key?.GetValue("IconIndex");
            var index = rawIndex switch
            {
                int signed => signed,
                uint unsigned => unchecked((int)unsigned),
                _ => 0
            };
            return new InputMethodIconRegistration(expandedPath, index);
        }

        return null;
    }

    internal static string BuildProfileRegistryPath(TsfProfileSnapshot profile) =>
        $@"{TipRoot}\{{{profile.Clsid:D}}}\LanguageProfile\0x{profile.LanguageId:x8}\{{{profile.ProfileGuid:D}}}";

    internal static SKBitmap? TryLoad(TsfProfileSnapshot profile, int pixelSize)
    {
        var registration = ResolveRegistration(profile);
        return registration is null
            ? null
            : NativeIconLoader.TryLoad(registration.Value, Math.Clamp(pixelSize, 16, 64));
    }

    internal static SKBitmap CreateThemeMask(SKBitmap source)
    {
        var opaquePixels = new List<SKColor>(source.Width * source.Height);
        var buckets = new Dictionary<int, int>();
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                if (color.Alpha < 240)
                {
                    continue;
                }

                opaquePixels.Add(color);
                var bucket = ((color.Red >> 5) << 6) | ((color.Green >> 5) << 3) | (color.Blue >> 5);
                buckets[bucket] = buckets.GetValueOrDefault(bucket) + 1;
            }
        }

        var dominantBucket = buckets.Count == 0
            ? -1
            : buckets.MaxBy(pair => pair.Value).Key;
        var backgroundPixels = opaquePixels.Where(color =>
            (((color.Red >> 5) << 6) | ((color.Green >> 5) << 3) | (color.Blue >> 5)) == dominantBucket).ToArray();
        var opaqueBackground = backgroundPixels.Length >= source.Width * source.Height * 0.35;
        var background = opaqueBackground
            ? new SKColor(
                checked((byte)backgroundPixels.Average(color => color.Red)),
                checked((byte)backgroundPixels.Average(color => color.Green)),
                checked((byte)backgroundPixels.Average(color => color.Blue)))
            : SKColors.Transparent;
        var result = new SKBitmap(source.Info);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var pixel = source.GetPixel(x, y);
                var alpha = pixel.Alpha;
                if (opaqueBackground)
                {
                    var distance = Math.Max(
                        Math.Abs(pixel.Red - background.Red),
                        Math.Max(
                            Math.Abs(pixel.Green - background.Green),
                            Math.Abs(pixel.Blue - background.Blue)));
                    alpha = checked((byte)Math.Clamp(distance * 3, 0, 255));
                }

                result.SetPixel(x, y, new SKColor(255, 255, 255, alpha));
            }
        }

        return result;
    }

    private static class NativeIconLoader
    {
        private const uint DibRgbColors = 0;
        private const uint DiNormal = 0x0003;

        internal static SKBitmap? TryLoad(InputMethodIconRegistration registration, int size)
        {
            if (!File.Exists(registration.FilePath))
            {
                return null;
            }

            nint icon = 0;
            nint screenDc = 0;
            nint memoryDc = 0;
            nint dib = 0;
            nint previous = 0;
            try
            {
                var extracted = ExtractIconExW(
                    registration.FilePath,
                    registration.IconIndex,
                    out var large,
                    out var small,
                    1);
                icon = small != 0 ? small : large;
                var unused = icon == small ? large : small;
                if (unused != 0)
                {
                    _ = DestroyIcon(unused);
                }
                if (extracted == 0 || icon == 0)
                {
                    return null;
                }

                screenDc = GetDC(0);
                memoryDc = CreateCompatibleDC(screenDc);
                var info = BitmapInfo.Create(size, size);
                dib = CreateDIBSection(screenDc, ref info, DibRgbColors, out var bits, 0, 0);
                if (memoryDc == 0 || dib == 0 || bits == 0)
                {
                    return null;
                }

                previous = SelectObject(memoryDc, dib);
                if (!DrawIconEx(memoryDc, 0, 0, icon, size, size, 0, 0, DiNormal))
                {
                    return null;
                }

                var bytes = new byte[checked(size * size * 4)];
                Marshal.Copy(bits, bytes, 0, bytes.Length);
                var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
                var mask = CreateThemeMask(bitmap);
                bitmap.Dispose();
                return mask;
            }
            catch (Exception exception) when (
                exception is Win32Exception or ExternalException or ArgumentException)
            {
                return null;
            }
            finally
            {
                if (previous != 0 && memoryDc != 0) _ = SelectObject(memoryDc, previous);
                if (dib != 0) _ = DeleteObject(dib);
                if (memoryDc != 0) _ = DeleteDC(memoryDc);
                if (screenDc != 0) _ = ReleaseDC(0, screenDc);
                if (icon != 0) _ = DestroyIcon(icon);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPelsPerMeter;
            internal int YPelsPerMeter;
            internal uint ColorsUsed;
            internal uint ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;

            internal static BitmapInfo Create(int width, int height) => new()
            {
                Header = new BitmapInfoHeader
                {
                    Size = checked((uint)Marshal.SizeOf<BitmapInfoHeader>()),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    SizeImage = checked((uint)(width * height * 4))
                }
            };
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconExW(string file, int index, out nint large, out nint small, uint count);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(nint icon);

        [DllImport("user32.dll")]
        private static extern nint GetDC(nint window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(nint window, nint dc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);

        [DllImport("gdi32.dll")]
        private static extern nint CreateCompatibleDC(nint dc);

        [DllImport("gdi32.dll")]
        private static extern nint SelectObject(nint dc, nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(nint dc);

        [DllImport("gdi32.dll")]
        private static extern nint CreateDIBSection(
            nint dc,
            ref BitmapInfo info,
            uint usage,
            out nint bits,
            nint section,
            uint offset);
    }
}
