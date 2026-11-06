using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Translates failures of the Maxio SDK into <see cref="BillingException"/>s with a status and a reason.
/// </summary>
public static class MaxioErrors
{
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not BillingException)
        {
            throw Translate(ex);
        }
    }

    public static BillingException Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException api:
                var reason = ReadReason(api);
                var status = (int)api.StatusCode;
                if (status == 404)
                {
                    return new BillingException(404, reason ?? "Not found in the billing system.", ex);
                }
                if (status == 401 || status == 403)
                {
                    return new BillingException(502, "The billing system rejected the app's credentials.", ex);
                }
                if (status == 429 || status >= 500)
                {
                    return new BillingException(503, "The billing system is unavailable, try again later.", ex);
                }
                return new BillingException(422, reason ?? $"The billing system refused the request ({status}).", ex);
            case SdkConnectionException:
                return new BillingException(503, "The billing system could not be reached, try again later.", ex);
            case SdkException:
                return new BillingException(502, "The billing system call failed.", ex);
            default:
                return new BillingException(500, ex.Message, ex);
        }
    }

    /// <summary>Digs the human-readable reason out of the SDK's typed (or raw) error payload.</summary>
    private static string? ReadReason(ApiException api)
    {
        try
        {
            var error = api.GetType().GetProperty("Error")?.GetValue(api);
            if (error == null)
            {
                return null;
            }

            foreach (var method in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                         .Where(m => m.Name.StartsWith("TryGet", StringComparison.Ordinal)))
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].IsOut)
                {
                    continue;
                }
                var args = new object?[] { null };
                if (method.Invoke(error, args) is true && args[0] != null)
                {
                    var text = args[0] is MaxioAdvancedBilling.Core.ErrorResponse.RawError raw
                        ? raw.ReadAsString()
                        : JsonSerializer.Serialize(args[0], args[0]!.GetType());
                    var reason = FromJson(text);
                    if (!string.IsNullOrWhiteSpace(reason))
                    {
                        return reason;
                    }
                }
            }
        }
        catch (Exception)
        {
            // fall through: no readable reason
        }
        return null;
    }

    private static string? FromJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "errors", "error", "message" })
                {
                    if (doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
                    {
                        return Flatten(null, value);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // not JSON
        }
        return text.Length > 300 ? text[..300] : text;
    }

    private static string Flatten(string? key, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return key == null ? element.GetString()! : $"{key}: {element.GetString()}";
            case JsonValueKind.Array:
                var joined = string.Join("; ", element.EnumerateArray().Select(e => Flatten(null, e)).Distinct());
                return key == null ? joined : $"{key}: {joined}";
            case JsonValueKind.Object:
                return string.Join("; ", element.EnumerateObject().Select(p => Flatten(p.Name, p.Value)));
            default:
                return key == null ? element.ToString() : $"{key}: {element}";
        }
    }
}
