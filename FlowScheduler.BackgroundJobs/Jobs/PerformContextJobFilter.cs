using CSharpEssentials.LoggerHelper.Sink.HangfireConsole;
using Hangfire.Server;

namespace FlowScheduler.BackgroundJobs.Jobs;

/// <summary>
/// Hangfire server filter that automatically sets IPerformContextAccessor
/// before each job execution. This decouples PerformContext from the
/// IBackgroundJobHandler interface contract, keeping Core free of Hangfire types.
/// </summary>
public class PerformContextJobFilter : IServerFilter {
    private readonly IPerformContextAccessor _accessor;

    public PerformContextJobFilter(IPerformContextAccessor accessor) {
        _accessor = accessor;
    }

    public void OnPerforming(PerformingContext context) {
        _accessor.Set(context);
    }

    public void OnPerformed(PerformedContext context) {
        // AsyncLocal is scoped to the execution context.
        // BackgroundJobHandler clears it in its finally block.
    }
}
