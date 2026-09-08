using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string responseBody, string endpoint)
        : base(BuildMessage(statusCode, responseBody, endpoint))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        Endpoint = endpoint;
    }

    public HttpStatusCode StatusCode { get; }

    public string ResponseBody { get; }

    public string Endpoint { get; }

    public string ErrorMessage => TryReadErrors(ResponseBody);

    private static string BuildMessage(HttpStatusCode statusCode, string responseBody, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return $"Maxio request to '{endpoint}' failed with HTTP {(int)statusCode} ({statusCode}).";
        }

        return $"Maxio request to '{endpoint}' failed with HTTP {(int)statusCode} ({statusCode}). Response: {responseBody}";
    }

    private static string TryReadErrors(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return responseBody;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("errors", out JsonElement errors))
            {
                return responseBody;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                var messages = errors.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToList();

                if (messages.Count > 0)
                {
                    return string.Join(" ", messages);
                }
            }
            else if (errors.ValueKind == JsonValueKind.Object)
            {
                var builder = new StringBuilder();
                foreach (JsonProperty property in errors.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        builder.Append(property.Name).Append(": ").Append(property.Value.GetString()).Append(' ');
                    }
                }

                if (builder.Length > 0)
                {
                    return builder.ToString().TrimEnd();
                }
            }

            return responseBody;
        }
        catch (JsonException)
        {
            return responseBody;
        }
    }
}
