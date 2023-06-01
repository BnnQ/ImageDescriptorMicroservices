using System.Data;

namespace Microservices.Services.Abstractions;

public interface IDatabaseConnectionFactory
{
    public IDbConnection CreateConnection();
}