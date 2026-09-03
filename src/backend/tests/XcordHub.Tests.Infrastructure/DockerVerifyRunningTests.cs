using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XcordHub.Infrastructure.Services;
using Xunit;

namespace XcordHub.Tests.Infrastructure;

/// <summary>
/// Covers how <see cref="HttpDockerService.VerifyContainerRunningAsync"/> reads a
/// Swarm service's tasks, against a scripted Docker API rather than a real one.
/// </summary>
/// <remarks>
/// This is the check that decides whether a resume succeeded, and it used to give
/// a different answer depending on the order Swarm happened to list two tasks in.
/// Scaling a service 0 -> 1 leaves the task that was shut down on the way to zero
/// visible for a moment beside the one starting up; seeing that first returned
/// false, and the instance was left marked Suspended with a container coming up
/// underneath it. Nothing about that is deterministic, which is why it surfaced
/// as an E2E chapter that passed or failed on its own.
/// </remarks>
public sealed class DockerVerifyRunningTests
{
    private const string ServiceId = "svc-under-test";

    /// <summary>
    /// The moment the old race lived in: a task Swarm has shut down is still
    /// listed, and the replacement has not reached running yet.
    /// </summary>
    [Fact]
    public async Task ATaskLeftOverFromTheScaleDown_DoesNotCountAsFailure()
    {
        var docker = ScriptedDocker.Returning(
            Tasks(("shutdown", "task exited"), ("starting", null)),
            Tasks(("shutdown", "task exited"), ("running", null)));

        var running = await docker.Service.VerifyContainerRunningAsync(ServiceId);

        running.Should().BeTrue(
            "the replacement task reached running - the shut-down one is history, not a verdict");
    }

    /// <summary>
    /// Order must not decide the answer. Same two tasks, listed the other way
    /// round, same result.
    /// </summary>
    [Fact]
    public async Task TaskOrderDoesNotChangeTheAnswer()
    {
        var docker = ScriptedDocker.Returning(
            Tasks(("running", null), ("shutdown", "task exited")));

        var running = await docker.Service.VerifyContainerRunningAsync(ServiceId);

        running.Should().BeTrue();
    }

    /// <summary>
    /// A gap between the old task disappearing and the new one being created is
    /// normal, and is not evidence of anything.
    /// </summary>
    [Fact]
    public async Task AnEmptyTaskListIsWaitedThrough()
    {
        var docker = ScriptedDocker.Returning(
            "[]",
            "[]",
            Tasks(("running", null)));

        var running = await docker.Service.VerifyContainerRunningAsync(ServiceId);

        running.Should().BeTrue();
    }

    /// <summary>
    /// The service really is dead: nothing running, every task terminal, and it
    /// stays that way. This must still fail - the point of the change was to stop
    /// guessing from one sample, not to stop reporting failure.
    /// </summary>
    [Fact]
    public async Task AServiceWhoseTasksAreAllTerminal_IsReportedAsNotRunning()
    {
        var docker = ScriptedDocker.Always(Tasks(("failed", "no such image")));

        var running = await docker.Service.VerifyContainerRunningAsync(ServiceId);

        running.Should().BeFalse();
        docker.Requests.Should().BeGreaterThan(1,
            "one terminal sample is the transient case; failure has to be confirmed");
    }

    /// <summary>
    /// A task that fails and is retried by Swarm ends up running, and the failed
    /// attempt alongside it must not short-circuit the wait.
    /// </summary>
    [Fact]
    public async Task ARetriedTaskThatEventuallyStarts_IsReportedAsRunning()
    {
        var docker = ScriptedDocker.Returning(
            Tasks(("failed", "start failed")),
            Tasks(("failed", "start failed"), ("preparing", null)),
            Tasks(("failed", "start failed"), ("running", null)));

        var running = await docker.Service.VerifyContainerRunningAsync(ServiceId);

        running.Should().BeTrue();
    }

    // -----------------------------------------------------------------------

    private static string Tasks(params (string State, string? Err)[] states)
    {
        var entries = states.Select(s =>
        {
            var status = "\"State\":\"" + s.State + "\"";
            if (s.Err is not null) status += ",\"Err\":\"" + s.Err + "\"";
            return "{\"Status\":{" + status + "}}";
        });
        return "[" + string.Join(",", entries) + "]";
    }

    /// <summary>
    /// A Docker API that answers /tasks from a script: one entry per poll, with
    /// the last entry repeating once the script runs out.
    /// </summary>
    private sealed class ScriptedDocker
    {
        public required HttpDockerService Service { get; init; }
        public required ScriptedHandler Handler { get; init; }
        public int Requests => Handler.Count;

        public static ScriptedDocker Returning(params string[] responses) => Build(responses);

        public static ScriptedDocker Always(string response) => Build([response]);

        private static ScriptedDocker Build(string[] responses)
        {
            var handler = new ScriptedHandler(responses);
            var client = new HttpClient(handler) { BaseAddress = new Uri("http://docker.invalid") };
            var options = Options.Create(new DockerOptions
            {
                SocketProxyUrl = "http://docker.invalid",
                UseReal = true,
                InstanceImage = "xcord-instance:test",
            });

            return new ScriptedDocker
            {
                Handler = handler,
                Service = new HttpDockerService(
                    new DirectHttpClientFactory(client),
                    NullLogger<HttpDockerService>.Instance,
                    options,
                    new FakeEnv("Development")),
            };
        }
    }

    private sealed class ScriptedHandler(string[] responses) : HttpMessageHandler
    {
        public int Count { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = responses[Math.Min(Count, responses.Length - 1)];
            Count++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class DirectHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeEnv(string env) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = env;
        public string ApplicationName { get; set; } = "XcordHub.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
