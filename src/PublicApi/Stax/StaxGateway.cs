using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Core.Hooks;
using StaxFattMerchantApi.Core.Models;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Settings read from the <c>Stax:</c> configuration section.</summary>
public class StaxOptions
{
    public const string SectionName = "Stax";

    /// <summary>Stax:ApiKey - the shop's own (merchant) key.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Stax:PlatformApiKey - only present when the shop is a Stax platform (marketplace).</summary>
    public string? PlatformApiKey { get; set; }

    /// <summary>Stax:BaseUrl - optional override of the API's base address, used verbatim.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Stax:SyncIntervalSeconds - how often pending bank payments and refunds are re-checked.</summary>
    public int SyncIntervalSeconds { get; set; } = 300;
}

/// <summary>
/// Owns the Stax SDK clients. The shop client uses <c>Stax:ApiKey</c>; the platform client (only used to onboard
/// sellers) uses <c>Stax:PlatformApiKey</c>.
/// </summary>
public sealed class StaxGateway
{
    private readonly StaxOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly object _gate = new();
    private StaxFattMerchantApiClient? _shop;
    private StaxFattMerchantApiClient? _platform;

    public StaxGateway(IOptions<StaxOptions> options, IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_options.PlatformApiKey);

    public StaxFattMerchantApiClient Shop
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                throw new RequestRefusedException(503, "Payments are unavailable: the shop's Stax API key (Stax:ApiKey) is not configured.");
            }

            lock (_gate)
            {
                return _shop ??= Create(_options.ApiKey!);
            }
        }
    }

    public StaxFattMerchantApiClient Platform
    {
        get
        {
            if (!HasPlatformKey)
            {
                throw new RequestRefusedException(409, "Seller onboarding is unavailable: this shop has no Stax platform key (Stax:PlatformApiKey) configured.");
            }

            lock (_gate)
            {
                return _platform ??= Create(_options.PlatformApiKey!);
            }
        }
    }

    private StaxFattMerchantApiClient Create(string key)
    {
        var options = new StaxFattMerchantApiClientOptions { BearerAuth = key };
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = _options.BaseUrl!;
        }

        return new StaxFattMerchantApiClient(_httpClientFactory.CreateClient(), options);
    }

    /// <summary>
    /// The SDK has no operation for Stax's surcharge preview (GET /surcharge/review), so this one call is made
    /// directly with the shop's key against the same base address as the SDK.
    /// </summary>
    public async Task<JsonElement> ReviewSurchargeAsync(string paymentMethodId, decimal total, CancellationToken ct)
    {
        _ = Shop; // fails with the proper reason when the key is missing
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? new StaxFattMerchantApiClientOptions().Server.Default.Production.BaseUrl
            : _options.BaseUrl!;
        var url = $"{baseUrl.TrimEnd('/')}/surcharge/review?payment_method_id={Uri.EscapeDataString(paymentMethodId)}" +
                  $"&total={total.ToString("0.00", CultureInfo.InvariantCulture)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var http = _httpClientFactory.CreateClient();
            using var response = await http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw StaxCall.Refusal((int)response.StatusCode, body);
            }

            return JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (HttpRequestException ex)
        {
            throw new RequestRefusedException(502, "Stax could not be reached to work out the fee.", ex);
        }
    }
}

/// <summary>Runs SDK calls and turns Stax failures into <see cref="RequestRefusedException"/>.</summary>
public static class StaxCall
{
    public static async Task<JsonElement> Json(Func<Task<object>> call)
    {
        var result = await Run(call);
        return result is JsonElement e ? e : JsonSerializer.SerializeToElement(result);
    }

