using FlowScheduler.Core.Dtos;
using FlowScheduler.Core.Interfaces.Processing;
using FlowScheduler.Core.Shared;
using Microsoft.Extensions.Logging;
using System.Data.Common;
using DashLogLevel = FlowScheduler.Core.Shared.LogLevel;

namespace FlowScheduler.Infrastructure.Processing;
public class ResultProcessorFromDatabase : CommandObservable, IResultProcessor {
    private readonly ILogger<ResultProcessorFromDatabase> _logger;
    public ResultProcessorFromDatabase(ILogger<ResultProcessorFromDatabase> logger) {
        _logger = logger;
    }
    public async Task<IEnumerable<IGrouping<string, Dictionary<string, object>>>> getErrorsGrouped(CreateTaskRequest taskRequest, object DataSource, CancellationToken cancellationToken = default) {
        IEnumerable<IGrouping<string, Dictionary<string, object>>> errorGroups = new List<IGrouping<string, Dictionary<string, object>>>();
        if (DataSource is not Task<DbDataReader> task) {
            throw new Exception("DataSource must be of type Task<DbDataReader>");
        }
        DbDataReader reader = await task;
        var allRows = new List<Dictionary<string, object>>();

        int resultSetIndex = 0;
        do {
            int rowsInSet = 0;
            while (await reader.ReadAsync(cancellationToken)) {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++) {
                    var name = reader.GetName(i);
                    row[name] = reader.GetValue(i);
                }
                allRows.Add(row);
                rowsInSet++;
            }
            _logger.LogInformation("[DB READER] ResultSet #{Index}: {Count} righe, {Columns} colonne",
                resultSetIndex, rowsInSet, reader.FieldCount);
            resultSetIndex++;
        } while (await reader.NextResultAsync(cancellationToken));

        _logger.LogInformation("[DB READER] Totale: {TotalSets} result set, {TotalRows} righe lette",
            resultSetIndex, allRows.Count);

        if (allRows.Any()) {
            List<Dictionary<string, object>> errorRows = new();
            List<Dictionary<string, object>> okRows = new();
            RaiseMessage(DashLogLevel.Info, $"[5] DB → Righe totali lette: {allRows.Count} | ErrorCondition: '{taskRequest.ErrorCondition ?? "(null)"}'");
            if (allRows.Count > 0) {
                var firstRow = allRows[0];
                var columns = string.Join(", ", firstRow.Keys);
                var values = string.Join(", ", firstRow.Select(kv => $"{kv.Key}={kv.Value}"));
                RaiseMessage(DashLogLevel.Info, $"[5b] DB → Colonne: [{columns}]");
                RaiseMessage(DashLogLevel.Info, $"[5c] DB → Prima riga: [{values}]");
            }
            if (!string.IsNullOrEmpty(taskRequest.ErrorCondition)) {
                // Se c'è una condizione, filtriamo
                foreach (var row in allRows) {
                    bool isError = jsonHelper.EvaluateCondition(row, taskRequest.ErrorCondition);
                    _logger.LogDebug("[DB EVAL] Riga: {Values} → isError={IsError}",
                        string.Join(", ", row.Select(kv => $"{kv.Key}={kv.Value} ({kv.Value?.GetType().Name})")),
                        isError);
                    if (isError)
                        errorRows.Add(row);
                    else
                        okRows.Add(row);
                }
            } else {
                // Se non c'è condizione di errore, è tutto OK per definizione
                okRows = allRows;
            }
            RaiseMessage(DashLogLevel.Info, $"[6] DB → ErrorRows: {errorRows.Count} | OkRows: {okRows.Count}");

            if (errorRows.Any()) {
                // Raggruppiamo per Source + Category per ricerche RAG mirate
                errorGroups = errorRows.GroupBy(row => {
                    var category = (row.ContainsKey("Source") ? row["Source"].ToString() : "Unknown") ?? "Unknown";
                    var subCategory = (row.ContainsKey("Category") ? row["Category"].ToString() : "") ?? "";
                    return string.IsNullOrEmpty(subCategory) ? category : $"{category}|{subCategory}";
                });
                RaiseMessage(DashLogLevel.Info, $"[7] DB → Errori trovati: {errorGroups.Count()} gruppi distinti. Passo il controllo all'agente AI...");
            }
        }
        return errorGroups;
    }
}
