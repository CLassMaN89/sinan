using FellowOakDicom;
using FellowOakDicom.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace PacsCdTransfer.Core.Services;

/// <summary>
/// Renders a real thumbnail from an actual DICOM file's pixel data — the mockup's series
/// thumbnails and preview box were decorative CSS placeholders that never reflected the
/// scanned CD's real image content.
/// </summary>
public static class DicomThumbnailService
{
    private static bool _initialized;
    private static readonly object InitLock = new();

    private static void EnsureImagingInitialized()
    {
        if (_initialized) return;
        lock (InitLock)
        {
            if (_initialized) return;
            new DicomSetupBuilder()
                .RegisterServices(s => s.AddImageManager<ImageSharpImageManager>())
                .Build();
            _initialized = true;
        }
    }

    /// <summary>Renders the first frame of a DICOM file as a PNG data URI, or null if it can't be read/rendered.</summary>
    public static string? RenderThumbnailDataUri(string dicomFilePath, int maxDimension = 96)
    {
        EnsureImagingInitialized();
        try
        {
            var file = DicomFile.Open(dicomFilePath, FileReadOption.ReadLargeOnDemand);
            var dicomImage = new DicomImage(file.Dataset);
            using var rendered = dicomImage.RenderImage();
            using var sharpImage = rendered.AsSharpImage();
            sharpImage.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(maxDimension, maxDimension)
            }));
            using var ms = new MemoryStream();
            sharpImage.Save(ms, new PngEncoder());
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
