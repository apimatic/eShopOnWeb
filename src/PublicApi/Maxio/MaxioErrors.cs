using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Turns SDK failures into <see cref="MaxioProblemException"/>s with a status and a reason.</summary>
public static class MaxioErrors
{
    /// <summary>
    /// Maps any failure of an SDK call. <paramref name="action"/> is a short phrase such as "create the subscription".
    /// <paramref name="writeMayHaveLanded"/> marks writes whose outcome is unknown after a connection failure.
    /// </summary>
    public static MaxioProblemException Map(Exception ex, string action, bool writeMayHaveLanded = false)
    {
        switch (ex)
        {
            case MaxioProblemException problem:
                return problem;

            case ResponseDeserializationException:
                return new MaxioProblemException(HttpStatusCode.BadGateway, $"The billing system answered with an unexpected body when trying to {action}.", ex);

            case ApiException api:
                var detail = Describe(api);
                var status = (int)api.StatusCode;
                var reason = string.IsNullOrWhiteSpace(detail) ? $"Maxio refused to {action}" : $"Maxio refused to {action}: {detail}";
                return status switch
                {
                    404 => new MaxioProblemException(HttpStatusCode.NotFound, reason, ex),
                    400 or 409 or 422 => new MaxioProblemException((HttpStatusCode)status, reason, ex),
                    429 => new MaxioProblemException(HttpStatusCode.ServiceUnavailable, "The billing system is busy; try again shortly.", ex),
                    401 or 403 => new MaxioProblemException(HttpStatusCode.BadGateway, "The billing system rejected this application's credentials.", ex),
                    _ when status >= 400 && status < 500 => new MaxioProblemException(HttpStatusCode.UnprocessableEntity, reason, ex),
                    _ => new MaxioProblemException(HttpStatusCode.BadGateway, $"The billing system failed to {action} (HTTP {status}).", ex),
                };

            case SdkTimeoutException:
                return new MaxioProblemException(HttpStatusCode.GatewayTimeout,
                    $"The billing system timed out while trying to {action}." + (writeMayHaveLanded ? " The outcome is unknown; read the resource again before retrying." : ""), ex);

            case SdkConnectionException:
                return new MaxioProblemException(HttpStatusCode.BadGateway,
                    $"The billing system could not be reached to {action}." + (writeMayHaveLanded ? " The outcome is unknown; read the resource again before retrying." : ""), ex);

            case AuthSchemeException:
                return new MaxioProblemException(HttpStatusCode.BadGateway, "The billing system credentials are not usable.", ex);

            default:
                return new MaxioProblemException(HttpStatusCode.BadGateway, $"The billing system failed to {action}.", ex);
        }
    }

    /// <summary>The exception types a Maxio call can throw; used in catch filters.</summary>
    public static bool IsSdkFailure(Exception ex) => ex is SdkException;

    /// <summary>The HTTP status of an SDK API failure, or null.</summary>
    public static int? StatusOf(Exception ex) => ex is ApiException api ? (int)api.StatusCode : null;

    /// <summary>Human readable text from Maxio's error body (typed or raw).</summary>
    public static string Describe(ApiException api)
    {
        try
        {
            var errorProperty = api.GetType().GetProperty("Error");
            var error = errorProperty?.GetValue(api);
            var json = error switch
            {
                null => null,
                RawError raw => raw.ReadAsString(),
                ApiError typed => ReadTyped(typed),
                _ => JsonSerializer.Serialize(error, error.GetType()),
            };
            return json is null ? string.Empty : Flatten(json);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static string? ReadTyped(ApiError typed)
    {
        foreach (var method in typed.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!method.Name.StartsWith("TryGet", StringComparison.Ordinal) || method.Name == nameof(ApiError.TryGetRawError)) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].IsOut) continue;
            var args = new object?[] { null };
            if (method.Invoke(typed, args) is true && args[0] is not null)
                return JsonSerializer.Serialize(args[0], args[0]!.GetType());
        }
        return typed.TryGetRawError(out var raw) ? raw.ReadAsString() : null;
    }

    private static string Flatten(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var parts = new List<string>();
            Collect(doc.RootElement, null, parts);
            return parts.Count == 0 ? json.Trim() : string.Join("; ", parts.Distinct());
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static void Collect(JsonElement element, string? key, List<string> parts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)) parts.Add(key is null || key is "errors" or "error" ? text! : $"{key}: {text}");
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) Collect(item, key, parts);
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Collect(property.Value, property.Name, parts);
                break;
        }
    }
}
