using System.Security.Cryptography;
using FluentAssertions;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Security;

[Trait("Category", "Security")]
public sealed class KeyWrappingTests
{
    private static byte[] GenerateKey() => RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void WrapUnwrap_RoundTrip_RecoversOriginalDek()
    {
        var kek = GenerateKey();
        var dek = GenerateKey();

        var wrapped = KeyWrappingService.WrapDek(dek, kek);
        var unwrapped = KeyWrappingService.UnwrapDek(wrapped, kek);

        unwrapped.Should().BeEquivalentTo(dek);
    }

    [Fact]
    public void WrapDek_StartsWithVersionByte0x02()
    {
        var kek = GenerateKey();
        var dek = GenerateKey();

        var wrapped = KeyWrappingService.WrapDek(dek, kek);

        wrapped[0].Should().Be(0x02);
    }

    [Fact]
    public void IsWrapped_ReturnsTrueForWrappedData()
    {
        var kek = GenerateKey();
        var dek = GenerateKey();

        var wrapped = KeyWrappingService.WrapDek(dek, kek);

        KeyWrappingService.IsWrapped(wrapped).Should().BeTrue();
    }

    [Fact]
    public void IsWrapped_ReturnsFalseForPlaintextBase64()
    {
        var dek = GenerateKey();
        var base64 = Convert.ToBase64String(dek);

        KeyWrappingService.IsWrappedBase64(base64).Should().BeFalse();
    }

    [Fact]
    public void UnwrapDek_WithWrongKek_Throws()
    {
        var kek1 = GenerateKey();
        var kek2 = GenerateKey();
        var dek = GenerateKey();

        var wrapped = KeyWrappingService.WrapDek(dek, kek1);

        var act = () => KeyWrappingService.UnwrapDek(wrapped, kek2);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void UnwrapDek_WithTamperedCiphertext_Throws()
    {
        var kek = GenerateKey();
        var dek = GenerateKey();

        var wrapped = KeyWrappingService.WrapDek(dek, kek);

        // Tamper with the ciphertext (last byte)
        wrapped[^1] ^= 0xFF;

        var act = () => KeyWrappingService.UnwrapDek(wrapped, kek);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void UnwrapDek_WithWrongVersionByte_Throws()
    {
        var kek = GenerateKey();
        var dek = GenerateKey();

        var wrapped = KeyWrappingService.WrapDek(dek, kek);
        wrapped[0] = 0x01; // Wrong version

        var act = () => KeyWrappingService.UnwrapDek(wrapped, kek);
        act.Should().Throw<CryptographicException>()
            .WithMessage("*version byte*");
    }

    [Fact]
    public void UnwrapDek_WithTooShortData_Throws()
    {
        var kek = GenerateKey();

        var act = () => KeyWrappingService.UnwrapDek(new byte[] { 0x02 }, kek);
        act.Should().Throw<CryptographicException>()
            .WithMessage("*too short*");
    }

    [Fact]
    public void WrapDek_ProducesDifferentCiphertextEachTime()
    {
        var kek = GenerateKey();
        var dek = GenerateKey();

        var wrapped1 = KeyWrappingService.WrapDek(dek, kek);
        var wrapped2 = KeyWrappingService.WrapDek(dek, kek);

        // Different nonces → different output
        wrapped1.Should().NotBeEquivalentTo(wrapped2);
    }

    [Fact]
    public void IsWrappedBase64_WithEmptyString_ReturnsFalse()
    {
        KeyWrappingService.IsWrappedBase64("").Should().BeFalse();
        KeyWrappingService.IsWrappedBase64(null!).Should().BeFalse();
    }

    [Fact]
    public void IsWrappedBase64_WithInvalidBase64_ReturnsFalse()
    {
        KeyWrappingService.IsWrappedBase64("not-valid-base64!!!").Should().BeFalse();
    }
}
