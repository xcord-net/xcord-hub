using System.ComponentModel.DataAnnotations;

namespace XcordHub.Infrastructure.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    [Range(4, 31)]
    public int BcryptWorkFactor { get; set; } = 12;
}
