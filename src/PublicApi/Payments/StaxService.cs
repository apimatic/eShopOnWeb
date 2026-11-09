using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Core.Hooks;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Charge;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Team;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Every call the shop makes to Stax goes through here, using the Stax SDK
/// (<c>sdk/stax</c>). Answers come back as <see cref="JsonElement"/>s because the SDK types most of them as <c>object</c>.
/// </summary>
public class StaxService
{
    private readonly StaxSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StaxService> _logger;
    private StaxFattMerchantApiClient? _shop;
    private StaxFattMerchantApiClient? _platform;
    private readonly object _gate = new();

    public StaxService(IOptions<StaxSettings> settings, IHttpClientFactory httpFactory, ILogger<StaxService> logger)
    {
        _settings = settings.Value;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    private string? ApiKey => FirstNonBlank(_settings.ApiKey, Environment.GetEnvironmentVariable("STAX_API_KEY"));
    private string? PlatformKey => FirstNonBlank(_settings.PlatformApiKey, Environment.GetEnvironmentVariable("STAX_PLATFORM_API_KEY"));

    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    /// <summary>The API's base address: Stax:BaseUrl verbatim when set, otherwise the SDK's public address.</summary>
    public string BaseUrl => !string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? _settings.BaseUrl!
        : new StaxFattMerchantApiClientOptions().Server.Default.Production.BaseUrl;

    public bool PlatformConfigured => PlatformKey is not null;

    private StaxFattMerchantApiClient Build(string key)
    {
        var options = new StaxFattMerchantApiClientOptions { BearerAuth = key };
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl!;
        return new StaxFattMerchantApiClient(_httpFactory.CreateClient(), options);
    }

    private StaxFattMerchantApiClient Shop
    {
        get
        {
            var key = ApiKey ?? throw new ApiProblem(422, "Stax is not configured: Stax:ApiKey is missing.");
            lock (_gate) return _shop ??= Build(key);
        }
    }

    private StaxFattMerchantApiClient Platform
    {
        get
        {
            var key = PlatformKey ??
                      throw new ApiProblem(422, "Seller onboarding is unavailable: Stax:PlatformApiKey is not configured.");
            lock (_gate) return _platform ??= Build(key);
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The SDK types some answers as void; this reads the body of a successful answer anyway.</summary>
    private static async Task<JsonElement?> Capture(Func<RequestOptions, Task> call, params SdkHook[] extraHooks)
    {
        JsonElement? captured = null;
        var hook = SdkHook.OnResponse(async (response, _, _) =>
        {
            if (!response.IsSuccessStatusCode) return;
            await response.Content.LoadIntoBufferAsync().ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                using var doc = JsonDocument.Parse(text);
                captured = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        });
        try
        {
            await call(new RequestOptions { Hooks = extraHooks.Append(hook).ToArray() }).ConfigureAwait(false);
        }
        catch (ResponseDeserializationException) when (captured is not null)
        {
            // The SDK's declared type did not fit the body, but the body was read successfully.
        }
        return captured;
    }

    /// <summary>The SDK does not expose some filters Stax supports (customer email, transaction customer); this adds them to one call.</summary>
    private static RequestOptions WithQuery(params (string Key, string Value)[] query) => new() { Hooks = new[] { QueryHook(query) } };

    private static SdkHook QueryHook(params (string Key, string Value)[] query) =>
        SdkHook.OnRequest((request, _) =>
        {
            var builder = new UriBuilder(request.RequestUri!);
            var extra = string.Join("&", query.Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value)));
            builder.Query = string.IsNullOrEmpty(builder.Query) ? extra : builder.Query.TrimStart('?') + "&" + extra;
            request.RequestUri = builder.Uri;
        });

    private static Dictionary<string, object?> Body(params (string Key, object? Value)[] fields)
    {
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in fields)
            if (v is not null) d[k] = v;
        return d;
    }

    // ---------------------------------------------------------------- customers

    /// <summary>Live Stax customers whose email is exactly this one.</summary>
    public async Task<List<JsonElement>> FindCustomers(string email, CancellationToken ct = default)
    {
        var found = new List<JsonElement>();
        for (var page = 1; page <= 20; page++)
        {
            var answer = Json.ToElement(await Shop.Customer.GetCustomers(
                new GetCustomersRequest { Page = page, PerPage = 100 }, WithQuery(("email", email)), ct));
            var items = Json.Items(answer);
            found.AddRange(items.Where(c =>
                string.Equals(c.Str("email"), email, StringComparison.OrdinalIgnoreCase) && c.Str("deleted_at") is null));
            var last = answer.Get("last_page")?.GetInt32() ?? 1;
            if (page >= last || items.Length == 0) break;
        }
        return found;
    }

    public async Task<JsonElement> CreateCustomer(string email, string? firstName, string? lastName, string? company,
        CancellationToken ct = default)
    {
        var body = Body(
            ("firstname", firstName),
            ("lastname", lastName),
            ("email", email),
            ("company", company),
            ("reference", email));
        return Json.ToElement(await Shop.Customer.PostCustomer(new PostCustomerRequest { Body = body }, null, ct));
    }

