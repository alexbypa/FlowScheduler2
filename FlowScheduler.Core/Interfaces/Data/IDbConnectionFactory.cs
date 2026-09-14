using System.Data.Common;

namespace FlowScheduler.Core.Interfaces.Data;

public interface IDbConnectionFactory {
    DbConnection CreateConnection(string connectionString);
    DbCommand CreateCommand(string commandText, DbConnection connection);
    void AddParameter(DbCommand command, string name, object value);
    bool SupportsStoredProcedures { get; }
}
