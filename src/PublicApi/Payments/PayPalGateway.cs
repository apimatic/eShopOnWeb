using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;

    public PayPalGateway(PayPalServerSdkClient client, Microsoft.Extensions.Options.IOptions<PayPalOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string?> CreateOrderAsync(int orderId, decimal amount, CardRequest card, CancellationToken ct)
    {
        var response = await _client.Orders.CreateOrder(
            payPalMockResponse: null, payPalRequestId: $"eshop-order-{orderId}", payPalPartnerAttributionId: null,
            payPalClientMetadataId: null, payPalAuthAssertion: null,
            body: new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new() { ReferenceId = orderId.ToString(CultureInfo.InvariantCulture), CustomId = orderId.ToString(CultureInfo.InvariantCulture),
                        Amount = new AmountWithBreakdown { CurrencyCode = _options.Currency, Value = amount.ToString("0.00", CultureInfo.InvariantCulture) } }
                },
                PaymentSource = new PaymentSource { Card = card }
            }, ct: ct);
        return response.Id;
    }

    public async Task<(string? AuthorizationId, string? ProviderStatus)> AuthorizeAsync(string providerOrderId, CardRequest? card, CancellationToken ct)
    {
        try
        {
            var response = await _client.Orders.AuthorizeOrder(providerOrderId, null, $"eshop-auth-{providerOrderId}", null, null,
                new OrderAuthorizeRequest { PaymentSource = card is null ? null : new OrderAuthorizeRequestPaymentSource { Card = card } }, ct: ct);
            var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            return (authorization?.Id, response.Status?.Value);
        }
        catch (SdkException<AuthorizeOrderError> ex) when (ex.Error.TryGetError(out var error))
        {
            throw new InvalidOperationException("PayPal rejected the card authorization: " + JsonSerializer.Serialize(error));
        }
    }

    public async Task<(string? CaptureId, decimal Amount, decimal Fee, decimal Net)> CaptureAsync(string authorizationId, decimal expectedAmount, CancellationToken ct)
    {
        var response = await _client.Payments.CaptureAuthorizedPayment(authorizationId, null, $"eshop-capture-{authorizationId}", null, null, null, ct: ct);
        var amount = ReadMoney(response.Amount) ?? expectedAmount;
        var fee = ReadMoney(response.SellerReceivableBreakdown?.PaypalFee) ?? 0m;
        var net = ReadMoney(response.SellerReceivableBreakdown?.NetAmount) ?? amount - fee;
        return (response.Id, amount, fee, net);
    }

    public async Task<string?> RenewIfExpiredAsync(string providerOrderId, string authorizationId, CancellationToken ct)
    {
        var current = await _client.Payments.GetAuthorizedPayment(authorizationId, null, null, ct: ct);
        if (!DateTimeOffset.TryParse(current.ExpirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiry) || expiry > DateTimeOffset.UtcNow)
            return authorizationId;
        var renewed = await _client.Orders.AuthorizeOrder(providerOrderId, null, $"eshop-renew-{authorizationId}", null, null, null, ct: ct);
        return renewed.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()?.Id;
    }

    public async Task<IReadOnlyList<JsonElement>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = new List<JsonElement>();
        var page = 1;
        while (true)
        {
            var response = await _client.TransactionSearch.SearchTransactions(
                from.ToUniversalTime().ToString("O"), to.ToUniversalTime().ToString("O"), null, null, null, null, null, null, null, null,
                pageSize: 100, page: page, ct: ct);
            var json = JsonSerializer.Serialize(response);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("transaction_details", out var details)) all.AddRange(details.EnumerateArray().Select(x => x.Clone()));
            if ((response.TotalPages ?? page) <= page) break;
            page++;
        }
        return all;
    }

    public Task VoidAsync(string authorizationId, CancellationToken ct) =>
        _client.Payments.VoidPayment(authorizationId, null, null, $"eshop-void-{authorizationId}", ct: ct);

    public async Task<(string? RefundId, decimal Amount, string? Status)> RefundAsync(string captureId, decimal? amount, string key, CancellationToken ct)
    {
        var response = await _client.Payments.RefundCapturedPayment(captureId, null, key, null, amount is null ? null : new RefundRequest
        {
            Amount = new Money { CurrencyCode = _options.Currency, Value = amount.Value.ToString("0.00", CultureInfo.InvariantCulture) }
        }, ct: ct);
        return (response.Id, ReadMoney(response.Amount) ?? amount ?? 0m, response.Status?.Value);
    }

    public async Task<string?> SaveCardAsync(string name, string number, string expiry, string cvc, CancellationToken ct)
    {
        var response = await _client.Vault.CreatePaymentToken(null, new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard { Name = name, Number = number, Expiry = expiry, SecurityCode = cvc }
            }
        }, ct: ct);
        return response.Id;
    }

    public Task DeleteCardAsync(string tokenId, CancellationToken ct) => _client.Vault.DeletePaymentToken(tokenId, ct: ct);

    private static decimal? ReadMoney(Money? money) => money is null || !decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? null : value;
}
