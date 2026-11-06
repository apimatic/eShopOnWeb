using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Turns failures of Maxio SDK calls into <see cref="BillingException"/>s carrying a status and a reason.
/// </summary>
public static class MaxioErrors
{
    public static BillingException Translate(Exception ex)
    {
        switch (ex)
        {
            case BillingException be:
                return be;
            case ApiException api:
                return FromApi(api);
            case AuthSchemeException:
                return new BillingException(503, "Billing is not configured: no usable Maxio credentials.", ex);
            case SdkException sdk:
                return new BillingException(502, $"The billing system could not be reached: {sdk.Message}", ex);
            default:
                return new BillingException(500, ex.Message, ex);
        }
    }

    private static BillingException FromApi(ApiException api)
    {
        var reason = ExtractReason(api);
        var code = (int)api.StatusCode;

        // Our own credentials being refused is not the caller's fault.
        if (code == 401 || code == 403)
            return new BillingException(502, "The billing system rejected this application's credentials.", api);
        if (code == 429)
            return new BillingException(503, "The billing system is busy, try again shortly.", api);
        if (code >= 500)
            return new BillingException(502, $"The billing system failed: {reason}", api);
        if (code == 404)
            return new BillingException(404, string.IsNullOrWhiteSpace(reason) ? "Not found." : reason, api);
        if (code == 422 || code == 400)
            return new BillingException(code, string.IsNullOrWhiteSpace(reason) ? "The billing system rejected the request." : reason, api);
        return new BillingException(code is >= 400 and < 500 ? code : 502, string.IsNullOrWhiteSpace(reason) ? api.Message : reason, api);
    }

    private static string ExtractReason(ApiException api)
    {
        try
        {
            var errorProp = api.GetType().GetProperty("Error");
            if (errorProp?.GetValue(api) is not ApiError error) return api.Message;

            foreach (var method in error.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!method.Name.StartsWith("TryGet", StringComparison.Ordinal)) continue;
                var ps = method.GetParameters();
                if (ps.Length != 1 || !ps[0].ParameterType.IsByRef) continue;
                var args = new object?[1];
                if (method.Invoke(error, args) is true && args[0] != null)
                {
                    var text = Describe(args[0]!);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        catch
        {
            // fall through to the generic message
        }
        return api.Message;
    }

    private static string Describe(object value)
    {
        if (value is RawError raw)
        {
            var body = raw.ReadAsString();
            try
            {
                using var doc = JsonDocument.Parse(body);
                return FromJson(doc.RootElement);
            }
            catch
            {
                return body;
            }
        }

        var errors = value.GetType().GetProperty("Errors")?.GetValue(value);
        if (errors == null) return value.ToString() ?? string.Empty;
        return FromObject(errors);
    }

    private static string FromJson(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("errors", out var errs)) return FromJson(errs);
        switch (e.ValueKind)
        {
            case JsonValueKind.String: return e.GetString() ?? string.Empty;
            case JsonValueKind.Array: return string.Join("; ", e.EnumerateArray().Select(FromJson).Distinct());
            case JsonValueKind.Object:
                return string.Join("; ", e.EnumerateObject().Select(p => $"{p.Name}: {FromJson(p.Value)}"));
            default: return e.ToString();
        }
    }

    private static string FromObject(object o)
    {
        switch (o)
        {
            case string s: return s;
            case JsonElement je: return FromJson(je);
            case IDictionary dict:
                return string.Join("; ", dict.Keys.Cast<object>().Select(k => $"{k}: {FromObject(dict[k]!)}"));
            case IEnumerable en:
                return string.Join("; ", en.Cast<object>().Select(FromObject).Distinct());
            default: return o.ToString() ?? string.Empty;
        }
    }
}
