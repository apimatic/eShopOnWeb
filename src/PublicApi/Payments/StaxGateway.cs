using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Team;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The only place that talks to Stax. Every call goes through the Stax SDK.
/// </summary>
public class StaxGateway
{
    private readonly StaxClients _clients;

    public StaxGateway(StaxClients clients)
    {
        _clients = clients;
    }

    private StaxFattMerchantApiClient Merchant =>
        _clients.Merchant ?? throw new ApiProblemException(503, "Payments are not configured: Stax:ApiKey is missing.");

    // ---------- customers ----------

    public async Task<string> CreateCustomerAsync(string firstName, string lastName, string email, string company)
    {
        var body = new Dictionary<string, object?>
        {
            ["firstname"] = firstName,
            ["lastname"] = lastName,
            ["email"] = email,
            ["company"] = company,
            ["allow_invoice_credit_card_payments"] = true,
        };
        var result = await Run(() => Merchant.Customer.PostCustomer(new PostCustomerRequest { Body = body }));
        return AsElement(result).Str("id") ?? throw new ApiProblemException(502, "Stax did not return a customer id.");
    }

    /// <summary>Finds the customer with this e-mail, or creates it; the customer's company is brought in line.</summary>
    public async Task<string> FindOrCreateBusinessCustomerAsync(string email, string company)
    {
        var result = await Run(() => Merchant.Customer.PostFindOrCreateCustomer(new PostFindOrCreateCustomerRequest
        {
            Body = new Dictionary<string, object?>
            {
                ["firstname"] = company,
                ["lastname"] = "Billing",
                ["email"] = email,
                ["company"] = company,
                ["allow_invoice_credit_card_payments"] = true,
            },
        }));
        var customer = AsElement(result);
        var id = customer.Str("id") ?? throw new ApiProblemException(502, "Stax did not return a customer id.");
        if (!string.Equals(customer.Str("company"), company, StringComparison.Ordinal))
        {
            await Run(() => Merchant.Customer.PutCustomer(new PutCustomerRequest
            {
                Id = Guid.Parse(id),
                Body = new Dictionary<string, object?> { ["company"] = company },
            }));
        }
        return id;
    }

    /// <summary>Overwrites the personal data held on the customer record.</summary>
    public Task ScrubCustomerAsync(string customerId) =>
        Run(() => Merchant.Customer.PutCustomer(new PutCustomerRequest
        {
            Id = Guid.Parse(customerId),
            Body = new Dictionary<string, object?>
            {
                ["firstname"] = "Closed",
                ["lastname"] = "Account",
                ["email"] = string.Empty,
                ["company"] = string.Empty,
                ["phone"] = string.Empty,
                ["address_1"] = string.Empty,
                ["address_2"] = string.Empty,
                ["address_city"] = string.Empty,
                ["address_state"] = string.Empty,
                ["address_zip"] = string.Empty,
                ["address_country"] = string.Empty,
                ["cc_emails"] = null,
                ["cc_sms"] = null,
                ["notes"] = null,
                ["reference"] = string.Empty,
            },
        }));

