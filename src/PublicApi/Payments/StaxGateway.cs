using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Core.Hooks;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Team;
using StaxFattMerchantApi.Requests.Transaction;
using StaxFattMerchantApi.Requests.Charge;
using StaxFattMerchantApi.Models;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The only place the shop talks to Stax. Wraps the Stax SDK: builds the clients from the
/// <c>Stax:</c> settings, hands back the JSON Stax answered with (several SDK operations discard
/// their response, so the body is captured with a response hook) and turns Stax failures into
/// <see cref="AppException"/>s the API can answer with.
/// </summary>
public class StaxGateway
{
    private readonly StaxSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StaxGateway> _logger;
    private StaxFattMerchantApiClient? _shop;
    private StaxFattMerchantApiClient? _platform;
    private readonly object _lock = new();

    public StaxGateway(IOptions<StaxSettings> settings, IHttpClientFactory httpClientFactory, ILogger<StaxGateway> logger)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public bool PlatformConfigured => !string.IsNullOrWhiteSpace(_settings.PlatformApiKey);

    private StaxFattMerchantApiClient Shop
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
                throw new AppException(HttpStatusCode.ServiceUnavailable, "Payments are not available: the Stax API key (Stax:ApiKey) is not configured.");
            lock (_lock) return _shop ??= Build(_settings.ApiKey);
        }
    }

    private StaxFattMerchantApiClient Platform
    {
        get
        {
            if (!PlatformConfigured)
                throw new AppException(HttpStatusCode.Conflict, "Seller onboarding is not available: this shop has no Stax platform key (Stax:PlatformApiKey) configured.");
            lock (_lock) return _platform ??= Build(_settings.PlatformApiKey!);
        }
    }

    private StaxFattMerchantApiClient Build(string key)
    {
        var options = new StaxFattMerchantApiClientOptions { BearerAuth = key };
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
        return new StaxFattMerchantApiClient(_httpClientFactory.CreateClient(), options);
    }

    // ------------------------------------------------------------------ plumbing

    /// <summary>Runs an SDK call and returns the JSON body Stax answered with.</summary>
    private async Task<JsonElement> Send(Func<RequestOptions, Task> call, params SdkHook[] extraHooks)
    {
        string? body = null;
        var capture = SdkHook.OnResponse(async (response, _, ct) =>
        {
            if (response.IsSuccessStatusCode)
                body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        });
        var hooks = new List<SdkHook>(extraHooks) { capture };
        try
        {
            await call(new RequestOptions { Hooks = hooks }).ConfigureAwait(false);
        }
        catch (ApiException<RawError> e)
        {
            throw Translate(e);
        }
        catch (SdkTimeoutException e)
        {
            throw new AppException(HttpStatusCode.GatewayTimeout, "Stax did not answer in time.", e);
        }
        catch (SdkException e)
        {
            _logger.LogWarning(e, "Stax call failed");
            throw new AppException(HttpStatusCode.BadGateway, "Stax could not be reached: " + e.Message, e);
        }

        if (string.IsNullOrWhiteSpace(body))
            return default;
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static AppException Translate(ApiException<RawError> e)
    {
        var text = ErrorText(e.Error.ReadAsString());
        var code = (int)e.StatusCode;
        var status = code switch
        {
            404 => HttpStatusCode.NotFound,
            401 or 403 => HttpStatusCode.BadGateway,
            >= 400 and < 500 => HttpStatusCode.UnprocessableEntity,
            _ => HttpStatusCode.BadGateway
        };
        var prefix = status == HttpStatusCode.BadGateway && code is 401 or 403
            ? "Stax rejected the shop's credentials"
            : "Stax refused the request";
        return new AppException(status, $"{prefix}: {text}", e) { StaxStatus = code, StaxMessage = text };
    }

    /// <summary>Flattens the different shapes Stax uses for errors into one readable line.</summary>
    public static string ErrorText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "no details given";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var parts = new List<string>();
            Collect(doc.RootElement, null, parts);
            return parts.Count == 0 ? raw : string.Join("; ", parts.Distinct());
        }
        catch (JsonException)
        {
            return raw.Length > 300 ? raw[..300] : raw;
        }

        static void Collect(JsonElement e, string? name, List<string> parts)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    parts.Add(name is null || name == "message" || name == "error" || int.TryParse(name, out _) ? e.GetString()! : $"{name}: {e.GetString()}");
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Collect(item, name, parts);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject()) Collect(p.Value, p.Name, parts);
                    break;
            }
        }
    }

    private static Guid Id(string id) =>
        Guid.TryParse(id, out var g) ? g : throw new AppException(HttpStatusCode.NotFound, "Unknown id.");

    private static SdkHook Query(Action<UriBuilder> change) => SdkHook.OnRequest((request, _) =>
    {
        var builder = new UriBuilder(request.RequestUri!);
        change(builder);
        request.RequestUri = builder.Uri;
    });

    // ------------------------------------------------------------------ customers

    public async Task<List<JsonElement>> FindCustomersByEmail(string email, CancellationToken ct = default)
    {
        var result = new List<JsonElement>();
        // The SDK sends "keywords=x" but Stax wants "keywords[]=x".
        var hook = Query(b => b.Query = b.Query.TrimStart('?').Replace("keywords=", "keywords%5B%5D=", StringComparison.Ordinal));
        for (var page = 1; page <= 20; page++)
        {
            var p = page;
            var body = await Send(o => Shop.Customer.GetCustomers(new GetCustomersRequest { Keywords = email, Page = p, PerPage = 100 }, o, ct), hook);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var data)) break;
            foreach (var c in data.EnumerateArray())
                if (string.Equals(Str(c, "email"), email, StringComparison.OrdinalIgnoreCase))
                    result.Add(c);
            if (Int(body, "current_page") >= Int(body, "last_page")) break;
        }
        return result;
    }

    public Task<JsonElement> CreateCustomer(string email, string firstName, string lastName, string? company, CancellationToken ct = default) =>
        Send(o => Shop.Customer.PostCustomer(new PostCustomerRequest
        {
            Body = new { firstname = firstName, lastname = lastName, email, company = company ?? string.Empty }
        }, o, ct));

    public Task<JsonElement> UpdateCustomer(string id, object body, CancellationToken ct = default) =>
        Send(o => Shop.Customer.PutCustomer(new PutCustomerRequest { Id = Id(id), Body = body }, o, ct));

    public Task<JsonElement> DeleteCustomer(string id, CancellationToken ct = default) =>
        Send(o => Shop.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = Id(id) }, o, ct));

    public async Task<List<JsonElement>> ListCustomerPaymentMethods(string customerId, CancellationToken ct = default)
    {
        var list = await Shop.CallList(() => Shop.Customer.GetCustomerPaymentMethods(new GetCustomerPaymentMethodsRequest { Id = Id(customerId) }, null, ct));
        return list;
    }

    // ------------------------------------------------------------------ payment methods

    public Task<JsonElement> CreatePaymentMethod(object body, CancellationToken ct = default) =>
        Send(o => Shop.PaymentMethod.PostPaymentMethod(new PostPaymentMethodRequest { Body = body }, o, ct));

    public Task<JsonElement> GetPaymentMethod(string id, CancellationToken ct = default) =>
        Send(o => Shop.PaymentMethod.GetPaymentMethod(new GetPaymentMethodRequest { Id = Id(id) }, o, ct));

    public Task<JsonElement> UpdatePaymentMethod(string id, object body, CancellationToken ct = default) =>
        Send(o => Shop.PaymentMethod.PutPaymentMethod(new PutPaymentMethodRequest { Id = Id(id), Body = body }, o, ct));

    public Task<JsonElement> DeletePaymentMethod(string id, CancellationToken ct = default) =>
        Send(o => Shop.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = Id(id) }, o, ct));

    // ------------------------------------------------------------------ transactions

    public Task<JsonElement> Charge(object body, CancellationToken ct = default) =>
        Send(o => Shop.Charge.PostCharge(new PostChargeRequest { Body = body }, o, ct));

    public Task<JsonElement> GetTransaction(string id, CancellationToken ct = default) =>
        Send(o => Shop.Transaction.GetTransaction(new GetTransactionRequest { Id = Id(id) }, o, ct));

    public async Task<List<JsonElement>> ListCustomerTransactions(string customerId, CancellationToken ct = default)
    {
        var result = new List<JsonElement>();
        var hook = Query(b => b.Query = b.Query.TrimStart('?') + "&customer_id=" + Uri.EscapeDataString(customerId));
        for (var page = 1; page <= 50; page++)
        {
            var p = page;
            var body = await Send(o => Shop.Transaction.GetTransactions(new GetTransactionsRequest { Page = p, PerPage = 100 }, o, ct), hook);
            if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("data", out var data)) break;
            result.AddRange(data.EnumerateArray());
            if (Int(body, "current_page") >= Int(body, "last_page")) break;
        }
        return result;
    }

    /// <summary>Voids the transaction when it can still be voided, otherwise refunds it. Amount is the whole total.</summary>
    public Task<JsonElement> VoidOrRefund(string transactionId, decimal total, CancellationToken ct = default) =>
        Send(o => Shop.Transaction.PostVoidOrRefund(new PostVoidOrRefundRequest
        {
            Id = Id(transactionId),
            Body = new PostVoidOrRefundRequestBodyJson { Total = (double)total }
        }, o, ct));

    public Task<JsonElement> GetRegistration(CancellationToken ct = default) =>
        Send(o => Shop.TeamRegistration.GetTeamRegistration(o, ct));

    // ------------------------------------------------------------------ invoice schedules

    public Task<JsonElement> CreateSchedule(object body, CancellationToken ct = default) =>
        Send(o => Shop.InvoiceSchedule.PostInvoiceSchedule(new PostInvoiceScheduleRequest { Body = body }, o, ct));

    public Task<JsonElement> GetSchedule(string id, CancellationToken ct = default) =>
        Send(o => Shop.InvoiceSchedule.GetInvoiceSchedule(new GetInvoiceScheduleRequest { Id = Id(id) }, o, ct));

    public Task<JsonElement> UpdateSchedule(string id, object body, CancellationToken ct = default) =>
        Send(o => Shop.InvoiceSchedule.PutInvoiceSchedule(new PutInvoiceScheduleRequest { Id = Id(id), Body = body }, o, ct));

    // ------------------------------------------------------------------ sellers

    public Task<JsonElement> CreateMerchant(object body, CancellationToken ct = default) =>
        Send(o => Platform.Team.PostMerchant(new PostMerchantRequest { Body = body }, o, ct));

    // ------------------------------------------------------------------ json helpers

    public static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static int Int(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) && i != 0));

    public static decimal? Dec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    /// <summary>Stax timestamps look like "2026-11-09 14:25:24" (UTC).</summary>
    public static DateTimeOffset? Time(JsonElement e, string name)
    {
        var s = Str(e, name);
        return s != null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : null;
    }
}

internal static class StaxClientExtensions
{
    /// <summary>Runs an SDK call that answers with a JSON array and translates Stax failures.</summary>
    public static async Task<List<JsonElement>> CallList(this StaxFattMerchantApiClient _, Func<Task<IReadOnlyList<object>>> call)
    {
        try
        {
            var items = await call().ConfigureAwait(false);
            return items.Select(i => i is JsonElement je ? je : JsonSerializer.SerializeToElement(i)).ToList();
        }
        catch (ApiException<RawError> e)
        {
            var text = StaxGateway.ErrorText(e.Error.ReadAsString());
            var code = (int)e.StatusCode;
            throw new AppException(code == 404 ? HttpStatusCode.NotFound : code is >= 400 and < 500 && code != 401 && code != 403 ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.BadGateway,
                "Stax refused the request: " + text, e) { StaxStatus = code, StaxMessage = text };
        }
        catch (SdkException e)
        {
            throw new AppException(HttpStatusCode.BadGateway, "Stax could not be reached: " + e.Message, e);
        }
    }
}
