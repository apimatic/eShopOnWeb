using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// The <c>Stax:</c> configuration section. Values come from configuration (user-secrets in development),
/// never from source.
/// </summary>
public class StaxOptions
{
    public const string SectionName = "Stax";

    public string? ApiKey { get; set; }
    public string? PlatformApiKey { get; set; }
    /// <summary>Optional override of the Stax API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>The app cannot carry out the request: answered with the given 4xx (or 5xx) status and the reason.</summary>
public class PaymentRequestException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public PaymentRequestException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public static PaymentRequestException BadRequest(string message) => new(HttpStatusCode.BadRequest, message);
    public static PaymentRequestException NotFound(string message) => new(HttpStatusCode.NotFound, message);
    public static PaymentRequestException Conflict(string message) => new(HttpStatusCode.Conflict, message);
    public static PaymentRequestException Unprocessable(string message) => new(HttpStatusCode.UnprocessableEntity, message);
}

/// <summary>Stax answered with an error (or could not be reached).</summary>
public class StaxApiException : Exception
{
    /// <summary>The HTTP status Stax answered with; 0 when Stax could not be reached.</summary>
    public int StaxStatus { get; }

    public StaxApiException(int staxStatus, string reason, Exception? inner = null) : base(reason, inner)
    {
        StaxStatus = staxStatus;
    }

    /// <summary>The status this app answers with: Stax rejecting the request is the caller's 4xx, anything else is a 502.</summary>
    public HttpStatusCode ResponseStatus =>
        StaxStatus is >= 400 and < 500 and not 401 and not 403 and not 429
            ? (StaxStatus == 404 ? HttpStatusCode.NotFound : HttpStatusCode.UnprocessableEntity)
            : HttpStatusCode.BadGateway;

    /// <summary>What Stax said, without this app's framing.</summary>
    public string Reason => base.Message;

    public override string Message =>
        ResponseStatus == HttpStatusCode.BadGateway
            ? $"The payment provider could not complete the request: {base.Message}"
            : $"The payment provider rejected the request: {base.Message}";
}

/// <summary>Builds the Stax SDK clients from the <c>Stax:</c> settings.</summary>
public sealed class StaxClients
{
    private readonly IOptionsMonitor<StaxOptions> _options;
    private readonly IHttpClientFactory _httpClients;
    private readonly object _gate = new();
    private (string Key, string? BaseUrl, StaxFattMerchantApiClient Client)? _shop;
    private (string Key, string? BaseUrl, StaxFattMerchantApiClient Client)? _platform;

    public StaxClients(IOptionsMonitor<StaxOptions> options, IHttpClientFactory httpClients)
    {
        _options = options;
        _httpClients = httpClients;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.CurrentValue.ApiKey);
    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_options.CurrentValue.PlatformApiKey);

    /// <summary>The client for the shop's own Stax account.</summary>
    public StaxFattMerchantApiClient Shop
    {
        get
        {
            var o = _options.CurrentValue;
            if (string.IsNullOrWhiteSpace(o.ApiKey))
            {
                throw new PaymentRequestException(HttpStatusCode.ServiceUnavailable,
                    "Payments are not available: the shop's Stax API key (Stax:ApiKey) is not configured.");
            }
            return Get(ref _shop, o.ApiKey!, o.BaseUrl);
        }
    }

    /// <summary>The client for the Stax platform account, or null when the shop has no platform key.</summary>
    public StaxFattMerchantApiClient? Platform
    {
        get
        {
            var o = _options.CurrentValue;
            return string.IsNullOrWhiteSpace(o.PlatformApiKey) ? null : Get(ref _platform, o.PlatformApiKey!, o.BaseUrl);
        }
    }

    private StaxFattMerchantApiClient Get(ref (string Key, string? BaseUrl, StaxFattMerchantApiClient Client)? slot, string key, string? baseUrl)
    {
        lock (_gate)
        {
            if (slot is { } s && s.Key == key && s.BaseUrl == baseUrl)
            {
                return s.Client;
            }

            var options = new StaxFattMerchantApiClientOptions { BearerAuth = key };
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                // used verbatim as the API's base address
                options.Server.Default.Production.BaseUrl = baseUrl!;
            }
            var client = new StaxFattMerchantApiClient(_httpClients.CreateClient(), options);
            slot = (key, baseUrl, client);
            return client;
        }
    }
}

/// <summary>Helpers for reading the loosely typed JSON the Stax SDK returns.</summary>
public static class StaxJson
{
    public static JsonElement ToElement(object? value) =>
        value is JsonElement e ? e : JsonSerializer.SerializeToElement(value);

    public static JsonElement? Prop(this JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
            ? v
            : null;

    public static string? Str(this JsonElement el, string name)
    {
        var v = el.Prop(name);
        return v?.ValueKind switch
        {
            JsonValueKind.String => v.Value.GetString(),
            JsonValueKind.Number => v.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static decimal? Dec(this JsonElement el, string name)
    {
        var v = el.Prop(name);
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetDecimal(out var d)) return d;
        if (v.Value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(v.Value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out d)) return d;
        return null;
    }

    public static bool Bool(this JsonElement el, string name)
    {
        var v = el.Prop(name);
        return v?.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.Value.TryGetInt32(out var i) && i != 0,
            JsonValueKind.String => bool.TryParse(v.Value.GetString(), out var b) && b || v.Value.GetString() == "1",
            _ => false
        };
    }

    /// <summary>Stax timestamps are UTC, formatted "yyyy-MM-dd HH:mm:ss".</summary>
    public static DateTimeOffset? Time(this JsonElement el, string name)
    {
        var s = el.Str(name);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }

    /// <summary>Flattens a Stax error body ({"error":{"message":..}}, {"field":["msg"]}, ...) into one sentence.</summary>
    public static string DescribeError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "no details were given";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new List<string>();
            Collect(doc.RootElement, parts);
            var distinct = new List<string>();
            foreach (var p in parts) if (!distinct.Contains(p)) distinct.Add(p);
            return distinct.Count == 0 ? "no details were given" : string.Join(" ", distinct);
        }
        catch (JsonException)
        {
            var text = body!.Trim();
            return text.Length > 300 ? text[..300] : text;
        }
    }

    private static void Collect(JsonElement el, List<string> parts)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) parts.Add(s!);
                break;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray()) Collect(item, parts);
                break;
            case JsonValueKind.Object:
                foreach (var p in el.EnumerateObject()) Collect(p.Value, parts);
                break;
        }
    }
}
