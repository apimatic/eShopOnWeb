using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkExceptions = PayPalServerSdk.Core.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/> built on the AsadAli.Checkout.Sdk
/// (PayPalServerSdk). All money movement — authorize, capture, reauthorize, void, refund — plus card
/// vaulting and transaction-search reconciliation flow through here. Card details are used only to
/// build the request and are never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    private const string PreferRepresentation = "return=representation";

    public PayPalPaymentGateway(
        PayPalServerSdkClient client,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    private static string Format(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
        => money is null || string.IsNullOrWhiteSpace(money.Value)
            ? (decimal?)null
            : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- Authorize

    public Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest
        {
            Number = card.Number,
            Expiry = card.ExpiryYearMonth,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = MapAddress(card.BillingAddress)
        };
        return CreateAndAuthorizeAsync(amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return CreateAndAuthorizeAsync(amount, currency, cardRequest, idempotencyKey, cancellationToken);
    }

    private async Task<AuthorizationResult> CreateAndAuthorizeAsync(
        decimal amount, string currency, CardRequest cardRequest, string idempotencyKey, CancellationToken cancellationToken)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = Format(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = cardRequest }
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                null, idempotencyKey, null, null, null, orderRequest, PreferRepresentation);
        }
        catch (SdkExceptions.SdkException<CreateOrderError> ex)
        {
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            _logger.LogWarning("PayPal CreateOrder failed: {Failure}", failure);
            throw new PaymentGatewayException($"Card authorization was declined or failed: {failure.Message}", failure.DebugId, ex);
        }

        GuardNoPayerAction(order.Status, order.Links);

        // If the card auth completed inline, read it back; otherwise fall back to an explicit authorize.
        var authorization = ExtractAuthorization(order.PurchaseUnits);
        if (authorization is null)
        {
            OrderAuthorizeResponse authResponse;
            try
            {
                authResponse = await _client.Orders.AuthorizeOrder(
                    order.Id, null, idempotencyKey + ":authorize", null, null, null, PreferRepresentation);
            }
            catch (SdkExceptions.SdkException<AuthorizeOrderError> ex)
            {
                var typed = ex.Error.TryGetError(out var e) ? e : null;
                var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
                _logger.LogWarning("PayPal AuthorizeOrder failed: {Failure}", failure);
                throw new PaymentGatewayException($"Authorization failed: {failure.Message}", failure.DebugId, ex);
            }

            GuardNoPayerAction(authResponse.Status, null);
            authorization = ExtractAuthorization(authResponse.PurchaseUnits);
        }

        if (authorization is null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for order {order.Id} (status {order.Status}).");
        }

        var (authId, authStatus) = authorization.Value;
        if (IsDeclined(authStatus))
        {
            throw new PaymentGatewayException(
                $"Card authorization was declined (authorization {authId} status {authStatus}).");
        }

        return new AuthorizationResult(order.Id ?? string.Empty, authId, authStatus);
    }

    // ---------------------------------------------------------------- Capture

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        CapturedPayment captured;
        try
        {
            // Passing no body captures the full authorized amount; final_capture defaults appropriately.
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId, null, idempotencyKey, null, null, PreferRepresentation);
        }
        catch (SdkExceptions.SdkException<CaptureAuthorizedPaymentError> ex)
        {
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            if (failure.IsAuthorizationExpired)
            {
                _logger.LogInformation("Authorization {AuthId} is stale and must be reauthorized before capture.", authorizationId);
                throw new AuthorizationExpiredException(
                    $"Authorization {authorizationId} has expired and must be renewed before capture.", ex);
            }
            _logger.LogWarning("PayPal capture failed: {Failure}", failure);
            throw new PaymentGatewayException($"Capture failed: {failure.Message}", failure.DebugId, ex);
        }

        if (IsCaptureFailed(captured.Status))
        {
            throw new PaymentGatewayException(
                $"Capture {captured.Id} did not complete (status {captured.Status}).");
        }

        var breakdown = captured.SellerReceivableBreakdown;
        var capturedAmount = ParseMoney(captured.Amount) ?? amount;

        // PayPal reports the fee and net in the transaction (order) currency. When the merchant's
        // account settles in a different currency, net_amount stays in the transaction currency and
        // the converted figures live in receivable_amount/exchange_rate. Prefer the net that matches
        // the captured currency so "net proceeds" reads in the same currency as the charge.
        var capturedCurrency = captured.Amount?.CurrencyCode ?? currency;
        var fee = ParseMoney(breakdown?.PaypalFee);
        var net = ChooseNet(breakdown, capturedCurrency) ?? (capturedAmount - (fee ?? 0m));

        _logger.LogInformation(
            "Capture {CaptureId}: gross={Gross} fee={Fee} net={Net} receivable={Receivable} feeInReceivable={FeeRcv} rate={Rate}",
            captured.Id,
            Describe(breakdown?.GrossAmount), Describe(breakdown?.PaypalFee), Describe(breakdown?.NetAmount),
            Describe(breakdown?.ReceivableAmount), Describe(breakdown?.PaypalFeeInReceivableCurrency),
            breakdown?.ExchangeRate is { } xr ? $"{xr.Value} {xr.SourceCurrency}->{xr.TargetCurrency}" : "n/a");

        return new CaptureResult(
            captured.Id ?? string.Empty,
            captured.Status?.Value ?? "UNKNOWN",
            capturedAmount,
            fee,
            net,
            capturedCurrency);
    }

    // ---------------------------------------------------------------- Reauthorize

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId,
                null,
                null,
                new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } },
                PreferRepresentation);

            return new AuthorizationResult(
                string.Empty,
                reauth.Id ?? authorizationId,
                reauth.Status?.Value ?? "UNKNOWN");
        }
        catch (SdkExceptions.SdkException<ReauthorizePaymentError> ex)
        {
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            _logger.LogWarning("PayPal reauthorize failed: {Failure}", failure);
            // A 4xx business rejection means the hold can no longer be renewed — surface it so an
            // operator knows the shopper must re-pay before the order can be fulfilled.
            throw new ReauthorizationNotPossibleException(
                $"The authorization for this order has expired and can no longer be renewed ({failure.Message}). " +
                "The shopper must re-pay the order before it can be fulfilled.", ex);
        }
    }

    // ---------------------------------------------------------------- Void

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Payments.VoidPayment(authorizationId, null, null, null, PreferRepresentation);
        }
        catch (SdkExceptions.SdkException<VoidPaymentError> ex)
        {
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            _logger.LogWarning("PayPal void failed: {Failure}", failure);
            throw new PaymentGatewayException($"Releasing the held funds failed: {failure.Message}", failure.DebugId, ex);
        }
    }

    // ---------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId,
                null,
                idempotencyKey,
                null,
                new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = Format(amount) } },
                PreferRepresentation);

            var refundedAmount = ParseMoney(refund.Amount) ?? amount;
            return new RefundResult(
                refund.Id ?? string.Empty,
                refund.Status?.Value ?? "UNKNOWN",
                refundedAmount,
                refund.Amount?.CurrencyCode ?? currency);
        }
        catch (SdkExceptions.SdkException<RefundCapturedPaymentError> ex)
        {
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            _logger.LogWarning("PayPal refund failed: {Failure}", failure);
            throw new PaymentGatewayException($"Refund failed: {failure.Message}", failure.DebugId, ex);
        }
    }

    // ---------------------------------------------------------------- Vault

    public async Task<VaultResult> VaultCardAsync(CardDetails card, string customerId, CancellationToken cancellationToken = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.ExpiryYearMonth,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(null, request);
            var savedCard = token.PaymentSource?.Card;
            return new VaultResult(
                token.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id."),
                savedCard?.Brand?.Value,
                savedCard?.LastDigits,
                savedCard?.Expiry);
        }
        catch (SdkExceptions.SdkException<CreatePaymentTokenError> ex)
        {
            var typed = ex.Error.TryGetError1(out var e) ? ToError(e) : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            _logger.LogWarning("PayPal vault-card failed: {Failure}", failure);
            throw new PaymentGatewayException($"Saving the card failed: {failure.Message}", failure.DebugId, ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(vaultId);
        }
        catch (SdkExceptions.SdkException<DeletePaymentTokenError> ex)
        {
            var typed = ex.Error.TryGetError1(out var e) ? ToError(e) : null;
            var failure = PayPalErrorTranslator.Translate(ex.Error, typed, ex.Message);
            _logger.LogWarning("PayPal delete-vaulted-card failed: {Failure}", failure);
            throw new PaymentGatewayException($"Removing the saved card failed: {failure.Message}", failure.DebugId, ex);
        }
    }

    // ---------------------------------------------------------------- Reconciliation

    public async Task<IReadOnlyList<ReconciliationTransaction>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var results = new List<ReconciliationTransaction>();
        const int pageSize = 100;
        int page = 1;
        int totalPages;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate, endDate,
                    null, null, null, null, null, null, null, null,
                    "transaction_info", "Y", pageSize, page);
            }
            catch (SdkExceptions.SdkException<PayPalServerSdk.Core.ErrorResponse.RawError> ex)
            {
                var status = (int)ex.Error.StatusCode;
                var body = SafeReadBody(ex.Error);
                _logger.LogWarning("PayPal transaction search failed (page {Page}): HTTP {Status} {Body}", page, status, body);
                throw new PaymentGatewayException($"Transaction search failed: {body ?? ex.Message}", inner: ex);
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    results.Add(new ReconciliationTransaction(
                        info.TransactionId ?? string.Empty,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseDate(info.TransactionInitiationDate),
                        info.TransactionEventCode));
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // ---------------------------------------------------------------- Helpers

    private static string? SafeReadBody(PayPalServerSdk.Core.ErrorResponse.RawError raw)
    {
        try { return raw.ReadAsString(); } catch { return null; }
    }

    private static string Describe(Money? money)
        => money is null ? "n/a" : $"{money.Value} {money.CurrencyCode}";

    /// <summary>
    /// The net proceeds in the captured currency. PayPal's net_amount is normally the net in the
    /// transaction currency; if a currency mismatch shows up, fall back to gross - fee so the value
    /// is always expressed in the currency the shopper was charged.
    /// </summary>
    private static decimal? ChooseNet(SellerReceivableBreakdown? breakdown, string capturedCurrency)
    {
        if (breakdown is null) return null;

        var netMoney = breakdown.NetAmount;
        if (netMoney is not null &&
            string.Equals(netMoney.CurrencyCode, capturedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return ParseMoney(netMoney);
        }

        var gross = ParseMoney(breakdown.GrossAmount);
        var fee = ParseMoney(breakdown.PaypalFee);
        if (gross is not null && fee is not null)
        {
            return gross - fee;
        }

        return ParseMoney(netMoney);
    }

    private static Address? MapAddress(BillingAddress? address)
    {
        if (address is null) return null;
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    /// <summary>Pull the first authorization (id + status) out of a purchase-unit collection.</summary>
    private static (string Id, string Status)? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        var auth = purchaseUnits?
            .Select(pu => pu.Payments)
            .Where(p => p?.Authorizations is not null)
            .SelectMany(p => p!.Authorizations!)
            .FirstOrDefault();

        if (auth is null || string.IsNullOrEmpty(auth.Id)) return null;
        return (auth.Id!, auth.Status?.Value ?? "UNKNOWN");
    }

    private void GuardNoPayerAction(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status is not null && status == OrderStatus.PayerActionRequired)
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this card payment in a browser (e.g. 3-D Secure). " +
                "This browser-less integration cannot complete such a payment.");
        }
    }

    private static bool IsDeclined(string status) =>
        status.Equals("DENIED", StringComparison.OrdinalIgnoreCase);

    private static bool IsCaptureFailed(CaptureStatus? status) =>
        status is not null && (status == CaptureStatus.Declined || status == CaptureStatus.Failed);

    private static DateTimeOffset? ParseDate(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? (DateTimeOffset?)null
            : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    // The Vault error variant carries an Error1; project the fields we use onto the common Error shape.
    private static Error? ToError(Error1? e1)
        => e1 is null ? null : new Error
        {
            Name = e1.Name,
            Message = e1.Message,
            DebugId = e1.DebugId,
            Details = e1.Details?.Select(d => new ErrorDetails { Issue = d.Issue, Description = d.Description }).ToList()
        };
}
