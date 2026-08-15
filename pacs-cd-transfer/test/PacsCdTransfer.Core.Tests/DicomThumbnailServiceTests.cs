using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.IO.Buffer;
using PacsCdTransfer.Core.Services;
using Xunit;

namespace PacsCdTransfer.Core.Tests;

public class DicomThumbnailServiceTests
{
    private static string WriteGrayscaleTestFile(string dir, int rows, int cols)
    {
        var pixels = new byte[rows * cols];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i * 4 % 256);

        var dataset = new DicomDataset
        {
            { DicomTag.SOPClassUID, DicomUID.SecondaryCaptureImageStorage },
            { DicomTag.SOPInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.StudyInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.SeriesInstanceUID, DicomUIDGenerator.GenerateDerivedFromUUID() },
            { DicomTag.PhotometricInterpretation, "MONOCHROME2" },
            { DicomTag.Rows, (ushort)rows },
            { DicomTag.Columns, (ushort)cols },
            { DicomTag.BitsAllocated, (ushort)8 },
            { DicomTag.BitsStored, (ushort)8 },
            { DicomTag.HighBit, (ushort)7 },
            { DicomTag.PixelRepresentation, (ushort)0 },
            { DicomTag.SamplesPerPixel, (ushort)1 },
            { DicomTag.PlanarConfiguration, (ushort)0 },
            { DicomTag.NumberOfFrames, "1" }
        };
        var pixelData = DicomPixelData.Create(dataset, true);
        pixelData.AddFrame(new MemoryByteBuffer(pixels));

        var path = Path.Combine(dir, "test.dcm");
        new DicomFile(dataset).Save(path);
        return path;
    }

    [Fact]
    public void RenderThumbnailDataUri_RendersRealPixelDataAsPng()
    {
        var dir = Path.Combine(Path.GetTempPath(), "thumb-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var path = WriteGrayscaleTestFile(dir, 16, 16);

            var dataUri = DicomThumbnailService.RenderThumbnailDataUri(path, maxDimension: 64);

            Assert.NotNull(dataUri);
            Assert.StartsWith("data:image/png;base64,", dataUri);

            var base64 = dataUri!.Substring("data:image/png;base64,".Length);
            var bytes = Convert.FromBase64String(base64);

            // PNG magic number: 89 50 4E 47 0D 0A 1A 0A
            Assert.True(bytes.Length > 8);
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
            Assert.Equal((byte)'N', bytes[2]);
            Assert.Equal((byte)'G', bytes[3]);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void RenderThumbnailDataUri_ReturnsNullForMissingFile()
    {
        var result = DicomThumbnailService.RenderThumbnailDataUri("/no/such/file.dcm");
        Assert.Null(result);
    }
}