    // ---------------------------------------------------------------- payment methods

    public async Task<JsonElement> CreatePaymentMethod(Dictionary<string, object?> body, CancellationToken ct = default) =>
        Json.ToElement(await Shop.PaymentMethod.PostPaymentMethod(new PostPaymentMethodRequest { Body = body }, null, ct));

    /// <summary>Returns the payment method, removed ones included (they carry deleted_at); null when Stax does not know it.</summary>
    public async Task<JsonElement?> GetPaymentMethod(Guid id, CancellationToken ct = default)
    {
        try
        {
            return Json.ToElement(await Shop.PaymentMethod.GetPaymentMethod(new GetPaymentMethodRequest { Id = id }, null, ct));
        }
        catch (ApiException ex) when ((int)ex.StatusCode is 404 or 422 or 400)
        {
            return null;
        }
    }

    public async Task<List<JsonElement>> ListCustomerPaymentMethods(Guid customerId, CancellationToken ct = default)
    {
        var answer = await Shop.Customer.GetCustomerPaymentMethods(new GetCustomerPaymentMethodsRequest { Id = customerId }, null, ct);
        return answer.Select(Json.ToElement).Where(p => p.Str("deleted_at") is null).ToList();
    }

    public Task DeletePaymentMethod(Guid id, CancellationToken ct = default) =>
        Shop.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = id }, null, ct);

    /// <summary>Blanks the personal details Stax keeps on a payment method.</summary>
    public Task ScrubPaymentMethod(Guid id, CancellationToken ct = default) =>
        Shop.PaymentMethod.PutPaymentMethod(new PutPaymentMethodRequest
        {
            Id = id,
            Body = new Dictionary<string, object?>
            {
                ["person_name"] = "Closed",
                ["address_1"] = "",
                ["address_2"] = "",
                ["address_city"] = "",
                ["address_state"] = "",
                ["address_zip"] = "",
                ["address_country"] = ""
            }
        }, null, ct);

    /// <summary>
    /// Every payment method a shopper has ever had at Stax: the live ones, the ones the shop recorded, and, for
    /// ones removed behind the shop's back.
    /// </summary>
    public async Task<List<JsonElement>> AllPaymentMethods(IReadOnlyCollection<Guid> customerIds,
        IEnumerable<Guid> knownPaymentMethodIds, CancellationToken ct = default)
    {
        var result = new Dictionary<Guid, JsonElement>();
        foreach (var customerId in customerIds)
        {
            foreach (var pm in await ListCustomerPaymentMethods(customerId, ct))
                if (pm.GuidOf("id") is { } id) result[id] = pm;
        }

        foreach (var customerId in customerIds)
        {
            foreach (var pm in await ListDeletedPaymentMethods(customerId, ct))
                if (pm.GuidOf("id") is { } id) result[id] = pm;
        }

        // Belt and braces: anything the shop recorded that Stax did not list.
        foreach (var id in knownPaymentMethodIds.Where(i => !result.ContainsKey(i)))
        {
            var pm = await GetPaymentMethod(id, ct);
            if (pm is { } found && found.GuidOf("customer_id") is { } owner && customerIds.Contains(owner))
                result[id] = found;
        }

        return result.Values.ToList();
    }

    /// <summary>
    /// The customer's removed payment methods. Stax lists them on the payment-method list with status=deleted;
    /// the SDK types that list as an array although Stax answers a page, so the page is read before the SDK
    /// gives up on it.
    /// </summary>
    public async Task<List<JsonElement>> ListDeletedPaymentMethods(Guid customerId, CancellationToken ct = default)
    {
        var all = new List<JsonElement>();
        for (var page = 1; page <= 50; page++)
        {
            var answer = await Capture(o => Shop.PaymentMethod.GetPaymentMethods(o, ct),
                QueryHook(("customer_id", customerId.ToString()), ("status", "deleted"),
                    ("page", page.ToString(CultureInfo.InvariantCulture)), ("per_page", "100")));
            if (answer is null) break;
            var items = Json.Items(answer.Value);
            all.AddRange(items.Where(p => p.GuidOf("customer_id") == customerId));
            if (page >= (answer.Value.Get("last_page")?.GetInt32() ?? 1) || items.Length == 0) break;
        }
        return all;
    }

    // ---------------------------------------------------------------- fees and charges

    /// <summary>Asks Stax what surcharge a card payment of this size carries. The SDK has no wrapper for this call.</summary>
    public async Task<decimal> CardFee(Guid paymentMethodId, decimal amount, CancellationToken ct = default)
    {
        var key = ApiKey ?? throw new ApiProblem(422, "Stax is not configured: Stax:ApiKey is missing.");
        var url = BaseUrl.TrimEnd('/') + "/surcharge/review?payment_method_id=" + paymentMethodId +
                  "&total=" + amount.ToString("0.00", CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await _httpFactory.CreateClient().SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                throw new ApiProblem(code is 400 or 422 ? 422 : code == 404 ? 404 : 424,
                    "Stax: could not work out the card fee: " + ApiProblem.DescribeBody(text));
            }

            using var doc = JsonDocument.Parse(text);
            var review = doc.RootElement;
            var fee = review.Dec("surcharge_amount");
            if (fee is null && review.Dec("total_with_surcharge_amount") is { } withFee) fee = withFee - amount;
            return Math.Max(0m, Math.Round(fee ?? 0m, 2, MidpointRounding.AwayFromZero));
        }
        catch (HttpRequestException ex)
        {
            throw new ApiProblem(424, "Stax could not be reached: " + ex.Message, ex);
        }
        catch (JsonException ex)
        {
            throw new ApiProblem(424, "Stax answered the card fee review with something unreadable.", ex);
        }
    }

    public async Task<JsonElement> Charge(Guid paymentMethodId, decimal total, Dictionary<string, object?> meta,
        CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_method_id"] = paymentMethodId,
            ["total"] = total,
            ["pre_auth"] = false,
            ["meta"] = meta
        };
        return Json.ToElement(await Shop.Charge.PostCharge(new PostChargeRequest { Body = body }, null, ct));
    }

    public async Task<JsonElement> GetTransaction(Guid id, CancellationToken ct = default) =>
        Json.ToElement(await Shop.Transaction.GetTransaction(new GetTransactionRequest { Id = id }, null, ct));

    public async Task<JsonElement> Refund(Guid transactionId, CancellationToken ct = default) =>
        Json.ToElement(await Shop.Transaction.PostRefund(new PostRefundRequest { Id = transactionId }, null, ct));

    public async Task<JsonElement> Void(Guid transactionId, CancellationToken ct = default) =>
        Json.ToElement(await Shop.Transaction.PostVoid(new PostVoidRequest { Id = transactionId }, null, ct));

    // ---------------------------------------------------------------- erasing a customer

    public async Task EraseCustomer(Guid customerId, CancellationToken ct = default)
    {
        await Shop.Customer.PutCustomer(new PutCustomerRequest
        {
            Id = customerId,
            Body = new Dictionary<string, object?>
            {
                ["firstname"] = "Closed",
                ["lastname"] = "",
                ["email"] = "",
                ["company"] = "",
                ["phone"] = "",
                ["address_1"] = "",
                ["address_2"] = "",
                ["address_city"] = "",
                ["address_state"] = "",
                ["address_zip"] = "",
                ["address_country"] = "",
                ["reference"] = "",
                ["notes"] = ""
            }
        }, null, ct);
        await Shop.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = customerId }, null, ct);
    }

    // ---------------------------------------------------------------- invoice schedules

    public async Task<JsonElement> CreateInvoiceSchedule(Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var created = await Capture(o => Shop.InvoiceSchedule.PostInvoiceSchedule(
            new PostInvoiceScheduleRequest { Body = body }, o, ct));
        return created ?? throw new ApiProblem(424, "Stax created the invoice schedule but did not say which one.");
    }

    public async Task<JsonElement> SetInvoiceScheduleActive(Guid scheduleId, bool active, CancellationToken ct = default)
    {
        try
        {
            var updated = await Capture(o => Shop.InvoiceSchedule.PutInvoiceSchedule(
                new PutInvoiceScheduleRequest { Id = scheduleId, Body = new Dictionary<string, object?> { ["active"] = active } }, o, ct));
            return updated ?? JsonSerializer.SerializeToElement(new { id = scheduleId, active });
        }
        catch (ApiException<StaxFattMerchantApi.Core.ErrorResponse.RawError> ex)
            when ((int)ex.StatusCode is 404 or 422 && ex.Error.ReadAsString().Contains("could not be found", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiProblem(404, "No such invoice schedule.", ex);
        }
    }

    /// <summary>The first page of schedules: the SDK exposes no page parameter for this call.</summary>
    public async Task<List<JsonElement>> ListInvoiceSchedules(CancellationToken ct = default)
    {
        var answer = await Capture(o => Shop.InvoiceSchedule.GetInvoiceSchedules(o, ct));
        return answer is null ? new List<JsonElement>() : Json.Items(answer.Value).ToList();
    }

    public Task DeleteInvoiceSchedule(Guid id, CancellationToken ct = default) =>
        Shop.InvoiceSchedule.DeleteInvoiceSchedule(new DeleteInvoiceScheduleRequest { Id = id }, null, ct);

    // ---------------------------------------------------------------- sellers

    /// <summary>Creates a merchant under the Stax platform; only ever called with the platform key.</summary>
    public async Task<JsonElement> CreateMerchant(Dictionary<string, object?> body, CancellationToken ct = default) =>
        Json.ToElement(await Platform.Team.PostMerchant(new PostMerchantRequest { Body = body }, null, ct));
}
