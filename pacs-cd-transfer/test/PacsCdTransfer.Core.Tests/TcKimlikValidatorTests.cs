using PacsCdTransfer.Core.Validation;
using Xunit;

namespace PacsCdTransfer.Core.Tests;

public class TcKimlikValidatorTests
{
    [Theory]
    [InlineData("10000000146", true)]  // valid checksum
    [InlineData("11111111110", true)]  // valid checksum
    [InlineData("12345678901", false)] // fails checksum
    [InlineData("1234567890", false)]  // 10 digits
    [InlineData("123456789012", false)] // 12 digits
    [InlineData("0234567891", false)]   // starts with 0, 10 digits
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("1234567890a", false)]  // non-digit
    public void Validates_TcKimlikNo(string? tc, bool expected)
    {
        Assert.Equal(expected, TcKimlikValidator.IsValid(tc));
    }

    [Fact]
    public void FormatCheck_AllowsAnyElevenDigits_EvenBadChecksum()
    {
        Assert.True(TcKimlikValidator.HasValidFormat("12345678901"));
        Assert.False(TcKimlikValidator.HasValidChecksum("12345678901"));
    }

    [Fact]
    public void RejectsLeadingZero()
    {
        Assert.True(TcKimlikValidator.HasValidFormat("01111111110"));
        Assert.False(TcKimlikValidator.IsValid("01111111110"));
    }
}
