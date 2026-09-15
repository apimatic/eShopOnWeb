using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// HTTP failure from the Maxio Advanced Billing API (per the OpenAPI error models).
/// </summary>
public class MaxioApiException : Exception
{
    public MaxioApiException(HttpStatusCode statusCode, string? responseBody)
        : base(BuildMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public string? ResponseBody { get; }

    public int HttpStatusCodeValue => (int)StatusCode;

    public static async Task ThrowFromResponse(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = response.Content != null
            ? await response.Content.ReadAsStringAsync(cancellationToken)
            : null;
        throw new MaxioApiException(response.StatusCode, Truncate(body));
    }

    private static string BuildMessage(HttpStatusCode statusCode, string? responseBody)
    {
        var detail = string.IsNullOrWhiteSpace(responseBody) ? "(empty body)" : Truncate(responseBody);
        return $"Maxio API request failed with {(int)statusCode} {statusCode}. Response: {detail}";
    }

    private static string? Truncate(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        const int max = 2000;
        return body.Length <= max ? body : body[..max];
    }

    public static string? TryReadErrorSummary(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (!doc.RootElement.TryGetProperty("errors", out var errors))
            {
                return null;
            }

            if (errors.ValueKind == JsonValueKind.Array)
            {
                return string.Join(" ", errors.EnumerateArray().Select(e => e.GetString()).Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            return errors.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
