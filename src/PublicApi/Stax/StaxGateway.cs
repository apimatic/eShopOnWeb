using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using StaxFattMerchantApi;
using StaxFattMerchantApi.Core;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;
using StaxFattMerchantApi.Core.Hooks;
using StaxFattMerchantApi.Models;
using StaxFattMerchantApi.Requests.Charge;
using StaxFattMerchantApi.Requests.Customer;
using StaxFattMerchantApi.Requests.InvoiceSchedule;
using StaxFattMerchantApi.Requests.PaymentMethod;
using StaxFattMerchantApi.Requests.Team;
using StaxFattMerchantApi.Requests.Transaction;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>The merchant's surcharge (card fee pass-through) settings, as configured at Stax.</summary>
public record SurchargeSettings(bool Enabled, decimal? CreditRate, string? Policy);

/// <summary>
/// Every call this app makes to Stax goes through here, and every one of them goes through the Stax SDK.
/// </summary>
public class StaxGateway
{
    private readonly StaxClients _clients;

    public StaxGateway(StaxClients clients)
    {
        _clients = clients;
    }

    public bool HasPlatformKey => _clients.HasPlatformKey;
    private StaxFattMerchantApiClient Shop => _clients.Shop;

    // ---------------------------------------------------------------- plumbing

    private static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ApiException<RawError> ex)
        {
            throw new StaxApiException((int)ex.StatusCode, StaxJson.DescribeError(ex.Error.ReadAsString()), ex);
        }
        catch (ApiException ex)
        {
            throw new StaxApiException((int)ex.StatusCode, ex.Message, ex);
        }
        catch (Exception ex) when (ex is SdkException or HttpRequestException or TaskCanceledException)
        {
            throw new StaxApiException(0, "Stax could not be reached.", ex);
        }
    }

    private static async Task Run(Func<Task> call)
    {
        await Run(async () =>
        {
            await call();
            return true;
        });
    }

    /// <summary>
    /// Some SDK operations are declared without a response model (they return a bare Task) even though Stax answers
    /// with the record. A per-call hook reads the body of the successful response so it is not lost.
    /// </summary>
    private static async Task<JsonElement> Capture(Func<RequestOptions, Task> call)
    {
        string? body = null;
        var hook = SdkHook.OnResponse(async (response, _, _) =>
        {
            if (!response.IsSuccessStatusCode) return;
            await response.Content.LoadIntoBufferAsync();
            body = await response.Content.ReadAsStringAsync();
        });
        await Run(() => call(new RequestOptions { Hooks = new[] { hook } }));
        if (string.IsNullOrWhiteSpace(body)) return default;
        using var doc = JsonDocument.Parse(body!);
        return doc.RootElement.Clone();
    }

    private static Guid AsGuid(string id, string what)
    {
        return Guid.TryParse(id, out var g) ? g : throw PaymentRequestException.BadRequest($"'{id}' is not a valid {what} id.");
    }

    // ---------------------------------------------------------------- customers

    public async Task<JsonElement> CreateCustomerAsync(Dictionary<string, object?> body, CancellationToken ct = default) =>
        StaxJson.ToElement(await Run(() => Shop.Customer.PostCustomer(new PostCustomerRequest { Body = body }, null, ct)));

    public async Task<JsonElement> FindOrCreateCustomerAsync(Dictionary<string, object?> body, CancellationToken ct = default) =>
        StaxJson.ToElement(await Run(() => Shop.Customer.PostFindOrCreateCustomer(new PostFindOrCreateCustomerRequest { Body = body }, null, ct)));

    public Task UpdateCustomerAsync(string id, Dictionary<string, object?> body, CancellationToken ct = default) =>
        Run(() => Shop.Customer.PutCustomer(new PutCustomerRequest { Id = AsGuid(id, "customer"), Body = body }, null, ct));

    public Task DeleteCustomerAsync(string id, CancellationToken ct = default) =>
        Run(() => Shop.Customer.DeleteCustomer(new DeleteCustomerRequest { Id = AsGuid(id, "customer") }, null, ct));

    // ---------------------------------------------------------------- payment methods

    public async Task<JsonElement> CreatePaymentMethodAsync(Dictionary<string, object?> body, CancellationToken ct = default) =>
        StaxJson.ToElement(await Run(() => Shop.PaymentMethod.PostPaymentMethod(new PostPaymentMethodRequest { Body = body }, null, ct)));

    /// <summary>Gets one payment method, removed ones included. Null when Stax does not know it.</summary>
    public async Task<JsonElement?> GetPaymentMethodAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return StaxJson.ToElement(await Run(() => Shop.PaymentMethod.GetPaymentMethod(new GetPaymentMethodRequest { Id = AsGuid(id, "payment method") }, null, ct)));
        }
        catch (StaxApiException ex) when (ex.StaxStatus == 404)
        {
            return null;
        }
    }

    /// <summary>The customer's payment methods Stax still considers saved (removed ones are not listed by Stax).</summary>
    public async Task<IReadOnlyList<JsonElement>> ListCustomerPaymentMethodsAsync(string customerId, CancellationToken ct = default)
    {
        var list = await Run(() => Shop.Customer.GetCustomerPaymentMethods(new GetCustomerPaymentMethodsRequest { Id = AsGuid(customerId, "customer") }, null, ct));
        var result = new List<JsonElement>();
        foreach (var item in list) result.Add(StaxJson.ToElement(item));
        return result;
    }

    public async Task UpdatePaymentMethodAsync(string id, Dictionary<string, object?> body, CancellationToken ct = default) =>
        await Run(() => Shop.PaymentMethod.PutPaymentMethod(new PutPaymentMethodRequest { Id = AsGuid(id, "payment method"), Body = body }, null, ct));

    /// <summary>
    /// Stax keeps a removed payment method (and cannot edit it afterwards), so the holder's personal details are
    /// blanked first. What stays is the brand and the last four digits.
    /// </summary>
    public Task ScrubPaymentMethodAsync(string id, CancellationToken ct = default) =>
        UpdatePaymentMethodAsync(id, new Dictionary<string, object?>
        {
            ["person_name"] = "Closed Account", // Stax insists on a name
            ["address_1"] = "",
            ["address_2"] = "",
            ["address_city"] = "",
            ["address_state"] = "",
            ["address_zip"] = "",
            ["address_country"] = ""
        }, ct);

    public Task DeletePaymentMethodAsync(string id, CancellationToken ct = default) =>
        Run(() => Shop.PaymentMethod.DeletePaymentMethod(new DeletePaymentMethodRequest { Id = AsGuid(id, "payment method") }, null, ct));

    // ---------------------------------------------------------------- fees

    public async Task<SurchargeSettings> GetSurchargeSettingsAsync(CancellationToken ct = default)
    {
        var team = StaxJson.ToElement(await Run(() => Shop.Team.GetTeam(null, ct)));
        var enabled = team.Bool("is_surcharge_enabled");
        var rate = team.Dec("cnp_credit_surcharge_rate") ?? team.Prop("registration")?.Dec("cnp_credit_surcharge_rate");
        var policy = team.Prop("options")?.Str("surcharge_policy");
        return new SurchargeSettings(enabled, rate, policy);
    }

    // ---------------------------------------------------------------- transactions

    public async Task<JsonElement> ChargeAsync(Dictionary<string, object?> body, CancellationToken ct = default) =>
        StaxJson.ToElement(await Run(() => Shop.Charge.PostCharge(new PostChargeRequest { Body = body }, null, ct)));

    public async Task<JsonElement> GetTransactionAsync(string id, CancellationToken ct = default) =>
        StaxJson.ToElement(await Run(() => Shop.Transaction.GetTransaction(new GetTransactionRequest { Id = AsGuid(id, "transaction") }, null, ct)));

    /// <summary>Voids the transaction when it has not settled, refunds it when it has.</summary>
    public Task VoidOrRefundAsync(string transactionId, decimal total, CancellationToken ct = default) =>
        Run(() => Shop.Transaction.PostVoidOrRefund(new PostVoidOrRefundRequest
        {
            Id = AsGuid(transactionId, "transaction"),
            Body = new PostVoidOrRefundRequestBodyJson { Total = (double)total }
        }, null, ct));

    // ---------------------------------------------------------------- invoice schedules

    public Task<JsonElement> CreateInvoiceScheduleAsync(Dictionary<string, object?> body, CancellationToken ct = default) =>
        Capture(o => Shop.InvoiceSchedule.PostInvoiceSchedule(new PostInvoiceScheduleRequest { Body = body }, o, ct));

    public Task<JsonElement> UpdateInvoiceScheduleAsync(string id, Dictionary<string, object?> body, CancellationToken ct = default) =>
        Capture(o => Shop.InvoiceSchedule.PutInvoiceSchedule(new PutInvoiceScheduleRequest { Id = AsGuid(id, "invoice schedule"), Body = body }, o, ct));

    public Task<JsonElement> GetInvoiceScheduleAsync(string id, CancellationToken ct = default) =>
        Capture(o => Shop.InvoiceSchedule.GetInvoiceSchedule(new GetInvoiceScheduleRequest { Id = AsGuid(id, "invoice schedule") }, o, ct));

    public Task DeleteInvoiceScheduleAsync(string id, CancellationToken ct = default) =>
        Run(() => Shop.InvoiceSchedule.DeleteInvoiceSchedule(new DeleteInvoiceScheduleRequest { Id = AsGuid(id, "invoice schedule") }, null, ct));

    // ---------------------------------------------------------------- sellers

    /// <summary>Creates a new merchant (a marketplace seller). Needs the platform key.</summary>
    public async Task<JsonElement> CreateMerchantAsync(Dictionary<string, object?> body, CancellationToken ct = default)
    {
        var platform = _clients.Platform ??
            throw PaymentRequestException.Unprocessable(
                "Sellers cannot be onboarded: this shop has no Stax platform key (Stax:PlatformApiKey) configured.");
        return StaxJson.ToElement(await Run(() => platform.Team.PostMerchant(new PostMerchantRequest { Body = body }, null, ct)));
    }
}