    public Task DeleteCustomerAsync(string customerId) =>
        Run(() => Merchant.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = Guid.Parse(customerId) }));

    // ---------- payment methods ----------

    public async Task<StaxPaymentMethod> CreatePaymentMethodAsync(IDictionary<string, object?> body)
    {
        var result = await Run(() => Merchant.PaymentMethod.PostPaymentMethod(new PostPaymentMethodRequest { Body = body }));
        return StaxPaymentMethod.From(AsElement(result));
    }

    public async Task<StaxPaymentMethod> CreatePaymentMethodFromTokenAsync(string token, string customerId)
    {
        var raw = await Capture(() => Merchant.PaymentMethod.PostPaymentMethodToken(new PostPaymentMethodTokenRequest
        {
            Body = new PostPaymentMethodTokenRequestBodyJson { Token = token, CustomerId = Guid.Parse(customerId) },
        }));
        return StaxPaymentMethod.From(ParseCaptured(raw));
    }

    public async Task<IReadOnlyList<StaxPaymentMethod>> ListCustomerPaymentMethodsAsync(string customerId)
    {
        var result = await Run(() => Merchant.Customer.GetCustomerPaymentMethods(
            new GetCustomerPaymentMethodsRequest { Id = Guid.Parse(customerId) }));
        return result.Select(o => StaxPaymentMethod.From(AsElement(o))).ToList();
    }

    /// <summary>Gets a payment method, including one that has been removed. Null if Stax does not know it.</summary>
    public async Task<StaxPaymentMethod?> GetPaymentMethodAsync(string id)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            return null;
        }
        try
        {
            var result = await Run(() => Merchant.PaymentMethod.GetPaymentMethod(new GetPaymentMethodRequest { Id = guid }));
            return StaxPaymentMethod.From(AsElement(result));
        }
        catch (ApiProblemException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    /// <summary>Overwrites the personal data held on a (live) payment method.</summary>
    public Task ScrubPaymentMethodAsync(string id) =>
        Run(() => Merchant.PaymentMethod.PutPaymentMethod(new PutPaymentMethodRequest
        {
            Id = Guid.Parse(id),
            Body = new Dictionary<string, object?>
            {
                ["person_name"] = "Closed Account",
                ["address_1"] = string.Empty,
                ["address_2"] = string.Empty,
                ["address_city"] = string.Empty,
                ["address_state"] = string.Empty,
                ["address_zip"] = string.Empty,
                ["address_country"] = string.Empty,
            },
        }));

    public Task DeletePaymentMethodAsync(string id) =>
        Run(() => Merchant.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = Guid.Parse(id) }));

    // ---------- fees ----------

    public async Task<StaxMerchantFees> GetMerchantFeesAsync()
    {
        var self = AsElement(await Run(() => Merchant.Self.GetSelf()));
        if (!self.TryGetProperty("merchant", out var merchant) || merchant.ValueKind != JsonValueKind.Object)
        {
            return new StaxMerchantFees(false, null, null);
        }
        decimal? rate = null;
        var rawRate = merchant.Str("cnp_credit_surcharge_rate");
        if (rawRate is not null && decimal.TryParse(rawRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
        {
            rate = r;
        }
        string? policy = null;
        if (merchant.TryGetProperty("options", out var options))
        {
            policy = options.Str("surcharge_policy");
        }
        return new StaxMerchantFees(merchant.Bool("is_surcharge_enabled"), rate, policy);
    }

    // ---------- transactions ----------

    public async Task<StaxTransaction> ChargeAsync(string paymentMethodId, decimal total, decimal subtotal, decimal fee, string reference)
    {
        var meta = new Dictionary<string, object?>
        {
            ["subtotal"] = subtotal,
            ["reference"] = reference,
        };
        if (fee > 0)
        {
            meta["surchargeAmount"] = fee;
        }
        var body = new Dictionary<string, object?>
        {
            ["type"] = "charge",
            ["source"] = "api",
            ["payment_method_id"] = paymentMethodId,
            ["total"] = total,
            ["pre_auth"] = false,
            ["meta"] = meta,
        };
        return await RunTransaction(() => Merchant.Transaction.PostTransaction(new PostTransactionRequest { Body = body }));
    }

    public async Task<StaxTransaction> GetTransactionAsync(string id)
    {
        var result = await Run(() => Merchant.Transaction.GetTransaction(new GetTransactionRequest { Id = Guid.Parse(id) }));
        return StaxTransaction.From(AsElement(result));
    }

    /// <summary>Refunds (Stax voids it first when it still can) the given amount of a transaction.</summary>
    public async Task<StaxTransaction> RefundAsync(string transactionId, decimal total)
    {
        return await RunTransaction(() => Merchant.Transaction.PostRefund(new PostRefundRequest
        {
            Id = Guid.Parse(transactionId),
            Body = new PostRefundRequestBodyJson { Total = (double)total },
        }));
    }

    // ---------- invoice schedules ----------

    public async Task<JsonElement> CreateInvoiceScheduleAsync(IDictionary<string, object?> body)
    {
        var raw = await Capture(() => Merchant.InvoiceSchedule.PostInvoiceSchedule(new PostInvoiceScheduleRequest { Body = body }));
        return ParseCaptured(raw);
    }

    public async Task<JsonElement> GetInvoiceScheduleAsync(string id)
    {
        if (!Guid.TryParse(id, out var guid))
        {
            throw new ApiProblemException(404, "Invoice schedule not found.");
        }
        var raw = await Capture(() => Merchant.InvoiceSchedule.GetInvoiceSchedule(new GetInvoiceScheduleRequest { Id = guid }));
        return ParseCaptured(raw);
    }

    public async Task<JsonElement> SetInvoiceScheduleActiveAsync(string id, bool active)
    {
        var raw = await Capture(() => Merchant.InvoiceSchedule.PutInvoiceSchedule(new PutInvoiceScheduleRequest
        {
            Id = Guid.Parse(id),
            Body = new Dictionary<string, object?> { ["active"] = active },
        }));
        return ParseCaptured(raw);
    }

    // ---------- sellers ----------

    public bool HasPlatformKey => _clients.Platform is not null;

    public async Task<JsonElement> CreateSellerMerchantAsync(IDictionary<string, object?> body)
    {
        var platform = _clients.Platform
            ?? throw new ApiProblemException(422,
                "Seller onboarding is not available: the shop has no Stax platform key (Stax:PlatformApiKey).");
        var result = await Run(() => platform.Team.PostMerchant(new PostMerchantRequest { Body = body }));
        return AsElement(result);
    }

    // ---------- plumbing ----------

    private async Task<string?> Capture(Func<Task> call)
    {
        try
        {
            return await _clients.Capture.CaptureAsync(call);
        }
        catch (Exception ex) when (ex is not ApiProblemException)
        {
            throw Translate(ex);
        }
    }

    private static JsonElement ParseCaptured(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ApiProblemException(502, "Stax returned an empty response.");
        }
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static JsonElement AsElement(object? o) => o switch
    {
        JsonElement e => e,
        null => throw new ApiProblemException(502, "Stax returned an empty response."),
        _ => JsonSerializer.SerializeToElement(o),
    };

    /// <summary>
    /// Stax answers a transaction it could not complete (declined, gateway trouble) with an error status whose body is
    /// still the transaction. That is an outcome to report, not a failure of the request.
    /// </summary>
    private static async Task<StaxTransaction> RunTransaction(Func<Task<object>> call)
    {
        try
        {
            return StaxTransaction.From(AsElement(await call()));
        }
        catch (ApiException<RawError> ex) when (TryReadTransaction(ex.Error, out var tx))
        {
            return tx;
        }
        catch (Exception ex) when (ex is not ApiProblemException)
        {
            throw Translate(ex);
        }
    }

    private static bool TryReadTransaction(RawError error, out StaxTransaction tx)
    {
        tx = null!;
        try
        {
            using var doc = JsonDocument.Parse(error.ReadAsBytes());
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out _)
                && root.TryGetProperty("type", out _) && root.TryGetProperty("total", out _))
            {
                tx = StaxTransaction.From(root.Clone());
                return true;
            }
        }
        catch (JsonException)
        {
        }
        return false;
    }

    private static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (Exception ex) when (ex is not ApiProblemException)
        {
            throw Translate(ex);
        }
    }

    private static async Task Run(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception ex) when (ex is not ApiProblemException)
        {
            throw Translate(ex);
        }
    }

    /// <summary>Turns a Stax SDK failure into an API problem with a readable reason.</summary>
    public static ApiProblemException Translate(Exception ex)
    {
        if (ex is ApiException api)
        {
            var reason = ex is ApiException<RawError> raw ? Reason(raw.Error) : api.Message;
            var status = (int)api.StatusCode;
            if (status == 404)
            {
                return new ApiProblemException(404, $"Not found at Stax: {reason}");
            }
            if (status >= 400 && status < 500 && status != 401 && status != 403 && status != 429)
            {
                return new ApiProblemException(422, $"Stax rejected the request: {reason}");
            }
            return new ApiProblemException(502, $"Stax could not process the request ({status}): {reason}");
        }
        return new ApiProblemException(502, "Stax could not be reached.");
    }

    private static string Reason(RawError error)
    {
        var text = error.ReadAsString();
        try
        {
            using var doc = JsonDocument.Parse(text);
            var parts = new List<string>();
            Collect(doc.RootElement, null, parts);
            if (parts.Count > 0)
            {
                return Cap(string.Join("; ", parts));
            }
        }
        catch (JsonException)
        {
        }
        return string.IsNullOrWhiteSpace(text) ? "no details" : text.Length > 300 ? text[..300] : text;
    }

    private static string Cap(string s) => s.Length > 400 ? s[..400] + "..." : s;

    private static string Unused()
    {
        return string.Empty;
    }

    private static void Collect(JsonElement e, string? name, List<string> parts)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                parts.Add(name is null || name is "message" or "error" ? e.GetString()! : $"{name}: {e.GetString()}");
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray()) Collect(i, name, parts);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Collect(p.Value, p.Name, parts);
                break;
        }
    }
}
