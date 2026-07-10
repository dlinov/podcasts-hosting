namespace PodcastsHosting.Services;

using System.Buffers.Binary;
using System.Text;

public static class AudioFileValidator
{
    private const int HeaderBytesToRead = 512;
    private static readonly HashSet<string> SupportedIsoBaseMediaBrands = new(StringComparer.Ordinal)
    {
        "M4A ",
        "M4B ",
        "iso2",
        "iso5",
        "iso6",
        "isom",
        "mp41",
        "mp42",
        "qt  "
    };

    public static async Task<string?> GetValidationErrorAsync(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName);
        var format = AudioFormats.FindByExtension(extension);
        if (format == null)
        {
            return $"Only {AudioFormats.SupportedDisplayNames} audio files are supported.";
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) && !AudioFormats.IsSupportedUploadContentType(file.ContentType))
        {
            return "The uploaded file content type is not supported.";
        }

        var header = new byte[HeaderBytesToRead];
        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(header);
        var headerBytes = header.AsSpan(0, bytesRead);

        return HasAudioSignature(format.SignatureKind, headerBytes)
            ? null
            : "The uploaded file does not look like a supported audio file.";
    }

    private static bool HasAudioSignature(AudioSignatureKind signatureKind, ReadOnlySpan<byte> header)
    {
        return signatureKind switch
        {
            AudioSignatureKind.Mp3 => HasMp3Signature(header),
            AudioSignatureKind.Aac => HasAacSignature(header),
            AudioSignatureKind.IsoBaseMedia => HasIsoBaseMediaAudioSignature(header),
            _ => false
        };
    }

    private static bool HasMp3Signature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 'I' && header[1] == 'D' && header[2] == '3')
        {
            return true;
        }

        return header.Length >= 2 && header[0] == 0xff && (header[1] & 0xe0) == 0xe0;
    }

    private static bool HasAacSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == 'A' && header[1] == 'D' && header[2] == 'I' && header[3] == 'F')
        {
            return true;
        }

        return header.Length >= 2 && header[0] == 0xff && (header[1] & 0xf6) == 0xf0;
    }

    private static bool HasIsoBaseMediaAudioSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12 || header[4] != 'f' || header[5] != 't' || header[6] != 'y' || header[7] != 'p')
        {
            return false;
        }

        var declaredBoxSize = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        if (declaredBoxSize < 12)
        {
            return false;
        }

        var availableBoxSize = (int)Math.Min(declaredBoxSize, (uint)header.Length);
        if (IsSupportedIsoBaseMediaBrand(header.Slice(8, 4)))
        {
            return true;
        }

        for (var offset = 16; offset + 4 <= availableBoxSize; offset += 4)
        {
            if (IsSupportedIsoBaseMediaBrand(header.Slice(offset, 4)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedIsoBaseMediaBrand(ReadOnlySpan<byte> brand)
    {
        return SupportedIsoBaseMediaBrands.Contains(Encoding.ASCII.GetString(brand));
    }
}