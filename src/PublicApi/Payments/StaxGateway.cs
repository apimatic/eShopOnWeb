using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Holds the SDK clients built from the <c>Stax:</c> configuration section
/// (<c>Stax:ApiKey</c>, <c>Stax:PlatformApiKey</c>, <c>Stax:BaseUrl</c>) and translates SDK failures
/// into <see cref="PaymentException"/>s.
/// </summary>
public sealed class StaxGateway
{
    private readonly Lazy<StaxFattMerchantApiClient?> _client;
    private readonly Lazy<StaxFattMerchantApiClient?> _platform;

    public StaxGateway(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        var apiKey = Clean(configuration["Stax:ApiKey"]) ?? Clean(Environment.GetEnvironmentVariable("STAX_API_KEY"));
        var platformKey = Clean(configuration["Stax:PlatformApiKey"]) ?? Clean(Environment.GetEnvironmentVariable("STAX_PLATFORM_API_KEY"));
        var baseUrl = Clean(configuration["Stax:BaseUrl"]);

        StaxFattMerchantApiClient? Build(string? key)
        {
            if (key == null) return null;
            var options = new StaxFattMerchantApiClientOptions { BearerAuth = key };
            if (baseUrl != null)
            {
                // used verbatim as the API's base address
                options.Server.Default.Production.BaseUrl = baseUrl;
            }
            return new StaxFattMerchantApiClient(httpClientFactory.CreateClient(), options);
        }

        _client = new Lazy<StaxFattMerchantApiClient?>(() => Build(apiKey));
        _platform = new Lazy<StaxFattMerchantApiClient?>(() => Build(platformKey));
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public bool HasPlatformKey => _platform.Value != null;

    /// <summary>The client for the shop's own merchant account.</summary>
    public StaxFattMerchantApiClient Client =>
        _client.Value ?? throw new PaymentException(503, "Payments are unavailable: Stax:ApiKey is not configured.");

    /// <summary>The client for the platform account (merchant onboarding), when the shop has a platform key.</summary>
    public StaxFattMerchantApiClient? Platform => _platform.Value;

    public Task<T> Call<T>(Func<StaxFattMerchantApiClient, Task<T>> action) => Run(Client, action);

    public Task Call(Func<StaxFattMerchantApiClient, Task> action) =>
        Run<object?>(Client, async c => { await action(c); return null; });

    public Task<T> CallPlatform<T>(Func<StaxFattMerchantApiClient, Task<T>> action) =>
        Run(Platform ?? throw new PaymentException(422,
            "Seller onboarding is unavailable: the shop has no Stax platform key (Stax:PlatformApiKey)."), action);

    private static async Task<T> Run<T>(StaxFattMerchantApiClient client, Func<StaxFattMerchantApiClient, Task<T>> action)
    {
        try
        {
            return await action(client);
        }
        catch (Exception ex) when (ex is not PaymentException)
        {
            throw Translate(ex);
        }
    }

    public static Exception Translate(Exception ex)
    {
        switch (ex)
        {
            case ApiException<RawError> api:
                var detail = Describe(api.Error.ReadAsString());
                var code = (int)api.StatusCode;
                if (code == 404) return new PaymentException(404, $"Not found at Stax{Suffix(detail)}", ex);
                if (code == 400 || code == 422) return new PaymentException(422, $"Stax rejected the request{Suffix(detail)}", ex);
                if (code == 401 || code == 403) return new PaymentException(502, "Stax rejected the shop's credentials.", ex);
                if (code == 429) return new PaymentException(503, "Stax is rate limiting requests; try again shortly.", ex);
                return new PaymentException(502, $"Stax returned {code}{Suffix(detail)}", ex);
            case SdkTimeoutException:
                return new PaymentException(504, "Stax did not respond in time.", ex);
            case SdkConnectionException:
            case HttpRequestException:
                return new PaymentException(503, "Stax could not be reached.", ex);
            default:
                return ex;
        }
    }

    private static string Suffix(string? detail) => string.IsNullOrEmpty(detail) ? "." : $": {detail}";

    private static string? Describe(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new List<string>();
            Collect(doc.RootElement, parts);
            return parts.Count == 0 ? null : string.Join(" ", parts);
        }
        catch (JsonException)
        {
            return body.Length > 200 ? body[..200] : body;
        }
    }

    private static void Collect(JsonElement e, List<string> parts)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String: parts.Add(e.GetString()!); break;
            case JsonValueKind.Array: foreach (var i in e.EnumerateArray()) Collect(i, parts); break;
            case JsonValueKind.Object: foreach (var p in e.EnumerateObject()) Collect(p.Value, parts); break;
        }
    }
}

/// <summary>Reads loosely typed SDK results (<c>object</c> responses come back as <see cref="JsonElement"/>).</summary>
public static class Js
{
    public static JsonElement El(object o) => o is JsonElement e ? e : JsonSerializer.SerializeToElement(o);

    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
        && v.ValueKind != JsonValueKind.Null && v.ValueKind != JsonValueKind.Undefined ? v : null;

    public static string? Str(this JsonElement e, string name)
    {
        var v = e.Prop(name);
        if (v == null) return null;
        return v.Value.ValueKind == JsonValueKind.String ? v.Value.GetString() : v.Value.ToString();
    }

    public static decimal? Dec(this JsonElement e, string name)
    {
        var v = e.Prop(name);
        if (v == null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number) return v.Value.GetDecimal();
        return decimal.TryParse(v.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static bool? Bool(this JsonElement e, string name)
    {
        var v = e.Prop(name);
        if (v == null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.Value.GetDecimal() != 0,
            JsonValueKind.String => v.Value.GetString() is "1" or "true" or "True",
            _ => null
        };
    }

    /// <summary>Stax timestamps look like "2026-11-09 18:30:48" (UTC).</summary>
    public static DateTime? Time(this JsonElement e, string name)
    {
        var s = e.Str(name);
        return !string.IsNullOrWhiteSpace(s) &&
               DateTime.TryParse(s, CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;
    }

    /// <summary>Items of a paginated Stax page ({"data": [...]}) or a plain array.</summary>
    public static IEnumerable<JsonElement> Items(JsonElement page)
    {
        var arr = page.ValueKind == JsonValueKind.Array ? page : page.Prop("data") ?? default;
        if (arr.ValueKind != JsonValueKind.Array) yield break;
        foreach (var i in arr.EnumerateArray()) yield return i;
    }

    public static int LastPage(JsonElement page) => (int)(page.Dec("last_page") ?? 1);
}
