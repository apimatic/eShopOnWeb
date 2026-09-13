using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? RawBody { get; }

    public MaxioApiException(HttpStatusCode statusCode, string? rawBody, string message)
        : base(message)
    {
        StatusCode = statusCode;
        RawBody = rawBody;
    }
}

public static class MaxioErrorHandler
{
    public static async Task HandleErrorResponseAsync(HttpResponseMessage response, ILogger logger)
    {
        var body = await response.Content.ReadAsStringAsync();
        logger.LogWarning("Maxio API error {StatusCode}: {Body}", response.StatusCode, body);

        string message = $"Maxio API returned {(int)response.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errors", out var errors))
            {
                var errorMessages = new List<string>();
                foreach (var error in errors.EnumerateArray())
                {
                    errorMessages.Add(error.GetString() ?? error.ToString());
                }
                message = $"Maxio API error: {string.Join("; ", errorMessages)}";
            }
        }
        catch
        {
            // Use default message if JSON parsing fails
        }

        throw new MaxioApiException(response.StatusCode, body, message);
    }
}
