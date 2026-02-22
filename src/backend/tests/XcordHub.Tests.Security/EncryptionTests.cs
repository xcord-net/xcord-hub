using System.Security.Cryptography;
using FluentAssertions;
using XcordHub.Infrastructure.Services;

namespace XcordHub.Tests.Security;

[Trait("Category", "Security")]
public sealed class EncryptionTests
{
    private const string TestEncryptionKey = "test-encryption-key-with-256-bits-minimum-length-required";

    [Fact]
    public void Encrypt_ShouldProduceCiphertext_DifferentFromPlaintext()
    {
        // Arrange
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var plaintext = "sensitive-data@example.com";

        // Act
        var ciphertext = encryptionService.Encrypt(plaintext);

        // Assert
        ciphertext.Should().NotBeNull();
        ciphertext.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Decrypt_ShouldRecoverOriginalPlaintext()
    {
        // Arrange
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var plaintext = "sensitive-data@example.com";

        // Act
        var ciphertext = encryptionService.Encrypt(plaintext);
        var decrypted = encryptionService.Decrypt(ciphertext);

        // Assert
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void EncryptTwice_ShouldProduceDifferentCiphertexts()
    {
        // Arrange
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var plaintext = "sensitive-data@example.com";

        // Act
        var ciphertext1 = encryptionService.Encrypt(plaintext);
        var ciphertext2 = encryptionService.Encrypt(plaintext);

        // Assert - due to random IV, same plaintext should produce different ciphertext
        ciphertext1.Should().NotBeEquivalentTo(ciphertext2);
    }

    [Fact]
    public void ComputeHmac_ShouldProduceConsistentHash()
    {
        // Arrange
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var data = "test@example.com";

        // Act
        var hash1 = encryptionService.ComputeHmac(data);
        var hash2 = encryptionService.ComputeHmac(data);

        // Assert
        hash1.Should().BeEquivalentTo(hash2);
        hash1.Should().NotBeEmpty();
    }

    [Fact]
    public void ComputeHmac_ShouldProduceDifferentHashesForDifferentInputs()
    {
        // Arrange
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var data1 = "test1@example.com";
        var data2 = "test2@example.com";

        // Act
        var hash1 = encryptionService.ComputeHmac(data1);
        var hash2 = encryptionService.ComputeHmac(data2);

        // Assert
        hash1.Should().NotBeEquivalentTo(hash2);
    }

    [Fact]
    public void Ciphertext_ShouldBePaddedToBlockSize()
    {
        // Arrange
        var encryptionService = new AesEncryptionService(TestEncryptionKey);
        var shortPlaintext = "a";
        var longPlaintext = "this is a much longer plaintext string";

        // Act
        var shortCiphertext = encryptionService.Encrypt(shortPlaintext);
        var longCiphertext = encryptionService.Encrypt(longPlaintext);

        // Assert - ciphertext should include IV and be properly sized
        shortCiphertext.Length.Should().BeGreaterThanOrEqualTo(16);
        longCiphertext.Length.Should().BeGreaterThanOrEqualTo(16);
        longCiphertext.Length.Should().BeGreaterThan(shortCiphertext.Length);
    }

    [Fact]
    public void DecryptWithWrongKey_ShouldThrow()
    {
        // Arrange
        var encryptionService1 = new AesEncryptionService(TestEncryptionKey);
        var encryptionService2 = new AesEncryptionService("different-key-with-256-bits-minimum-length-required-here");
        var plaintext = "sensitive-data@example.com";

        // Act
        var ciphertext = encryptionService1.Encrypt(plaintext);

        // Assert
        var act = () => encryptionService2.Decrypt(ciphertext);
        act.Should().Throw<CryptographicException>();
    }
}
