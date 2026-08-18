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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal implementation of <see cref="IPayPalGateway"/> over the APIMatic-generated PayPal .NET SDK. Every
/// provider/transport/parse failure is translated into a <see cref="PaymentGatewayException"/> with a
/// caller-safe message so the rest of the app sees a single failure type. Card details are never logged.
/// </summary>
public class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken ct)
    {
        var cardRequest = new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToSdkAddress(card.BillingAddress)
        };
        return AuthorizeAsync(amount, currency, cardRequest, idempotencyKey, ct);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken ct)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return AuthorizeAsync(amount, currency, cardRequest, idempotencyKey, ct);
    }

    private async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currency, CardRequest card,
        string idempotencyKey, CancellationToken ct)
    {
        // For a raw/vaulted card with intent=AUTHORIZE, supplying the card on CreateOrder places the
        // authorization during the create call itself — a single server-side call, no buyer approval. The
        // authorization is read back from the Order response; AuthorizeOrder is NOT called (doing so would
        // return ORDER_ALREADY_AUTHORIZED). `prefer: "return=representation"` populates the payments block.
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
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToGatewayException(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "place the payment hold", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "place the payment hold");
        }

        var payPalOrderId = created.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id.", null, null);

        var authorization = FirstAuthorization(created)
            ?? throw new PaymentGatewayException("PayPal did not return an authorization for the order.", null, null);

        var authorizationId = authorization.Id
            ?? throw new PaymentGatewayException("PayPal did not return an authorization id.", null, null);

        return new AuthorizationResult(
            payPalOrderId,
            authorizationId,
            authorization.Status?.Value.ToString() ?? string.Empty,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var authorization = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            return new AuthorizationState(authorization.Status?.Value.ToString() ?? string.Empty, ParseDate(authorization.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw ToGatewayException(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "read the authorization", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "read the authorization");
        }
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct)
    {
        try
        {
            var reauthorized = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                ct: ct);

            var id = reauthorized.Id
                ?? throw new PaymentGatewayException("PayPal did not return a reauthorization id.", null, null);

            return new ReauthorizationResult(id, reauthorized.Status?.Value.ToString() ?? string.Empty, ParseDate(reauthorized.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            var issue = ex.Error.TryGetError(out var e) ? FirstIssue(e) : null;
            var status = ex.Error.TryGetRawError(out var r) ? (int)r.StatusCode : (int?)null;
            var reason = issue is null ? string.Empty : $" ({issue})";
            throw new PaymentReauthorizationException(
                $"The payment hold has expired and can no longer be renewed{reason}. Ask the shopper to pay again.",
                status, ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "renew the authorization");
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
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
            // The captured amount is authoritative for what was taken (and therefore what is refundable). The
            // seller-receivable breakdown carries the fee and net PayPal reported; use its gross only as a
            // fallback when the capture amount is absent.
            var gross = ParseMoney(captured.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? 0m;
            var fee = ParseMoney(breakdown?.PaypalFee);
            var net = ParseMoney(breakdown?.NetAmount);
            var currency = captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty;

            var captureId = captured.Id
                ?? throw new PaymentGatewayException("PayPal did not return a capture id.", null, null);

            return new CaptureResult(captureId, captured.Status?.Value.ToString() ?? string.Empty, gross, fee, net, currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw ToGatewayException(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "capture the payment", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "capture the payment");
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct)
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
            throw ToGatewayException(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "release the payment hold", ex);
        }
        catch (JsonException)
        {
            // A successful void returns 204 No Content; the SDK models a response body and throws while
            // parsing the empty response. A genuine void failure surfaces as SdkException<VoidPaymentError>,
            // so an empty-body parse error here means the hold was released — treat it as success.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw ToInfrastructureException(ex, "release the payment hold");
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken ct)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
            : null;

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                // Namespace the caller's key by capture so it is globally unique as a PayPal-Request-Id while
                // staying stable per (capture, key) — repeating the same key does not refund twice.
                payPalRequestId: $"refund-{captureId}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var refundId = refund.Id
                ?? throw new PaymentGatewayException("PayPal did not return a refund id.", null, null);
            var refundedAmount = ParseMoney(refund.Amount) ?? amount ?? 0m;
            var refundedCurrency = refund.Amount?.CurrencyCode ?? currency;

            return new RefundResult(refundId, refund.Status?.Value.ToString() ?? string.Empty, refundedAmount, refundedCurrency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw ToGatewayException(ex.Error.TryGetError(out var e) ? e : null,
                ex.Error.TryGetRawError(out var r) ? r : null, "refund the payment", ex);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "refund the payment");
        }
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerReference, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: ct);

            var vaultId = token.Id
                ?? throw new PaymentGatewayException("PayPal did not return a vault id for the saved card.", null, null);

            var savedCard = token.PaymentSource?.Card;
            var brand = savedCard?.Brand?.Value.ToString() ?? "Card";
            var lastFour = savedCard?.LastDigits ?? LastFour(card.Number);
            var expiry = savedCard?.Expiry ?? card.Expiry;

            return new VaultedCardResult(vaultId, brand, lastFour, expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw ToGatewayException(null, ex.Error.TryGetRawError(out var r) ? r : null, "save the card", ex,
                ex.Error.TryGetError1(out var e1) ? FirstIssue(e1) : null);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "save the card");
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // Treat an already-absent token as success — deletion is idempotent.
            if (ex.Error.TryGetRawError(out var r) && r.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }
            throw ToGatewayException(null, ex.Error.TryGetRawError(out var raw) ? raw : null, "remove the saved card", ex,
                ex.Error.TryGetError1(out var e1) ? FirstIssue(e1) : null);
        }
        catch (Exception ex) when (IsInfrastructureFailure(ex))
        {
            throw ToInfrastructureException(ex, "remove the saved card");
        }
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        // PayPal's reporting API rejects the round-trip ("O") format's 7-digit fractional seconds and
        // colon offset; it accepts millisecond precision in UTC with a "Z" suffix.
        var startDate = FormatReportingDate(from);
        var endDate = FormatReportingDate(to);

        var results = new List<ReconciliationTransaction>();
        var page = 1;
        var totalPages = 1;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: startDate,
                    endDate: endDate,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                // TransactionSearch is the SDK's only Case-B operation: the error model IS RawError.
                var raw = ex.Error;
                throw new PaymentGatewayException(
                    $"PayPal could not return the reconciliation report (HTTP {(int)raw.StatusCode}).",
                    (int)raw.StatusCode, ex);
            }
            catch (Exception ex) when (IsInfrastructureFailure(ex))
            {
                throw ToInfrastructureException(ex, "return the reconciliation report");
            }

            totalPages = response.TotalPages ?? 1;
            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    results.Add(new ReconciliationTransaction(
                        info.TransactionId,
                        info.TransactionStatus ?? string.Empty,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode,
                        ParseDate(info.TransactionInitiationDate)));
                }
            }

            page++;
        }
        while (page <= totalPages);

        return results;
    }

    // --- helpers ---

    private static AuthorizationWithAdditionalData? FirstAuthorization(Order order)
    {
        if (order.PurchaseUnits is null)
        {
            return null;
        }

        foreach (var purchaseUnit in order.PurchaseUnits)
        {
            var authorizations = purchaseUnit.Payments?.Authorizations;
            if (authorizations is not null && authorizations.Count > 0)
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static PayPalServerSdk.Models.Address ToSdkAddress(BillingAddress address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null)
        {
            return null;
        }
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string LastFour(string number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static string? FirstIssue(Error error) => error.Details?.FirstOrDefault()?.Issue ?? error.Name;

    private static string? FirstIssue(Error1 error) => error.Details?.FirstOrDefault()?.Issue ?? error.Name;

    private PaymentGatewayException ToGatewayException(Error? error, RawError? raw, string action, Exception inner,
        string? issueOverride = null)
    {
        var issue = issueOverride ?? (error is null ? null : FirstIssue(error));
        _logger.LogWarning($"PayPal failed to {action}. Issue: {issue ?? "unknown"}. " +
            $"Status: {(raw is null ? "n/a" : ((int)raw.StatusCode).ToString())}. DebugId: {error?.DebugId ?? "n/a"}");

        var detail = string.IsNullOrWhiteSpace(issue) ? string.Empty : $" ({issue})";
        // A provider rejection is client-actionable → surface as a 4xx; only a raw/untyped status maps directly.
        var status = raw is not null ? (int)raw.StatusCode : 400;
        return new PaymentGatewayException($"PayPal could not {action}{detail}.", status, inner);
    }

    private PaymentGatewayException ToInfrastructureException(Exception ex, string action)
    {
        if (ex is JsonException)
        {
            // A response body that did not match the SDK's model — outcome unknown; never leak JSON detail.
            _logger.LogWarning($"PayPal returned an unreadable response while trying to {action}.");
            return new PaymentGatewayException(
                $"PayPal returned a response that could not be processed while trying to {action}.", 502, ex);
        }

        _logger.LogWarning($"PayPal was unreachable while trying to {action}.");
        return new PaymentGatewayException($"PayPal was unreachable while trying to {action}.", 502, ex);
    }

    private static bool IsInfrastructureFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;
}
