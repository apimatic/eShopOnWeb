using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private const string Representation = "return=representation";
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client) => _client = client;

    public async Task<ProviderOrder> CreateOrderAsync(string amount, string currency, string invoiceId,
        string customId, string requestId, CancellationToken cancellationToken)
    {
        var response = await _client.Orders.CreateOrder(
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalPartnerAttributionId: null,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new[]
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = amount },
                        InvoiceId = invoiceId,
                        CustomId = customId
                    }
                }
            },
            prefer: Representation,
            ct: cancellationToken);

        return new ProviderOrder(Required(response.Id, "PayPal order id"), response.Status?.Value);
    }

    public async Task<ProviderAuthorization> AuthorizeAsync(string payPalOrderId, CardInput? card,
        string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        var cardRequest = card == null
            ? new CardRequest { VaultId = Required(vaultId, "vault id") }
            : new CardRequest
            {
                Name = card.Name,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = Address(card.BillingAddress)
            };

        var response = await _client.Orders.AuthorizeOrder(
            id: payPalOrderId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
            },
            prefer: Representation,
            ct: cancellationToken);

        var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()
            ?? throw InvalidResponse("authorization");
        return Authorization(authorization, response.Status?.Value);
    }

    public async Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        var response = await _client.Payments.GetAuthorizedPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalAuthAssertion: null,
            ct: cancellationToken);
        return Authorization(response, null);
    }

    public async Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, string amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var response = await _client.Payments.ReauthorizePayment(
            authorizationId: authorizationId,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = amount } },
            prefer: Representation,
            ct: cancellationToken);
        return Authorization(response, null);
    }

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, string amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        var response = await _client.Payments.CaptureAuthorizedPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: new CaptureRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = amount },
                FinalCapture = true
            },
            prefer: Representation,
            ct: cancellationToken);
        return Capture(response);
    }

    public async Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        var response = await _client.Payments.GetCapturedPayment(
            captureId: captureId,
            payPalMockResponse: null,
            ct: cancellationToken);
        return Capture(response);
    }

    public async Task<ProviderAuthorization> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        var response = await _client.Payments.VoidPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalAuthAssertion: null,
            payPalRequestId: requestId,
            prefer: Representation,
            ct: cancellationToken);
        return Authorization(response, null);
    }

    public async Task<ProviderRefund> RefundAsync(string captureId, string? amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        var body = new RefundRequest
        {
            Amount = amount == null ? null : new Money { CurrencyCode = currency, Value = amount }
        };
        var response = await _client.Payments.RefundCapturedPayment(
            captureId: captureId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: body,
            prefer: Representation,
            ct: cancellationToken);
        return Refund(response);
    }

    public async Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        var response = await _client.Payments.GetRefund(
            refundId: refundId,
            payPalMockResponse: null,
            payPalAuthAssertion: null,
            ct: cancellationToken);
        return Refund(response);
    }

    public async Task<ProviderPaymentMethod> SaveCardAsync(string merchantCustomerId,
        string? providerCustomerId, CardInput card, string requestId, CancellationToken cancellationToken)
    {
        var response = await _client.Vault.CreatePaymentToken(
            payPalRequestId: requestId,
            body: new PaymentTokenRequest
            {
                Customer = new Customer
                {
                    Id = providerCustomerId,
                    MerchantCustomerId = merchantCustomerId
                },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = Address(card.BillingAddress)
                    }
                }
            },
            ct: cancellationToken);
        return PaymentMethod(response);
    }

    public async Task<IReadOnlyList<ProviderPaymentMethod>> ListCardsAsync(string customerId,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var page = 1;
        var totalPages = 1;
        var result = new List<ProviderPaymentMethod>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (page <= totalPages && page <= 1000)
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: customerId,
                pageSize: pageSize,
                page: page,
                totalRequired: true,
                ct: cancellationToken);
            var tokens = response.PaymentTokens ?? Array.Empty<PaymentTokenResponse>();
            foreach (var token in tokens)
            {
                var mapped = PaymentMethod(token);
                if (seen.Add(mapped.Id)) result.Add(mapped);
            }

            totalPages = Math.Max(page,
                checked((int)(response.TotalPages ?? (tokens.Count < pageSize ? page : page + 1))));
            if (tokens.Count == 0) break;
            page++;
        }

        return result;
    }

    public async Task<ProviderPaymentMethod> GetCardAsync(string tokenId, CancellationToken cancellationToken)
    {
        var response = await _client.Vault.GetPaymentToken(id: tokenId, ct: cancellationToken);
        return PaymentMethod(response);
    }

    public Task DeleteCardAsync(string tokenId, CancellationToken cancellationToken) =>
        _client.Vault.DeletePaymentToken(id: tokenId, ct: cancellationToken);

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var result = new List<ProviderTransaction>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chunkStart = from.ToUniversalTime();
        var finalEnd = to.ToUniversalTime();
        do
        {
            var chunkEnd = chunkStart.AddDays(31) < finalEnd ? chunkStart.AddDays(31) : finalEnd;
            var page = 1;
            var totalPages = 1;
            while (page <= totalPages && page <= 10000)
            {
                var response = await _client.TransactionSearch.SearchTransactions(
                    startDate: SearchDate(chunkStart),
                    endDate: SearchDate(chunkEnd),
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: pageSize,
                    page: page,
                    ct: cancellationToken);
                var details = response.TransactionDetails ?? Array.Empty<TransactionDetails>();
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info == null) continue;
                    var key = info.TransactionId ?? $"{SearchDate(chunkStart)}:{page}:{result.Count}";
                    if (!seen.Add(key)) continue;
                    result.Add(new ProviderTransaction(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        info.TransactionEventCode,
                        info.TransactionStatus,
                        info.TransactionInitiationDate,
                        info.TransactionUpdatedDate,
                        ParseNullable(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseNullable(info.FeeAmount?.Value),
                        info.InvoiceId,
                        info.CustomField));
                }

                totalPages = Math.Max(page,
                    checked((int)(response.TotalPages ?? (details.Count < pageSize ? page : page + 1))));
                if (details.Count == 0) break;
                page++;
            }
            if (chunkEnd >= finalEnd) break;
            chunkStart = chunkEnd;
        }
        while (chunkStart <= finalEnd);

        return result;
    }

    private static string SearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static ProviderAuthorization Authorization(AuthorizationWithAdditionalData value, string? orderStatus) =>
        new(Required(value.Id, "authorization id"), Required(value.Status?.Value, "authorization status"),
            Parse(value.Amount?.Value), Required(value.Amount?.CurrencyCode, "authorization currency"),
            orderStatus, value.CreateTime, value.UpdateTime, value.ExpirationTime);

    private static ProviderAuthorization Authorization(PaymentAuthorization value, string? orderStatus) =>
        new(Required(value.Id, "authorization id"), Required(value.Status?.Value, "authorization status"),
            Parse(value.Amount?.Value), Required(value.Amount?.CurrencyCode, "authorization currency"),
            orderStatus, value.CreateTime, value.UpdateTime, value.ExpirationTime);

    private static ProviderCapture Capture(CapturedPayment value)
    {
        var breakdown = value.SellerReceivableBreakdown ?? throw InvalidResponse("seller receivable breakdown");
        return new ProviderCapture(
            Required(value.Id, "capture id"), Required(value.Status?.Value, "capture status"),
            Parse(value.Amount?.Value), Required(value.Amount?.CurrencyCode, "capture currency"),
            Parse(breakdown.GrossAmount.Value), ParseNullable(breakdown.PaypalFee?.Value),
            ParseNullable(breakdown.NetAmount?.Value), value.CreateTime, value.UpdateTime);
    }

    private static ProviderRefund Refund(Refund value) =>
        new(Required(value.Id, "refund id"), Required(value.Status?.Value, "refund status"),
            Parse(value.Amount?.Value), Required(value.Amount?.CurrencyCode, "refund currency"),
            value.CreateTime, value.UpdateTime);

    private static ProviderPaymentMethod PaymentMethod(PaymentTokenResponse value)
    {
        var card = value.PaymentSource?.Card ?? throw InvalidResponse("vaulted card");
        return new ProviderPaymentMethod(
            Required(value.Id, "payment token id"),
            Required(value.Customer?.Id, "PayPal customer id"),
            card.Brand?.Value,
            card.LastDigits,
            card.Expiry,
            card.Type?.Value);
    }

    private static Address Address(CardAddressInput value) => new()
    {
        AddressLine1 = value.AddressLine1,
        AddressLine2 = value.AddressLine2,
        AdminArea2 = value.City,
        AdminArea1 = value.State,
        PostalCode = value.PostalCode,
        CountryCode = value.CountryCode
    };

    private static decimal Parse(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw InvalidResponse("money amount");

    private static decimal? ParseNullable(string? value) => value == null ? null : Parse(value);

    private static string Required(string? value, string name) =>
        !string.IsNullOrWhiteSpace(value) ? value : throw InvalidResponse(name);

    private static InvalidOperationException InvalidResponse(string field) =>
        new($"PayPal returned an incomplete {field} response.");
}
