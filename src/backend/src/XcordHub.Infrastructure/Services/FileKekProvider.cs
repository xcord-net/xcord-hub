using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace XcordHub.Infrastructure.Services;

/// <summary>
/// Resolves the KEK from a Docker secret file or configuration.
/// Priority: file at KekFile path → config Encryption:Kek → null (no KEK).
/// </summary>
public sealed class FileKekProvider : IKekProvider
{
    private readonly byte[]? _kek;

    public FileKekProvider(IConfiguration configuration, ILogger<FileKekProvider> logger)
    {
        var kekFile = configuration.GetSection("Encryption:KekFile").Value
            ?? "/run/secrets/xcord-kek";
        var kekBase64 = configuration.GetSection("Encryption:Kek").Value;

        if (File.Exists(kekFile))
        {
            var fileContent = File.ReadAllText(kekFile).Trim();
            _kek = Convert.FromBase64String(fileContent);
            logger.LogInformation("KEK loaded from file {KekFile}", kekFile);
        }
        else if (!string.IsNullOrEmpty(kekBase64))
        {
            _kek = Convert.FromBase64String(kekBase64);
            logger.LogInformation("KEK loaded from configuration");
        }
        else
        {
            _kek = null;
        }
    }

    public byte[]? GetKek() => _kek;
}
