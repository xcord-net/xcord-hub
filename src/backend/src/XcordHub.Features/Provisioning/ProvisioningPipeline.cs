using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XcordHub.Entities;
using XcordHub.Infrastructure.Data;
using XcordHub.Infrastructure.Services;
using XcordHub;

namespace XcordHub.Features.Provisioning;

public sealed class ProvisioningPipeline
{
    private readonly HubDbContext _dbContext;
    private readonly ILogger<ProvisioningPipeline> _logger;
    private readonly ProvisioningMetrics _metrics;
    private readonly GatewayMetrics _gatewayMetrics;
    private readonly List<IProvisioningStep> _steps;

    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20)
    ];

    public ProvisioningPipeline(
        HubDbContext dbContext,
        ILogger<ProvisioningPipeline> logger,
        ProvisioningMetrics metrics,
        GatewayMetrics gatewayMetrics,
        IEnumerable<IProvisioningStep> steps)
    {
        _dbContext = dbContext;
        _logger = logger;
        _metrics = metrics;
        _gatewayMetrics = gatewayMetrics;
        _steps = steps.ToList();
    }

    public async Task<Result<bool>> RunAsync(long instanceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting provisioning pipeline for instance {InstanceId}", instanceId);

        var startTime = DateTimeOffset.UtcNow;

        var instance = await _dbContext.ManagedInstances
            .Include(i => i.ProvisioningEvents)
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance == null)
        {
            return Error.NotFound("INSTANCE_NOT_FOUND", $"Instance {instanceId} not found");
        }

        // Determine where to resume from
        var lastCompletedStep = GetLastCompletedStep(instance.ProvisioningEvents);
        var startIndex = lastCompletedStep != null
            ? _steps.FindIndex(s => s.StepName == lastCompletedStep) + 1
            : 0;

        _logger.LogInformation("Resuming from step {StartIndex} (last completed: {LastStep})",
            startIndex, lastCompletedStep ?? "none");

        // Execute each step sequentially
        for (int i = startIndex; i < _steps.Count; i++)
        {
            var step = _steps[i];
            _logger.LogInformation("Executing step {StepIndex}/{TotalSteps}: {StepName}",
                i + 1, _steps.Count, step.StepName);

            var executeResult = await ExecuteStepWithRetry(instanceId, step, ProvisioningPhase.Execute, cancellationToken);
            if (executeResult.IsFailure)
            {
                _logger.LogError("Step {StepName} execution failed: {Error}", step.StepName, executeResult.Error?.Message ?? "Unknown error");
                await MarkInstanceFailed(instanceId, cancellationToken);
                return executeResult;
            }

            _logger.LogInformation("Verifying step {StepName}", step.StepName);

            var verifyResult = await ExecuteStepWithRetry(instanceId, step, ProvisioningPhase.Verify, cancellationToken);
            if (verifyResult.IsFailure)
            {
                _logger.LogError("Step {StepName} verification failed: {Error}", step.StepName, verifyResult.Error?.Message ?? "Unknown error");
                await MarkInstanceFailed(instanceId, cancellationToken);
                return verifyResult;
            }

            _logger.LogInformation("Step {StepName} completed successfully", step.StepName);
        }

        // All steps completed, mark instance as Running
        await MarkInstanceRunning(instanceId, cancellationToken);

        // Record provisioning metrics
        var duration = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        _metrics.RecordProvisioningDuration(duration);
        _gatewayMetrics.RecordInstanceProvisioned();

        _logger.LogInformation("Provisioning pipeline completed successfully for instance {InstanceId} in {Duration}s", instanceId, duration);
        return true;
    }

    private async Task<Result<bool>> ExecuteStepWithRetry(
        long instanceId,
        IProvisioningStep step,
        ProvisioningPhase phase,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (attempt < MaxRetries)
        {
            attempt++;

            var eventId = await RecordProvisioningEvent(instanceId, step.StepName, phase, ProvisioningStepStatus.InProgress, null, cancellationToken);

            try
            {
                Result<bool> result = phase == ProvisioningPhase.Execute
                    ? await step.ExecuteAsync(instanceId, cancellationToken)
                    : await step.VerifyAsync(instanceId, cancellationToken);

                if (result.IsSuccess)
                {
                    await UpdateProvisioningEvent(eventId, ProvisioningStepStatus.Completed, null, cancellationToken);
                    _metrics.RecordProvisioningStep(step.StepName, success: true);
                    return result;
                }

                _logger.LogWarning("Step {StepName} {Phase} failed (attempt {Attempt}/{MaxRetries}): {Error}",
                    step.StepName, phase, attempt, MaxRetries, result.Error?.Message ?? "Unknown error");

                await UpdateProvisioningEvent(eventId, ProvisioningStepStatus.Failed, result.Error?.Message, cancellationToken);

                if (attempt == MaxRetries)
                {
                    _metrics.RecordProvisioningStep(step.StepName, success: false);
                }

                if (attempt < MaxRetries)
                {
                    var delay = RetryDelays[attempt - 1];
                    _logger.LogInformation("Retrying in {Delay}s", delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    return result; // Max retries reached
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Step {StepName} {Phase} threw exception (attempt {Attempt}/{MaxRetries})",
                    step.StepName, phase, attempt, MaxRetries);

                await UpdateProvisioningEvent(eventId, ProvisioningStepStatus.Failed, ex.Message, cancellationToken);

                if (attempt < MaxRetries)
                {
                    var delay = RetryDelays[attempt - 1];
                    _logger.LogInformation("Retrying in {Delay}s", delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    _metrics.RecordProvisioningStep(step.StepName, success: false);
                    return Error.Failure("STEP_EXCEPTION", $"Step {step.StepName} threw exception: {ex.Message}");
                }
            }
        }

        return Error.Failure("MAX_RETRIES_EXCEEDED", $"Step {step.StepName} failed after {MaxRetries} attempts");
    }

    private async Task<long> RecordProvisioningEvent(
        long instanceId,
        string stepName,
        ProvisioningPhase phase,
        ProvisioningStepStatus status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var provisioningEvent = new ProvisioningEvent
        {
            ManagedInstanceId = instanceId,
            Phase = phase,
            StepName = stepName,
            Status = status,
            ErrorMessage = errorMessage,
            StartedAt = DateTimeOffset.UtcNow
        };

        _dbContext.ProvisioningEvents.Add(provisioningEvent);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return provisioningEvent.Id;
    }

    private async Task UpdateProvisioningEvent(
        long eventId,
        ProvisioningStepStatus status,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var provisioningEvent = await _dbContext.ProvisioningEvents
            .FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);

        if (provisioningEvent != null)
        {
            provisioningEvent.Status = status;
            provisioningEvent.ErrorMessage = errorMessage;
            provisioningEvent.CompletedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private string? GetLastCompletedStep(ICollection<ProvisioningEvent> events)
    {
        // Find the last step where both Execute and Verify phases completed successfully
        var completedSteps = events
            .Where(e => e.Status == ProvisioningStepStatus.Completed)
            .GroupBy(e => e.StepName)
            .Where(g => g.Any(e => e.Phase == ProvisioningPhase.Execute && e.Status == ProvisioningStepStatus.Completed) &&
                        g.Any(e => e.Phase == ProvisioningPhase.Verify && e.Status == ProvisioningStepStatus.Completed))
            .Select(g => new
            {
                StepName = g.Key,
                LastCompleted = g.Max(e => e.CompletedAt)
            })
            .OrderByDescending(x => x.LastCompleted)
            .FirstOrDefault();

        return completedSteps?.StepName;
    }

    private async Task MarkInstanceRunning(long instanceId, CancellationToken cancellationToken)
    {
        var instance = await _dbContext.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance != null)
        {
            instance.Status = InstanceStatus.Running;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task MarkInstanceFailed(long instanceId, CancellationToken cancellationToken)
    {
        var instance = await _dbContext.ManagedInstances
            .FirstOrDefaultAsync(i => i.Id == instanceId, cancellationToken);

        if (instance != null)
        {
            instance.Status = InstanceStatus.Failed;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
