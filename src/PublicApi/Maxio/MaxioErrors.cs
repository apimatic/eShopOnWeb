using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Runs Maxio calls and turns every way they can fail into a <see cref="BillingException"/>
/// carrying an HTTP status and a reason.
/// </summary>
public class MaxioErrors
{
    private readonly ILogger<MaxioErrors> _logger;

    public MaxioErrors(ILogger<MaxioErrors> logger)
    {
        _logger = logger;
    }

    public async Task<T> RunAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not BillingException && ex is not OperationCanceledException)
        {
            throw Translate(operation, ex);
        }
    }

    public async Task RunAsync(string operation, Func<Task> call)
    {
        await RunAsync<bool>(operation, async () =>
        {
            await call();
            return true;
        });
    }

    /// <summary>True when the request may have reached Maxio, so the outcome must be settled by a re-read.</summary>
    public static bool IsUnknownOutcome(Exception ex) => ex is SdkConnectionException;

    /// <summary>Maxio answered with this HTTP status.</summary>
    public static bool IsStatus(Exception ex, HttpStatusCode status) =>
        ex is ApiException api && api.StatusCode == status;

    public BillingException Translate(string operation, Exception ex)
    {
        switch (ex)
        {
            case SdkTimeoutException:
                _logger.LogError(ex, "Maxio {Operation} timed out", operation);
                return new BillingException(504, "The billing system did not answer in time. Try again shortly.", ex);
            case SdkConnectionException:
                _logger.LogError(ex, "Maxio {Operation} could not connect", operation);
                return new BillingException(502, "The billing system could not be reached.", ex);
            case ApiException api:
                return TranslateApi(operation, api);
            case AuthSchemeException:
                _logger.LogError(ex, "Maxio {Operation} has no usable credentials", operation);
                return new BillingException(502, "The billing system is not configured correctly.", ex);
            default:
                _logger.LogError(ex, "Maxio {Operation} failed unexpectedly", operation);
                return new BillingException(502, "The billing system call failed.", ex);
        }
    }

    private BillingException TranslateApi(string operation, ApiException api)
    {
        var status = (int)api.StatusCode;
        var reason = Reason(api);

        // A body that does not match its declared type (ResponseDeserializationException) is also an ApiException.
        if (status is >= 200 and < 300)
        {
            _logger.LogError(api, "Maxio {Operation} returned an unreadable {Status} response", operation, status);
            return new BillingException(502, "The billing system returned an unexpected response.", api);
        }

        if (status is 401 or 403)
        {
            _logger.LogError("Maxio {Operation} refused our credentials ({Status})", operation, status);
            return new BillingException(502, "The billing system refused the application's credentials.", api);
        }

        if (status >= 500 || status == 429)
        {
            _logger.LogError("Maxio {Operation} failed with {Status}: {Reason}", operation, status, reason);
            return new BillingException(status == 429 ? 503 : 502, $"The billing system is unavailable ({status}).", api);
        }

        _logger.LogWarning("Maxio {Operation} rejected the request with {Status}: {Reason}", operation, status, reason);
        return status switch
        {
            404 => new BillingException(404, $"Not found in the billing system. {reason}".Trim(), api),
            409 => new BillingException(409, reason, api),
            _ => new BillingException(422, reason, api)
        };
    }

    /// <summary>The reason Maxio gave, read from whichever error shape the operation declares.</summary>
    public static string Reason(ApiException api)
    {
        string? body = null;
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            switch (error)
            {
                case RawError raw:
                    body = raw.ReadAsString();
                    break;
                case ApiError typed when typed.TryGetRawError(out var typedRaw):
                    body = typedRaw.ReadAsString();
                    break;
                case ApiError typed:
                    body = TypedPayloadJson(typed);
                    break;
            }
        }
        catch (Exception)
        {
            // fall through to the generic reason
        }

        var summary = Summarize(body);
        return string.IsNullOrWhiteSpace(summary)
            ? $"The billing system rejected the request ({(int)api.StatusCode})."
            : summary!;
    }

    /// <summary>
    /// A typed error holds its payload behind one TryGet{Shape}(out value) accessor per declared shape;
    /// whichever one answers true carries Maxio's own error body.
    /// </summary>
    private static string? TypedPayloadJson(ApiError typed)
    {
        foreach (var method in typed.GetType().GetMethods().Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal) && m.Name != nameof(ApiError.TryGetRawError)))
        {
            var parameters = method.GetParameters();
            if (parameters.Length != 1 || !parameters[0].ParameterType.IsByRef)
            {
                continue;
            }

            var args = new object?[1];
            if (method.Invoke(typed, args) is true && args[0] is not null)
            {
                return JsonSerializer.Serialize(args[0], args[0]!.GetType());
            }
        }

        return null;
    }

    private static string? Summarize(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var messages = new List<string>();
            Collect(doc.RootElement, messages);
            if (messages.Count > 0)
            {
                return Truncate(string.Join(" ", messages.Distinct()));
            }
        }
        catch (JsonException)
        {
            // not JSON: use the text as it is
        }

        return Truncate(body.Trim());
    }

    private static void Collect(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, messages);
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var text = property.Value.GetString()!;
                        messages.Add(property.Name is "error" or "errors" or "message" ? text : $"{property.Name}: {text}");
                    }
                    else
                    {
                        Collect(property.Value, messages);
                    }
                }
                break;
        }
    }

    private static string Truncate(string text) => text.Length <= 400 ? text : text[..400] + "...";
}
