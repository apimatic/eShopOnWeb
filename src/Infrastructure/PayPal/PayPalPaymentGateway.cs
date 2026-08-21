using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>. All PayPal SDK types stay behind this
/// class; callers see only domain models. Every failure is translated to a
/// <see cref="PaymentGatewayException"/> carrying a caller-safe message and a deliberate HTTP status.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    // ---------------------------------------------------------------- create + authorize

    public async Task<GatewayAuthorization> CreateAndAuthorizeAsync(CreateAuthorizationRequest request, CancellationToken ct = default)
    {
        var card = request.VaultId != null
            ? new CardRequest { VaultId = request.VaultId }
            : new CardRequest
            {
                Name = request.Card!.Name,
                Number = request.Card.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                BillingAddress = ToSdkAddress(request.Card.BillingAddress)
            };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = MoneyFormatter.Format(request.Amount, request.CurrencyCode)
                    },
                    CustomId = request.OrderReference,
                    InvoiceId = request.InvoiceId
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.CreateRequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) { throw new PaymentGatewayException(SafeMessage("create the order", error?.Message, FirstIssue(error)), 422, issue: FirstIssue(error)); }
            if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "create the order"); }
            throw new PaymentGatewayException("PayPal could not create the order.", 502);
        }
        catch (JsonException ex) { throw BadResponse(ex, "create the order"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        if (order.Status == OrderStatus.PayerActionRequired)
        {
            return new GatewayAuthorization(order.Id ?? string.Empty, string.Empty, "PAYER_ACTION_REQUIRED", null, true);
        }

        // With a direct card, PayPal authorizes inline during CreateOrder, so the authorization is
        // already present. Only when it isn't (e.g. a wallet-approved order) do we authorize explicitly.
        var authorization = ExtractAuthorization(order.PurchaseUnits);

        if (authorization?.Id == null)
        {
            OrderAuthorizeResponse authorized;
            try
            {
                authorized = await _client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: request.AuthorizeRequestId,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                if (ex.Error.TryGetError(out var error)) { throw new PaymentGatewayException(SafeMessage("authorize the order", error?.Message, FirstIssue(error)), 422, issue: FirstIssue(error)); }
                if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "authorize the order"); }
                throw new PaymentGatewayException("PayPal could not authorize the order.", 502);
            }
            catch (JsonException ex) { throw BadResponse(ex, "authorize the order"); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

            if (authorized.Status == OrderStatus.PayerActionRequired)
            {
                return new GatewayAuthorization(order.Id ?? string.Empty, string.Empty, "PAYER_ACTION_REQUIRED", null, true);
            }

            authorization = ExtractAuthorization(authorized.PurchaseUnits);
        }

        if (authorization?.Id == null)
        {
            throw new PaymentGatewayException("PayPal accepted the order but returned no authorization to act on.", 502);
        }

        return new GatewayAuthorization(
            order.Id ?? string.Empty,
            authorization.Id,
            authorization.Status?.Value ?? "CREATED",
            ParseExpiry(authorization.ExpirationTime),
            RequiresBuyerAction: false);
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
              .FirstOrDefault();

    // ---------------------------------------------------------------- reauthorize

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currencyCode, Value = MoneyFormatter.Format(amount, currencyCode) }
        };

        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new GatewayAuthorization(
                string.Empty,
                reauth.Id ?? authorizationId,
                reauth.Status?.Value ?? "CREATED",
                ParseExpiry(reauth.ExpirationTime),
                RequiresBuyerAction: false);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // Any failure to reauthorize means the hold can no longer be renewed — say so in operator terms.
            var detail = ex.Error.TryGetRawError(out var raw) && raw != null ? $" (HTTP {(int)raw.StatusCode})" : string.Empty;
            throw new PaymentGatewayException(
                $"The authorization for this order can no longer be renewed{detail}. A new payment must be collected from the shopper before the order can be fulfilled.",
                422, issue: GatewayIssues.AuthorizationNotRenewable);
        }
        catch (JsonException ex) { throw BadResponse(ex, "renew the authorization"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    // ---------------------------------------------------------------- capture

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            var breakdown = captured.SellerReceivableBreakdown;
            var gross = MoneyFormatter.Parse(breakdown?.GrossAmount?.Value) ?? MoneyFormatter.Parse(captured.Amount?.Value) ?? 0m;
            var fee = MoneyFormatter.Parse(breakdown?.PaypalFee?.Value);
            var net = MoneyFormatter.Parse(breakdown?.NetAmount?.Value);
            var currency = breakdown?.GrossAmount?.CurrencyCode ?? captured.Amount?.CurrencyCode ?? string.Empty;

            return new GatewayCapture(
                captured.Id ?? string.Empty,
                captured.Status?.Value ?? "COMPLETED",
                gross, fee, net, currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                var issue = NormalizeIssue(FirstIssue(error));
                throw new PaymentGatewayException(SafeMessage("capture the payment", error?.Message, issue), 422, issue: issue);
            }
            if (ex.Error.TryGetNoContent(out var noContent)) { throw FromRaw(noContent!, "capture the payment"); }
            if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "capture the payment"); }
            throw new PaymentGatewayException("PayPal rejected the capture.", 502);
        }
        catch (JsonException ex) { throw BadResponse(ex, "capture the payment"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    // ---------------------------------------------------------------- void

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) { throw new PaymentGatewayException(SafeMessage("release the held funds", error?.Message, FirstIssue(error)), 422, issue: FirstIssue(error)); }
            if (ex.Error.TryGetNoContent(out var noContent)) { throw FromRaw(noContent!, "release the held funds"); }
            if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "release the held funds"); }
            throw new PaymentGatewayException("PayPal rejected the void.", 502);
        }
        catch (JsonException ex) { throw BadResponse(ex, "release the held funds"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    // ---------------------------------------------------------------- refund

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = MoneyFormatter.Format(amount.Value, currencyCode) } }
            : null;

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: RefundRequestId(captureId, idempotencyKey),
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var refunded = MoneyFormatter.Parse(refund.Amount?.Value) ?? amount ?? 0m;
            var totalRefunded = MoneyFormatter.Parse(refund.SellerPayableBreakdown?.TotalRefundedAmount?.Value);
            return new GatewayRefund(refund.Id ?? string.Empty, refund.Status?.Value ?? "COMPLETED", refunded, totalRefunded);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) { throw new PaymentGatewayException(SafeMessage("refund the payment", error?.Message, FirstIssue(error)), 422, issue: FirstIssue(error)); }
            if (ex.Error.TryGetNoContent(out var noContent)) { throw FromRaw(noContent!, "refund the payment"); }
            if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "refund the payment"); }
            throw new PaymentGatewayException("PayPal rejected the refund.", 502);
        }
        catch (JsonException ex) { throw BadResponse(ex, "refund the payment"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    // ---------------------------------------------------------------- vault card

    public async Task<GatewayVaultedCard> VaultCardAsync(CardDetails card, string customerId, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(payPalRequestId: null, body: body, ct: ct);
            var entity = token.PaymentSource?.Card;
            return new GatewayVaultedCard(
                token.Id ?? throw new PaymentGatewayException("PayPal vaulted the card but returned no token id.", 502),
                entity?.Brand?.Value ?? "UNKNOWN",
                entity?.LastDigits ?? string.Empty,
                entity?.Expiry ?? card.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) { throw new PaymentGatewayException(SafeMessage("save the card", error?.Message, FirstIssue(error)), 422, issue: FirstIssue(error)); }
            if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "save the card"); }
            throw new PaymentGatewayException("PayPal rejected the card.", 422);
        }
        catch (JsonException ex) { throw BadResponse(ex, "save the card"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) { throw new PaymentGatewayException(SafeMessage("remove the saved card", error?.Message, FirstIssue(error)), 422, issue: FirstIssue(error)); }
            if (ex.Error.TryGetRawError(out var raw)) { throw FromRaw(raw!, "remove the saved card"); }
            throw new PaymentGatewayException("PayPal rejected the request to remove the saved card.", 502);
        }
        catch (JsonException ex) { throw BadResponse(ex, "remove the saved card"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    // ---------------------------------------------------------------- reconciliation

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var start = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var end = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        var results = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;

        try
        {
            do
            {
                var response = await _client.TransactionSearch.SearchTransactions(
                    startDate: start,
                    endDate: end,
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
                    pageSize: 100,
                    page: page,
                    ct: ct);

                totalPages = response.TotalPages ?? 1;

                foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info == null) continue;

                    results.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.InvoiceId,
                        info.CustomField,
                        MoneyFormatter.Parse(info.TransactionAmount?.Value),
                        MoneyFormatter.Parse(info.FeeAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        info.TransactionInitiationDate?.ToString(),
                        info.TransactionUpdatedDate?.ToString()));
                }

                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            throw FromRaw(ex.Error, "search transactions"); // TransactionSearch is the SDK's only Case-B op
        }
        catch (JsonException ex) { throw BadResponse(ex, "search transactions"); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        return results;
    }

    // ---------------------------------------------------------------- helpers

    private static Address? ToSdkAddress(GatewayBillingAddress? address)
    {
        if (address == null) return null;
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode ?? "US"
        };
    }

    // A PayPal-Request-Id unique per (capture, caller key): the same caller key never false-collides
    // across different captures, while a retry of the same refund keeps the same id (idempotent).
    private static string RefundRequestId(string captureId, string idempotencyKey)
    {
        var raw = System.Text.Encoding.UTF8.GetBytes(captureId + "|" + idempotencyKey);
        return "refund-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw))[..40].ToLowerInvariant();
    }

    private static DateTimeOffset? ParseExpiry(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

    private static string? FirstIssue(Error? error) => error?.Details?.FirstOrDefault()?.Issue;

    private static string? FirstIssue(Error1? error) => error?.Details?.FirstOrDefault()?.Issue;

    /// <summary>Normalizes an expired-authorization issue to a provider-agnostic code the service can branch on.</summary>
    private static string? NormalizeIssue(string? issue)
    {
        if (issue == null) return null;
        return issue.Contains("EXPIR", StringComparison.OrdinalIgnoreCase)
            ? GatewayIssues.AuthorizationExpired
            : issue;
    }

    private static string SafeMessage(string action, string? providerMessage, string? issue)
    {
        var reason = !string.IsNullOrWhiteSpace(issue) ? $" ({issue})"
            : !string.IsNullOrWhiteSpace(providerMessage) ? $" ({providerMessage})"
            : string.Empty;
        return $"PayPal could not {action}{reason}.";
    }

    private static PaymentGatewayException FromRaw(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        var mapped = status >= 500 ? 502 : status;
        return new PaymentGatewayException($"PayPal could not {action} (HTTP {status}).", mapped);
    }

    private static PaymentGatewayException BadResponse(JsonException ex, string action) =>
        new($"PayPal returned a response that could not be processed while trying to {action}.", 502, ex);

    private static PaymentGatewayException Unreachable(Exception ex) =>
        new("The payment provider is currently unreachable. Please try again.", 503, ex);
}
