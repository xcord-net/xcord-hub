using FluentAssertions;

namespace XcordHub.Tests.Unit;

public sealed class ValidationHelpersTests
{
    // -----------------------------------------------------------------------
    // ValidateSubdomain - valid inputs
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("abcdef")]
    [InlineData("my-server")]
    [InlineData("server1a")]
    [InlineData("a1b2c3")]
    [InlineData("test-server-01")]
    public void ValidateSubdomain_ValidInputs_ReturnsNull(string subdomain)
    {
        ValidationHelpers.ValidateSubdomain(subdomain).Should().BeNull();
    }

    [Fact]
    public void ValidateSubdomain_ExactlySixChars_ReturnsNull()
    {
        ValidationHelpers.ValidateSubdomain("abcdef").Should().BeNull();
    }

    [Fact]
    public void ValidateSubdomain_Exactly63Chars_ReturnsNull()
    {
        var label = new string('a', 63);
        ValidationHelpers.ValidateSubdomain(label).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // ValidateSubdomain - null/empty/whitespace
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSubdomain_NullOrEmpty_ReturnsError(string? subdomain)
    {
        var error = ValidationHelpers.ValidateSubdomain(subdomain);
        error.Should().NotBeNull();
        error!.Code.Should().Be("VALIDATION_FAILED");
    }

    // -----------------------------------------------------------------------
    // ValidateSubdomain - length violations
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcde")]
    public void ValidateSubdomain_TooShort_ReturnsError(string subdomain)
    {
        var error = ValidationHelpers.ValidateSubdomain(subdomain);
        error.Should().NotBeNull();
        error!.Message.Should().Contain("6-63");
    }

    [Fact]
    public void ValidateSubdomain_TooLong_ReturnsError()
    {
        var label = new string('a', 64);
        var error = ValidationHelpers.ValidateSubdomain(label);
        error.Should().NotBeNull();
        error!.Message.Should().Contain("6-63");
    }

    // -----------------------------------------------------------------------
    // ValidateSubdomain - invalid characters
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("my.server.x")]       // dots not allowed in subdomain label
    [InlineData("My-Server")]         // uppercase
    [InlineData("my_server")]         // underscores
    [InlineData("my server")]         // spaces
    [InlineData("my@server")]         // special chars
    [InlineData("caféxx")]            // non-ASCII
    public void ValidateSubdomain_InvalidChars_ReturnsError(string subdomain)
    {
        var error = ValidationHelpers.ValidateSubdomain(subdomain);
        error.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // ValidateSubdomain - leading/trailing hyphens
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("-server")]
    [InlineData("server-")]
    [InlineData("-server-")]
    public void ValidateSubdomain_LeadingTrailingHyphen_ReturnsError(string subdomain)
    {
        var error = ValidationHelpers.ValidateSubdomain(subdomain);
        error.Should().NotBeNull();
    }

    // -----------------------------------------------------------------------
    // ValidateSubdomain - consecutive hyphens
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateSubdomain_ConsecutiveHyphens_ReturnsError()
    {
        var error = ValidationHelpers.ValidateSubdomain("my--server");
        error.Should().NotBeNull();
        error!.Message.Should().Contain("consecutive hyphens");
    }

    // -----------------------------------------------------------------------
    // ValidateSubdomain - reserved names (those >= 6 chars hit reserved check,
    // shorter ones hit length check first)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("docker")]
    [InlineData("registry")]
    [InlineData("postgres")]
    [InlineData("livekit")]
    [InlineData("grafana")]
    [InlineData("status")]
    [InlineData("monitor")]
    [InlineData("prometheus")]
    [InlineData("autoconfig")]
    [InlineData("autodiscover")]
    public void ValidateSubdomain_ReservedName_ReturnsReservedError(string subdomain)
    {
        var error = ValidationHelpers.ValidateSubdomain(subdomain);
        error.Should().NotBeNull();
        error!.Code.Should().Be("RESERVED_SUBDOMAIN");
    }

    [Theory]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("redis")]
    [InlineData("minio")]
    [InlineData("proxy")]
    public void ValidateSubdomain_ShortReservedName_ReturnsLengthError(string subdomain)
    {
        // Short reserved names are caught by the length check before the reserved check
        var error = ValidationHelpers.ValidateSubdomain(subdomain);
        error.Should().NotBeNull();
        error!.Code.Should().Be("VALIDATION_FAILED");
    }

    // -----------------------------------------------------------------------
    // ValidateDomain - valid inputs
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("myserver.xcord.net")]
    [InlineData("test.example.com")]
    [InlineData("a1.b2.c3")]
    [InlineData("my-server.xcord-dev.net")]
    public void ValidateDomain_ValidInputs_ReturnsNull(string domain)
    {
        ValidationHelpers.ValidateDomain(domain).Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // ValidateDomain - null/empty
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateDomain_NullOrEmpty_ReturnsError(string? domain)
    {
        var error = ValidationHelpers.ValidateDomain(domain);
        error.Should().NotBeNull();
        error!.Code.Should().Be("VALIDATION_FAILED");
    }

    // -----------------------------------------------------------------------
    // ValidateDomain - must have at least two labels
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateDomain_SingleLabel_ReturnsError()
    {
        var error = ValidationHelpers.ValidateDomain("myserver");
        error.Should().NotBeNull();
        error!.Message.Should().Contain("two labels");
    }

    // -----------------------------------------------------------------------
    // ValidateDomain - exceeds 253 chars
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateDomain_TooLong_ReturnsError()
    {
        var domain = string.Join(".", Enumerable.Repeat("a", 128)) + ".com";
        domain.Length.Should().BeGreaterThan(253);
        var error = ValidationHelpers.ValidateDomain(domain);
        error.Should().NotBeNull();
        error!.Message.Should().Contain("253");
    }

    // -----------------------------------------------------------------------
    // ValidateDomain - label-level violations
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateDomain_LabelTooLong_ReturnsError()
    {
        var domain = new string('a', 64) + ".com";
        var error = ValidationHelpers.ValidateDomain(domain);
        error.Should().NotBeNull();
        error!.Message.Should().Contain("1-63");
    }

    [Fact]
    public void ValidateDomain_EmptyLabel_ReturnsError()
    {
        var error = ValidationHelpers.ValidateDomain("myserver..com");
        error.Should().NotBeNull();
    }

    [Theory]
    [InlineData("My-Server.com")]       // uppercase in label
    [InlineData("my_server.com")]       // underscore in label
    [InlineData("-server.com")]         // leading hyphen in label
    [InlineData("server-.com")]         // trailing hyphen in label
    public void ValidateDomain_InvalidLabel_ReturnsError(string domain)
    {
        var error = ValidationHelpers.ValidateDomain(domain);
        error.Should().NotBeNull();
    }

    [Fact]
    public void ValidateDomain_ConsecutiveHyphensInLabel_ReturnsError()
    {
        var error = ValidationHelpers.ValidateDomain("my--server.com");
        error.Should().NotBeNull();
        error!.Message.Should().Contain("consecutive hyphens");
    }

    // -----------------------------------------------------------------------
    // ValidateDomain - reserved first label
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("docker.xcord.net")]
    [InlineData("registry.xcord.net")]
    [InlineData("postgres.example.com")]
    [InlineData("grafana.example.com")]
    public void ValidateDomain_ReservedFirstLabel_ReturnsError(string domain)
    {
        var error = ValidationHelpers.ValidateDomain(domain);
        error.Should().NotBeNull();
        error!.Code.Should().Be("RESERVED_SUBDOMAIN");
    }

    [Fact]
    public void ValidateDomain_ReservedInNonFirstLabel_ReturnsNull()
    {
        // "api" in a non-first position is fine
        ValidationHelpers.ValidateDomain("myserver.api.com").Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // IsReservedSubdomain
    // -----------------------------------------------------------------------

    [Fact]
    public void IsReservedSubdomain_CaseInsensitive()
    {
        ValidationHelpers.IsReservedSubdomain("API").Should().BeTrue();
        ValidationHelpers.IsReservedSubdomain("Admin").Should().BeTrue();
        ValidationHelpers.IsReservedSubdomain("myserver").Should().BeFalse();
    }
}
