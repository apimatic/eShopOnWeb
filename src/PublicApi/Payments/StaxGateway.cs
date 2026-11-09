using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.MerchantAdmin;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Surcharge;
using StaxFattMerchantApi.Requests.Transaction;
using Charges = StaxFattMerchantApi.Requests.Charge;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Every call to Stax goes through the Stax SDK (sdk/stax) from here. Responses are the real Stax responses.
/// </summary>
public class StaxGateway
{
    private readonly StaxSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Lazy<StaxFattMerchantApiClient> _merchant;
    private readonly Lazy<StaxFattMerchantApiClient> _platform;

    public StaxGateway(IOptions<StaxSettings> settings, IHttpClientFactory httpClientFactory)
    {
        _settings = settings.Value;
        _httpClientFactory = httpClientFactory;
        _merchant = new Lazy<StaxFattMerchantApiClient>(() => CreateClient(_settings.ApiKey!));
        _platform = new Lazy<StaxFattMerchantApiClient>(() => CreateClient(_settings.PlatformApiKey!));
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_settings.PlatformApiKey);

    private StaxFattMerchantApiClient CreateClient(string apiKey)
    {
        var options = new StaxFattMerchantApiClientOptions { BearerAuth = apiKey };
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            // Used verbatim as the API's base address.
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl!;
        }
        return new StaxFattMerchantApiClient(_httpClientFactory.CreateClient(), options);
    }

    private StaxFattMerchantApiClient Merchant
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                throw new PaymentException((int)HttpStatusCode.ServiceUnavailable, "Payments are not configured: Stax:ApiKey is not set.");
            }
            return _merchant.Value;
        }
    }

    // ---------------------------------------------------------------- plumbing

    public static JsonElement ToJson(object? value)
    {
        if (value is JsonElement e) return e;
        return JsonSerializer.SerializeToElement(value);
    }

    /// <summary>Runs an SDK call and turns Stax / transport failures into PaymentExceptions with a readable reason.</summary>
    private static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (PaymentException)
        {
            throw;
        }
        catch (SdkTimeoutException ex)
        {
            throw new PaymentException((int)HttpStatusCode.GatewayTimeout, "Stax did not answer in time.", ex);
        }
        catch (SdkException ex)
        {
            throw new PaymentException((int)HttpStatusCode.BadGateway, "Stax could not be reached or answered unexpectedly: " + ex.Message, ex);
        }
    }

    private static Task Run(Func<Task> call) => Run<int>(async () => { await call(); return 0; });

    public static PaymentException Translate(ApiException<RawError> ex)
    {
        var reason = ExtractReason(ex.Error.ReadAsString());
        var code = (int)ex.StatusCode;
        if (code == 422 && reason.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
        {
            code = 404;
        }
        if (code == 400 || code == 404 || code == 409 || code == 422)
        {
            return new PaymentException(code, reason, ex);
        }
        if (code == 401 || code == 403)
        {
            return new PaymentException((int)HttpStatusCode.BadGateway, "Stax rejected the configured API credentials: " + reason, ex);
        }
        if (code == 429)
        {
            return new PaymentException(429, "Stax is rate limiting requests: " + reason, ex);
        }
        if (code >= 400 && code < 500)
        {
            return new PaymentException(422, reason, ex);
        }
        return new PaymentException((int)HttpStatusCode.BadGateway, "Stax failed: " + reason, ex);
    }

    /// <summary>Collects every message in a Stax error body ({"error":{"0":"..."}}, {"field":["..."]}, ["..."]).</summary>
    public static string ExtractReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Stax rejected the request.";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new List<string>();
            Collect(doc.RootElement, null, parts);
            parts = parts.Distinct().ToList();
            return parts.Count == 0 ? "Stax rejected the request." : string.Join("; ", parts);
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body.Substring(0, 300) : body;
        }
    }

    private static void Collect(JsonElement e, string? key, List<string> parts)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var text = e.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var plain = string.IsNullOrEmpty(key) || int.TryParse(key, out _) || key == "message" || key == "error";
                    parts.Add(plain ? text! : key + ": " + text);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) Collect(item, key, parts);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Collect(p.Value, p.Name, parts);
                break;
        }
    }

    public static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.ToString()
        };
    }

    public static decimal? Dec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDecimal();
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
        return null;
    }

    public static bool? Bool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => v.GetDecimal() != 0,
            _ => null
        };
    }

    private static List<JsonElement> Items(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array) return e.EnumerateArray().ToList();
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
            return d.EnumerateArray().ToList();
        return new List<JsonElement>();
    }

    private static int LastPage(JsonElement response, int fallback) =>
        response.ValueKind == JsonValueKind.Object && response.TryGetProperty("last_page", out var lp) && lp.ValueKind == JsonValueKind.Number
            ? lp.GetInt32()
            : fallback;

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static Guid ParseId(string id, string what)
    {
        if (!Guid.TryParse(id, out var guid)) throw PaymentException.BadRequest($"'{id}' is not a valid {what} id.");
        return guid;
    }

    // ---------------------------------------------------------------- customers

    public Task<List<JsonElement>> FindCustomersByEmail(string email, CancellationToken ct) => Run(async () =>
    {
        var result = new List<JsonElement>();
        for (var page = 1; page <= 20; page++)
        {
            var response = ToJson(await Merchant.Customer.GetCustomers(
                new GetCustomersRequest { Keywords = email, Page = page, PerPage = 100 }, null, ct));
            result.AddRange(Items(response).Where(c =>
                string.Equals(Str(c, "email"), email, StringComparison.OrdinalIgnoreCase) && Str(c, "deleted_at") == null));
            if (page >= LastPage(response, page)) break;
        }
        return result;
    });

    public Task<JsonElement> CreateCustomer(string email, string firstName, string lastName, string? company, CancellationToken ct) => Run(async () =>
    {
        var body = new Dictionary<string, object?>
        {
            ["firstname"] = firstName,
            ["lastname"] = lastName,
            ["email"] = email,
            ["reference"] = email,
        };
        if (!string.IsNullOrWhiteSpace(company)) body["company"] = company;
        return ToJson(await Merchant.Customer.PostCustomer(new PostCustomerRequest { Body = body }, null, ct));
    });

    public Task ScrubCustomer(string customerId, CancellationToken ct) => Run(() =>
        Merchant.Customer.PutCustomer(new PutCustomerRequest
        {
            Id = ParseId(customerId, "customer"),
            Body = new Dictionary<string, object?>
            {
                ["firstname"] = "Closed",
                ["lastname"] = "Account",
                ["company"] = "",
                ["email"] = "",
                ["phone"] = "",
                ["address_1"] = "",
                ["address_2"] = "",
                ["address_city"] = "",
                ["address_state"] = "",
                ["address_zip"] = "",
                ["reference"] = "",
                ["notes"] = "",
            }
        }, null, ct));

    public Task DeleteCustomer(string customerId, CancellationToken ct) => Run(() =>
        Merchant.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = ParseId(customerId, "customer") }, null, ct));

    // ---------------------------------------------------------------- payment methods

    public Task<JsonElement> CreatePaymentMethod(Dictionary<string, object?> body, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.PaymentMethod.PostPaymentMethod(new PostPaymentMethodRequest { Body = body }, null, ct)));

    public Task<List<JsonElement>> ListActivePaymentMethods(string customerId, CancellationToken ct) => Run(async () =>
    {
        var list = await Merchant.Customer.GetCustomerPaymentMethods(
            new GetCustomerPaymentMethodsRequest { Id = ParseId(customerId, "customer") }, null, ct);
        return list.Select(ToJson).Where(p => Str(p, "deleted_at") == null).ToList();
    });

    /// <summary>Every payment method ever saved for the customer, removed ones included.</summary>
    public Task<List<JsonElement>> ListAllPaymentMethods(string customerId, CancellationToken ct) => Run(async () =>
    {
        var result = new List<JsonElement>();
        for (var page = 1; page <= 100; page++)
        {
            var response = ToJson(await Merchant.PaymentMethod.ListPaymentMethods(new ListPaymentMethodsRequest
            {
                CustomerId = ParseId(customerId, "customer"),
                Status = "all",
                Page = page,
                PerPage = 100,
            }, null, ct));
            result.AddRange(Items(response));
            if (page >= LastPage(response, page)) break;
        }
        return result;
    });

    public Task<JsonElement> GetPaymentMethod(string id, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.PaymentMethod.GetPaymentMethod(new GetPaymentMethodRequest { Id = ParseId(id, "payment method") }, null, ct)));

    public Task DeletePaymentMethod(string id, CancellationToken ct) => Run(() =>
        Merchant.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = ParseId(id, "payment method") }, null, ct));

    // ---------------------------------------------------------------- charging

    /// <summary>The surcharge Stax applies to a card payment of this amount (zero for bank accounts, debit cards, or when the shop does not surcharge).</summary>
    public Task<decimal> GetSurcharge(string paymentMethodId, decimal subtotal, CancellationToken ct) => Run(async () =>
    {
        var review = ToJson(await Merchant.Surcharge.GetSurchargeReview(new GetSurchargeReviewRequest
        {
            PaymentMethodId = ParseId(paymentMethodId, "payment method"),
            Total = Money(subtotal),
        }, null, ct));
        var amount = Dec(review, "surcharge_amount");
        if (amount == null)
        {
            var withSurcharge = Dec(review, "total_with_surcharge_amount");
            amount = withSurcharge == null ? 0m : withSurcharge.Value - subtotal;
        }
        return Math.Max(0m, Math.Round(amount.Value, 2, MidpointRounding.AwayFromZero));
    });

    public Task<JsonElement> Charge(string paymentMethodId, decimal subtotal, string reference, CancellationToken ct) => Run(async () =>
    {
        var body = new Dictionary<string, object?>
        {
            ["payment_method_id"] = paymentMethodId,
            ["total"] = subtotal,
            ["pre_auth"] = false,
            ["meta"] = new Dictionary<string, object?>
            {
                ["subtotal"] = subtotal,
                ["tax"] = 0,
                ["reference"] = reference,
            },
        };
        return ToJson(await Merchant.Charge.PostCharge(new Charges.PostChargeRequest { Body = body }, null, ct));
    });

    public Task<JsonElement> GetTransaction(string id, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.Transaction.GetTransaction(new GetTransactionRequest { Id = ParseId(id, "transaction") }, null, ct)));

    /// <summary>Refunds the whole charge (a refund without an amount refunds the entire charge, surcharge included).</summary>
    public Task<JsonElement> Refund(string transactionId, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.Transaction.PostRefund(new PostRefundRequest { Id = ParseId(transactionId, "transaction"), Body = new PostRefundRequestBodyJson() }, null, ct)));

    // ---------------------------------------------------------------- invoice schedules

    public Task<JsonElement> CreateInvoiceSchedule(Dictionary<string, object?> body, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.InvoiceSchedule.PostInvoiceSchedule(new PostInvoiceScheduleRequest { Body = body }, null, ct)));

    public Task<JsonElement> GetInvoiceSchedule(string id, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.InvoiceSchedule.GetInvoiceSchedule(new GetInvoiceScheduleRequest { Id = ParseId(id, "invoice schedule") }, null, ct)));

    public Task<JsonElement> UpdateInvoiceSchedule(string id, Dictionary<string, object?> body, CancellationToken ct) => Run(async () =>
        ToJson(await Merchant.InvoiceSchedule.PutInvoiceSchedule(new PutInvoiceScheduleRequest { Id = ParseId(id, "invoice schedule"), Body = body }, null, ct)));

    // ---------------------------------------------------------------- marketplace

    public Task<JsonElement> CreateMerchant(Dictionary<string, object?> body, CancellationToken ct)
    {
        if (!HasPlatformKey)
        {
            throw PaymentException.Unprocessable("Seller onboarding is not available: this shop has no Stax platform key (Stax:PlatformApiKey).");
        }
        return Run(async () =>
            ToJson(await _platform.Value.MerchantAdmin.AdminPostMerchant(new AdminPostMerchantRequest { Body = body }, null, ct)));
    }
}
