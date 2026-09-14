namespace FlowScheduler.Core.Dtos;
public record CreateTaskRequest(
    string HangFireJobName,
    string GitHubOptionName, // Riferimento a una configurazione GitHub predefinita (token, repo, branch)
    string ConnectionString,
    string DatabaseType,    // ← NUOVO: "SqlServer", "PostgreSql"
    string Name,
    string CommandType,
    Dictionary<string, string> ParametersTask, // Accettiamo un oggetto che poi serializzeremo in JSON
    string ParseInJsonResultsTask,
    string ErrorCondition,
    string SqlTableToAnalizeWithLLM,
    string CronExpression
) {
    public override string ToString() => HangFireJobName;
};