    public static async Task<List<JsonElement>> JsonList(Func<Task<IReadOnlyList<object>>> call)
    {
        var result = await Run(call);
        var list = new List<JsonElement>();
        foreach (var item in result)
        {
            list.Add(item is JsonElement e ? e : JsonSerializer.SerializeToElement(item));
        }

        return list;
    }

    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    public static async Task Run(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception ex) when (Translate(ex) is { } translated)
        {
            throw translated;
        }
    }

    /// <summary>True when Stax answered with a refusal (as opposed to being unreachable).</summary>
    public static bool IsStaxRefusal(RequestRefusedException ex) => ex.StatusCode is >= 400 and < 500;

    private static RequestRefusedException? Translate(Exception ex)
    {
        switch (ex)
        {
            case RequestRefusedException:
                return null;
            case ApiException<RawError> api:
                return Refusal((int)api.StatusCode, api.Error.ReadAsString());
            case SdkTimeoutException:
                return new RequestRefusedException(504, "Stax did not answer in time.", ex);
            case SdkConnectionException:
                return new RequestRefusedException(502, "Stax could not be reached.", ex);
            case AuthSchemeException:
                return new RequestRefusedException(503, "Payments are unavailable: no Stax credentials are configured.", ex);
            case SdkException:
                return new RequestRefusedException(502, "Stax answered with something unexpected: " + ex.Message, ex);
            default:
                return null;
        }
    }

    /// <summary>Builds the shop's answer from a Stax error status and body.</summary>
    public static RequestRefusedException Refusal(int staxStatus, string body)
    {
        var reason = ExtractReason(body);
        return staxStatus switch
        {
            404 => new RequestRefusedException(404, $"Not found at Stax{Suffix(reason)}"),
            401 or 403 => new RequestRefusedException(502, $"Stax refused the shop's credentials{Suffix(reason)}"),
            >= 400 and < 500 => new RequestRefusedException(422, reason ?? $"Stax refused the request (HTTP {staxStatus})."),
            _ => new RequestRefusedException(502, $"Stax failed to process the request (HTTP {staxStatus}){Suffix(reason)}")
        };
    }

    private static string Suffix(string? reason) => string.IsNullOrEmpty(reason) ? "." : ": " + reason;

    public static string? ExtractReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new List<string>();
            Collect(doc.RootElement, parts, null);
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    private static void Collect(JsonElement e, List<string> parts, string? name)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var text = e.GetString();
                if (!string.IsNullOrWhiteSpace(text) && !parts.Contains(text!))
                {
                    parts.Add(name is null || name is "message" or "error" or "0" ? text! : $"{name}: {text}");
                }
                break;
            case JsonValueKind.Object:
                // "message" and "error" are the canonical keys; field errors are collected otherwise.
                if (e.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    Collect(msg, parts, null);
                    return;
                }

                foreach (var p in e.EnumerateObject())
                {
                    Collect(p.Value, parts, p.Name);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                {
                    Collect(item, parts, name);
                }
                break;
        }
    }

    /// <summary>Request options that add query parameters the SDK does not model.</summary>
    public static RequestOptions WithQuery(params (string Key, string Value)[] query) => new()
    {
        Hooks =
        [
            SdkHook.OnRequest((request, _) =>
            {
                var builder = new UriBuilder(request.RequestUri!);
                var extra = string.Join("&", Array.ConvertAll(query,
                    q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value)}"));
                builder.Query = string.IsNullOrEmpty(builder.Query) ? extra : builder.Query.TrimStart('?') + "&" + extra;
                request.RequestUri = builder.Uri;
            })
        ]
    };

    /// <summary>Request options carrying an Idempotency-Key, so a retried call cannot charge twice.</summary>
    public static RequestOptions WithIdempotencyKey(string key) => new()
    {
        Headers = [new HeaderParam("Idempotency-Key", key)]
    };

    /// <summary>
    /// Captures the response body of an SDK operation that the SDK models as "no content" although Stax answers
    /// with the created/updated resource.
    /// </summary>
    public sealed class BodyCapture
    {
        public string? Body { get; private set; }

        public RequestOptions Options => new()
        {
            Hooks =
            [
                SdkHook.OnResponse(async (response, _, ct) =>
                {
                    if (response.IsSuccessStatusCode && response.Content is not null)
                    {
                        await response.Content.LoadIntoBufferAsync();
                        Body = await response.Content.ReadAsStringAsync();
                    }
                })
            ]
        };

        public JsonElement Element()
        {
            if (string.IsNullOrWhiteSpace(Body))
            {
                throw new RequestRefusedException(502, "Stax accepted the request but did not return the resulting record.");
            }

            return JsonDocument.Parse(Body).RootElement.Clone();
        }
    }
}

/// <summary>Small helpers to read Stax's JSON.</summary>
public static class StaxJson
{
    public static string? Str(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => p.GetString(),
            _ => p.ToString()
        };
    }

    public static decimal? Dec(this JsonElement e, string name)
    {
        var s = e.Str(name);
        return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static bool? Bool(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => p.GetDouble() != 0,
            JsonValueKind.String => p.GetString() is "1" or "true" or "True" ? true : p.GetString() is "0" or "false" or "False" ? false : null,
            _ => null
        };
    }

    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind != JsonValueKind.Null ? p : null;

    /// <summary>The records of a Stax list answer, whether it is a bare array or a page with <c>data</c>.</summary>
    public static IEnumerable<JsonElement> Records(this JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            return e.EnumerateArray();
        }

        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray();
        }

        return [];
    }

    public static int LastPage(this JsonElement e) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty("last_page", out var p) && p.TryGetInt32(out var n) ? n : 1;

    /// <summary>Stax timestamps are UTC strings such as "2026-11-09 20:07:53".</summary>
    public static DateTimeOffset? Date(this JsonElement e, string name)
    {
        var s = e.Str(name);
        return !string.IsNullOrWhiteSpace(s) &&
               DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d
            : null;
    }
}
