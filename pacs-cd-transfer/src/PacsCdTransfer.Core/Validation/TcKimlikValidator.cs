using System.Text.RegularExpressions;

namespace PacsCdTransfer.Core.Validation;

/// <summary>
/// Validates a Turkish national ID (TC Kimlik No): 11 digits, first digit non-zero,
/// and the two official checksum digits. The mockup only enforced "exactly 11 digits";
/// this adds the real checksum so bad transcriptions from a CD label don't get sent to PACS.
/// </summary>
public static partial class TcKimlikValidator
{
    [GeneratedRegex(@"^\d{11}$")]
    private static partial Regex ElevenDigits();

    public static bool HasValidFormat(string? tc) => tc is not null && ElevenDigits().IsMatch(tc);

    public static bool HasValidChecksum(string? tc)
    {
        if (!HasValidFormat(tc) || tc![0] == '0')
            return false;

        var d = new int[11];
        for (var i = 0; i < 11; i++)
            d[i] = tc[i] - '0';

        var oddSum = d[0] + d[2] + d[4] + d[6] + d[8];
        var evenSum = d[1] + d[3] + d[5] + d[7];
        var digit10 = ((oddSum * 7) - evenSum) % 10;
        if (digit10 < 0) digit10 += 10;
        if (digit10 != d[9]) return false;

        var sumFirstTen = 0;
        for (var i = 0; i < 10; i++) sumFirstTen += d[i];
        var digit11 = sumFirstTen % 10;

        return digit11 == d[10];
    }

    public static bool IsValid(string? tc) => HasValidChecksum(tc);
}
