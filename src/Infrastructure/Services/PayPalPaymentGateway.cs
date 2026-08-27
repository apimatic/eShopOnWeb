using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// IPaymentGateway implementation over the PayPal Server SDK. Full card details
/// pass through to PayPal and are never persisted or logged here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    public const string HttpClientName = "PayPal";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
        Currency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency;
    }

    public string Currency { get; }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, CardDetails card,
        string idempotencyKey, string invoiceId, CancellationToken ct)
    {
        var paymentSource = new PaymentSource { Card = ToCardRequest(card) };
        return await AuthorizeAsync(amount, paymentSource, idempotencyKey, invoiceId, ct);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string vaultTokenId,
        string idempotencyKey, string invoiceId, CancellationToken ct)
    {
        var paymentSource = VaultedPaymentSource(vaultTokenId);
        return await AuthorizeAsync(amount, paymentSource, idempotencyKey, invoiceId, ct);
    }

    private async Task<AuthorizationResult> AuthorizeAsync(decimal amount, PaymentSource paymentSource,
        string idempotencyKey, string invoiceId, CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = Currency,
                        Value = Format(amount)
                    },
                    InvoiceId = invoiceId,
                    CustomId = invoiceId
                }
            }
        };

        try
        {
            var order = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            var authorized = await Bounded(c => _client.Orders.AuthorizeOrder(
                id: order.Id!,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-authorize",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = paymentSource.Card,
                        Token = paymentSource.Token
                    }
                },
                prefer: "return=representation",
                ct: c), ct);

            var authorization = authorized.PurchaseUnits?
                .SelectMany(u => u.Payments?.Authorizations ?? (IReadOnlyList<AuthorizationWithAdditionalData>)Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault()
                ?? throw new PaymentGatewayException("PayPal did not return an authorization for the order.");

            return new AuthorizationResult(
                order.Id!,
                authorization.Id!,
                authorization.Status?.Value ?? "UNKNOWN",
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrder(ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrder(ex);
        }
    }

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: c), ct);
            return new AuthorizationState(
                authorization.Status?.Value ?? "UNKNOWN",
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw TranslateGetAuthorizedPayment(ex);
        }
    }

    public async Task<AuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = Currency, Value = Format(amount) }
                },
                prefer: "return=representation",
                ct: c), ct);
            return new AuthorizationState(
                authorization.Status?.Value ?? "UNKNOWN",
                ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorize(ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        string invoiceId, CancellationToken ct)
    {
        try
        {
            var capture = await Bounded(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    InvoiceId = invoiceId,
                    FinalCapture = true
                },
                prefer: "return=representation",
                ct: c), ct);

            if (capture.SellerReceivableBreakdown == null)
            {
                // Breakdown is absent while the capture is pending — re-read it.
                capture = await Bounded(c => _client.Payments.GetCapturedPayment(
                    captureId: capture.Id!,
                    payPalMockResponse: null,
                    ct: c), ct);
            }

            var breakdown = capture.SellerReceivableBreakdown;
            return new CaptureResult(
                capture.Id!,
                capture.Status?.Value ?? "UNKNOWN",
                ParseMoney(breakdown?.GrossAmount ?? capture.Amount) ?? 0m,
                breakdown?.PaypalFee == null ? null : ParseMoney(breakdown.PaypalFee),
                breakdown?.NetAmount == null ? null : ParseMoney(breakdown.NetAmount));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCapture(ex);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw TranslateGetCapturedPayment(ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: c), ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateVoid(ex);
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var refund = await Bounded(c => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: amount == null
                    ? new RefundRequest()
                    : new RefundRequest
                    {
                        Amount = new Money { CurrencyCode = Currency, Value = Format(amount.Value) }
                    },
                prefer: "return=representation",
                ct: c), ct);

            return new RefundResult(
                refund.Id!,
                refund.Status?.Value ?? "UNKNOWN",
                ParseMoney(refund.Amount) ?? amount ?? 0m);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefund(ex);
        }
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string merchantCustomerId,
        string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name
                        }
                    },
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId }
                },
                ct: c), ct);

            var vaultedCard = response.PaymentSource?.Card;
            return new VaultedCardResult(
                response.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id."),
                response.Customer?.Id,
                vaultedCard?.Brand?.Value,
                vaultedCard?.LastDigits,
                vaultedCard?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateCreatePaymentToken(ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await Bounded(c => _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: c), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                throw TranslateVaultError(error);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                // Delete has no typed 404 accessor: an already-gone token is success here.
                if ((int)raw.StatusCode == 404)
                {
                    return;
                }
                throw TranslateRaw(raw);
            }
            throw new PaymentGatewayException("PayPal rejected the delete request.", null, ex);
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var transactions = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;

        do
        {
            SearchResponse response;
            try
            {
                response = await Bounded(c => _client.TransactionSearch.SearchTransactions(
                    startDate: from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    endDate: to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: null,
                    pageSize: 100,
                    page: page,
                    ct: c), ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw TranslateRaw(ex.Error);
            }

            totalPages = response.TotalPages ?? page;
            foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info == null)
                {
                    continue;
                }
                transactions.Add(new GatewayTransaction(
                    info.TransactionId,
                    info.PaypalReferenceId,
                    info.PaypalReferenceIdType?.Value,
                    info.InvoiceId,
                    info.CustomField,
                    info.TransactionAmount == null ? null : ParseMoney(info.TransactionAmount),
                    info.TransactionAmount?.CurrencyCode,
                    info.FeeAmount == null ? null : ParseMoney(info.FeeAmount),
                    info.TransactionStatus,
                    info.TransactionEventCode,
                    ParseDate(info.TransactionInitiationDate)));
            }
            page++;
        }
        while (page <= totalPages);

        return transactions;
    }

    // The source-endorsed shape for charging a vaulted card: card.vault_id plus
    // stored-credential attributes (customer-initiated one-time payment).
    private static PaymentSource VaultedPaymentSource(string vaultTokenId) =>
        new PaymentSource
        {
            Card = new CardRequest
            {
                VaultId = vaultTokenId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime
                }
            }
        };

    private static CardRequest ToCardRequest(CardDetails card) =>
        new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToBillingAddress(card)
        };

    private static PayPalServerSdk.Models.Address? ToBillingAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.CountryCode))
        {
            return null;
        }
        return new PayPalServerSdk.Models.Address
        {
            AddressLine1 = card.AddressLine1,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or the call timed out.", null, ex);
        }
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        try
        {
            await call(cts.Token);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal is unreachable or the call timed out.", null, ex);
        }
    }

    private static string Format(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value == null ? null : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private static string Describe(Error error) =>
        error.Details == null || error.Details.Count == 0
            ? error.Message
            : string.Join("; ", error.Details.Select(d =>
                $"{d.Issue} ({d.Field}={d.Value}): {d.Description}"));

    private static string Describe(Error1 error) =>
        error.Details == null || error.Details.Count == 0
            ? error.Message
            : string.Join("; ", error.Details.Select(d =>
                $"{d.Issue} ({d.Field}={d.Value}): {d.Description}"));

    private static PaymentGatewayException TranslateRaw(RawError raw) =>
        new PaymentGatewayException($"PayPal request failed with status {(int)raw.StatusCode}.", (int)raw.StatusCode);

    private static PaymentGatewayException TranslateVaultError(Error1 error) =>
        new PaymentGatewayException($"PayPal rejected the request ({error.Name}): {Describe(error)}", 422);

    private PaymentGatewayException TranslateCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal rejected the order ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal rejected the order.", null, ex);
    }

    private PaymentGatewayException TranslateAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not authorize the payment ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not authorize the payment.", null, ex);
    }

    private PaymentGatewayException TranslateGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not read the authorization ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetNoContent(out var noContent)) return TranslateRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not read the authorization.", null, ex);
    }

    private PaymentGatewayException TranslateReauthorize(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not renew the authorization ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetNoContent(out var noContent)) return TranslateRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not renew the authorization.", null, ex);
    }

    private PaymentGatewayException TranslateCapture(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not capture the payment ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetNoContent(out var noContent)) return TranslateRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not capture the payment.", null, ex);
    }

    private PaymentGatewayException TranslateGetCapturedPayment(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not read the capture ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetNoContent(out var noContent)) return TranslateRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not read the capture.", null, ex);
    }

    private PaymentGatewayException TranslateVoid(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not void the authorization ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetNoContent(out var noContent)) return TranslateRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not void the authorization.", null, ex);
    }

    private PaymentGatewayException TranslateRefund(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return new PaymentGatewayException($"PayPal could not refund the payment ({error.Name}): {Describe(error)}", 422);
        if (ex.Error.TryGetNoContent(out var noContent)) return TranslateRaw(noContent);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not refund the payment.", null, ex);
    }

    private PaymentGatewayException TranslateCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error)) return TranslateVaultError(error);
        if (ex.Error.TryGetRawError(out var raw)) return TranslateRaw(raw);
        return new PaymentGatewayException("PayPal could not save the card.", null, ex);
    }
}
