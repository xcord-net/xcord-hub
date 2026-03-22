using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace XcordHub.Tests.Unit.Infrastructure;

/// <summary>
/// Minimal IHostEnvironment implementation for unit tests that need to control
/// which environment name is reported (e.g., "Production", "Development").
/// </summary>
internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "XcordHub.Tests.Unit";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
