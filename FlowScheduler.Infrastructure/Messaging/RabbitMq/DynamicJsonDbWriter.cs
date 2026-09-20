using System.Data.Common;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlowScheduler.Core.Interfaces.Data;
using Microsoft.Extensions.Logging;

namespace FlowScheduler.Infrastructure.Messaging.RabbitMq;

public partial class DynamicJsonDbWriter(
    IDbConnectionFactoryResolver connectionFactoryResolver,
    ILogger<DynamicJsonDbWriter> logger)
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    [GeneratedRegex(@"^[a-zA-Z_][a-zA-Z0-9_.]*$", options: RegexOptions.Compiled)]
    private static partial Regex SafeIdentifierRegex();

    public async Task WriteBatchAsync(
        IReadOnlyList<string> jsonBatch,
        string connectionString,
        string databaseType,
        string tableName,
        CancellationToken cancellationToken)
    {
        if (jsonBatch.Count == 0) return;

        if (!SafeIdentifierRegex().IsMatch(tableName))
            throw new ArgumentException($"Invalid table name: '{tableName}'. Must match ^[a-zA-Z_][a-zA-Z0-9_.]*$", nameof(tableName));

        var factory = connectionFactoryResolver.Resolve(databaseType);
        var rows = ParseBatch(jsonBatch);
        if (rows.Count == 0) return;

        var columns = rows[0].Keys.ToList();
        ValidateColumnNames(columns);
        var sql = BuildInsertSql(tableName, columns);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await using var connection = factory.CreateConnection(connectionString);
                await connection.OpenAsync(cancellationToken);

                foreach (var row in rows)
                {
                    await using var command = factory.CreateCommand(sql, connection);
                    command.CommandType = System.Data.CommandType.Text;
                    foreach (var col in columns)
                        factory.AddParameter(command, $"@{col}", row.GetValueOrDefault(col) ?? DBNull.Value);

                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                logger.LogDebug("Persisted {Count} rows to {Table}", rows.Count, tableName);
                return;
            }
            catch (DbException ex) when (attempt < MaxRetries)
            {
                logger.LogError(ex, "DB write attempt {Attempt}/{MaxRetries} failed for {Table}, retrying",
                    attempt, MaxRetries, tableName);
                await Task.Delay(RetryDelay, cancellationToken);
            }
        }
    }

    private static List<Dictionary<string, object?>> ParseBatch(IReadOnlyList<string> jsonBatch)
    {
        var rows = new List<Dictionary<string, object?>>(jsonBatch.Count);

        foreach (var json in jsonBatch)
        {
            using var doc = JsonDocument.Parse(json);
            var row = new Dictionary<string, object?>();

            foreach (var prop in doc.RootElement.EnumerateObject())
                row[prop.Name] = ConvertJsonValue(prop.Value);

            if (row.Count > 0)
                rows.Add(row);
        }

        return rows;
    }

    private static object? ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number when element.TryGetInt64(out var l) => l,
        JsonValueKind.Number => element.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => DBNull.Value,
        _ => element.GetRawText()
    };

    private static void ValidateColumnNames(List<string> columns)
    {
        foreach (var col in columns)
        {
            if (!SafeIdentifierRegex().IsMatch(col))
                throw new ArgumentException($"Invalid column name: '{col}'. Must match ^[a-zA-Z_][a-zA-Z0-9_.]*$");
        }
    }

    private static string BuildInsertSql(string tableName, List<string> columns)
    {
        var cols = string.Join(", ", columns);
        var pars = string.Join(", ", columns.Select(c => $"@{c}"));
        return $"INSERT INTO {tableName} ({cols}) VALUES ({pars})";
    }
}
