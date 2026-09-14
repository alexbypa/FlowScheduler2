using FlowScheduler.Core.Interfaces.Data;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FlowScheduler.Infrastructure.Database;

public class SqlServerConnectionFactory : IDbConnectionFactory {
    public bool SupportsStoredProcedures => true;
    public void AddParameter(DbCommand command, string name, object value) => ((SqlCommand)command).Parameters.AddWithValue(name, value);
    public DbCommand CreateCommand(string commandText, DbConnection connection) {
        var cmd = new SqlCommand(commandText, (SqlConnection)connection);
        cmd.CommandType = CommandType.StoredProcedure;
        return cmd;
    }
    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);
}