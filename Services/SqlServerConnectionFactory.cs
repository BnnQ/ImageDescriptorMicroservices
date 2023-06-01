using System.Data;
using Microservices.Services.Abstractions;
using Microsoft.Data.SqlClient;

namespace Microservices.Services;

public class SqlServerConnectionFactory : IDatabaseConnectionFactory
{
    private readonly string connectionString;

    public SqlServerConnectionFactory(string connectionString)
    {
        this.connectionString = connectionString;
    }
    
    public IDbConnection CreateConnection()
    {
        return new SqlConnection(connectionString);
    }
}