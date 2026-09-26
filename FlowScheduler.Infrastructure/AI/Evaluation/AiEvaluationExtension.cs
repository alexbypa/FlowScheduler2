using FlowScheduler.Core.Interfaces.AI;
using Microsoft.Extensions.DependencyInjection;

namespace FlowScheduler.Infrastructure.AI.Evaluation;

public static class AiEvaluationExtension {
    public static IServiceCollection AddAiEvaluation(this IServiceCollection services) {
        services.AddScoped<IRagEvaluator, RagEvaluationService>();
        services.AddScoped<IEvalInterpretationService, EvalInterpretationService>();
        return services;
    }
}
