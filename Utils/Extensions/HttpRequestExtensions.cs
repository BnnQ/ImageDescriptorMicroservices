#nullable enable
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;

namespace Microservices.Utils.Extensions;

public static class HttpRequestExtensions
{
    public static async Task<string?> GetParameterValueOrDefaultAsync(this HttpRequest request, string parameterName)
    {
        if (!request.Query.TryGetValue(parameterName, out var value))
        {
            var serializedRequestBody = await new StreamReader(request.Body).ReadToEndAsync();
            dynamic? requestBody = JsonConvert.DeserializeObject(serializedRequestBody);
            if (requestBody is not null)
            {
                value = requestBody[parameterName];   
            }
        }

        return value != StringValues.Empty ? value.ToString() : null;
    }
}