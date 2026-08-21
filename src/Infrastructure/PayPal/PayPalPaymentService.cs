using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single place the PayPal SDK is used. Every call is grounded on the contract sheet; every failure is
/// translated to <see cref="PayPalPaymentException"/> (or a more specific subtype) so callers see one failure
/// type and no raw SDK/framework text leaks out.
/// </summary>
public class PayPalPaymentService : IPayPalPaymentService
{
    // Link rels that are ordinary navigation, not a required buyer action.
    private static readonly HashSet<string> OrdinaryRels = new(StringComparer.OrdinalIgnoreCase)
    {
        "self", "capture", "authorize", "void", "refund", "up", "approve", "update"
    };

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentService(PayPalServerSdkClient client)
    {
        _client = client;
    }

    // ----------------------------------------------------------------- Authorize
    public async Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode,
        string invoiceReference, string customId, PaymentSourceInput source, string idempotencyKey,
        CancellationToken ct = default)
    {
        var value = MoneyFormatter.Format(amount, currencyCode);

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currencyCode, Value = value },
                    InvoiceId = invoiceReference,
                    CustomId = customId
                }
            }
        };

        Order createdOrder;
        try
        {
            createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateApiError(ex, "create order",
                ex.Error.TryGetError(out var e) ? e : null,
                !ex.Error.TryGetError(out _) && ex.Error.TryGetRawError(out var r) ? r : null);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "create order");
        }

        if (string.IsNullOrEmpty(createdOrder.Id))
        {
            throw new PayPalPaymentException("PayPal did not return an order id.", 502);
        }

        var card = BuildCardRequest(source);
        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
        };

        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: createdOrder.Id!,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateApiError(ex, "authorize order",
                ex.Error.TryGetError(out var e) ? e : null,
                !ex.Error.TryGetError(out _) && ex.Error.TryGetRawError(out var r) ? r : null);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "authorize order");
        }

        // 3DS / challenge STOP detection — report, do not round-trip a browser.
        var (actionRel, actionHref) = FindActionLink(authResponse.Links);
        if (authResponse.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayPalBuyerActionRequiredException(
                "This card requires the shopper to approve the payment in a browser (3-D Secure), which this " +
                "integration does not support. The payment was not authorized.",
                actionRel, actionHref);
        }

        var authorization = FirstAuthorization(authResponse);
        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PayPalPaymentException(
                "PayPal did not return an authorization for the order; the card may have been declined.", 402);
        }

        return new AuthorizationResult(
            createdOrder.Id!,
            authorization.Id!,
            authorization.Status?.Value ?? "CREATED",
            ParseDate(authorization.ExpirationTime),
            RequiresBuyerAction: false,
            actionRel,
            actionHref);
    }

    // ----------------------------------------------------------------- Capture (with reauth on stale)
    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode,
        DateTimeOffset? authorizationExpiresAt, string idempotencyKey, CancellationToken ct = default)
    {
        var effectiveAuthId = authorizationId;
        DateTimeOffset? expiresAt = authorizationExpiresAt;

        // Proactively renew an authorization we already know is past its honor period.
        if (authorizationExpiresAt.HasValue && authorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            var renewed = await ReauthorizeAsync(effectiveAuthId, amount, currencyCode, ct);
            effectiveAuthId = renewed.authorizationId;
            expiresAt = renewed.expiresAt;
        }

        try
        {
            var captured = await CaptureInternalAsync(effectiveAuthId, idempotencyKey, ct);
            return BuildCaptureResult(effectiveAuthId, captured, currencyCode, expiresAt);
        }
        catch (PayPalPaymentException ex) when (IsStaleAuthorization(ex))
        {
            // Authorization went stale before fulfilment — renew rather than fail outright, then capture again.
            var renewed = await ReauthorizeAsync(effectiveAuthId, amount, currencyCode, ct);
            var captured = await CaptureInternalAsync(renewed.authorizationId, idempotencyKey, ct);
            return BuildCaptureResult(renewed.authorizationId, captured, currencyCode, renewed.expiresAt);
        }
    }

    private async Task<CapturedPayment> CaptureInternalAsync(string authorizationId, string idempotencyKey,
        CancellationToken ct)
    {
        try
        {
            return await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateApiError(ex, "capture authorization",
                ex.Error.TryGetError(out var e) ? e : null,
                !ex.Error.TryGetError(out _) && ex.Error.TryGetRawError(out var r) ? r : null);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "capture authorization");
        }
    }

    private CaptureResult BuildCaptureResult(string authorizationId, CapturedPayment captured, string currencyCode,
        DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrEmpty(captured.Id))
        {
            throw new PayPalPaymentException("PayPal did not return a capture id.", 502);
        }

        var pending = captured.Status == CaptureStatus.Pending;

        decimal? gross = null, fee = null, net = null;
        var breakdown = captured.SellerReceivableBreakdown;
        if (breakdown is not null)
        {
            gross = MoneyFormatter.ParseOrNull(breakdown.GrossAmount?.Value);
            fee = MoneyFormatter.ParseOrNull(breakdown.PaypalFee?.Value);
            net = MoneyFormatter.ParseOrNull(breakdown.NetAmount?.Value);
        }

        var currencyOut = captured.Amount?.CurrencyCode ?? currencyCode;
        return new CaptureResult(authorizationId, captured.Id!, captured.Status?.Value ?? "COMPLETED",
            pending, gross, fee, net, currencyOut, expiresAt);
    }

    // ----------------------------------------------------------------- Reauthorize
    private async Task<(string authorizationId, DateTimeOffset? expiresAt)> ReauthorizeAsync(string authorizationId,
        decimal amount, string currencyCode, CancellationToken ct)
    {
        var reauthorizeRequest = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currencyCode, Value = MoneyFormatter.Format(amount, currencyCode) }
        };

        try
        {
            var result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{authorizationId}",
                payPalAuthAssertion: null,
                body: reauthorizeRequest,
                prefer: "return=minimal",
                ct: ct);

            if (string.IsNullOrEmpty(result.Id))
            {
                throw new AuthorizationNotRenewableException(
                    "The authorization could not be renewed and the order cannot be fulfilled. Ask the shopper to pay again.");
            }

            return (result.Id!, ParseDate(result.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            var error = ex.Error.TryGetError(out var e) ? e : null;
            var raw = error is null && ex.Error.TryGetRawError(out var r) ? r : null;
            var (message, _, issues) = DescribeError(error, raw, "reauthorize");
            throw new AuthorizationNotRenewableException(
                "The held funds could not be renewed, so the order cannot be fulfilled. " + message +
                " Ask the shopper to pay again.", raw is not null ? (int)raw.StatusCode : null, ex, issues);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "reauthorize");
        }
    }

    // ----------------------------------------------------------------- Void
    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            // Ask for a representation so PayPal returns a 200 body (the voided authorization) rather than an
            // empty 204 — an empty body would surface to the SDK deserializer as a JsonException.
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateApiError(ex, "void authorization",
                ex.Error.TryGetError(out var e) ? e : null,
                !ex.Error.TryGetError(out _) && ex.Error.TryGetRawError(out var r) ? r : null);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "void authorization");
        }
    }

    // ----------------------------------------------------------------- Refund
    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest
            {
                Amount = new Money { CurrencyCode = currencyCode, Value = MoneyFormatter.Format(amount.Value, currencyCode) }
            }
            : null;

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=minimal",
                ct: ct);

            if (string.IsNullOrEmpty(refund.Id))
            {
                throw new PayPalPaymentException("PayPal did not return a refund id.", 502);
            }

            return new RefundResult(refund.Id!, refund.Status?.Value ?? "PENDING");
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateApiError(ex, "refund capture",
                ex.Error.TryGetError(out var e) ? e : null,
                !ex.Error.TryGetError(out _) && ex.Error.TryGetRawError(out var r) ? r : null);
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "refund capture");
        }
    }

    // ----------------------------------------------------------------- Vault: save card
    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken ct = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                ct: ct);

            if (string.IsNullOrEmpty(response.Id))
            {
                throw new PayPalPaymentException("PayPal did not return a vault token id.", 502);
            }

            var vaultedCard = response.PaymentSource?.Card;
            var brand = vaultedCard?.Brand?.Value ?? "CARD";
            var lastDigits = vaultedCard?.LastDigits ?? string.Empty;
            var expiry = vaultedCard?.Expiry ?? card.Expiry;

            return new VaultCardResult(response.Id!, brand, lastDigits, expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            var error1 = ex.Error.TryGetError1(out var e) ? e : null;
            var raw = error1 is null && ex.Error.TryGetRawError(out var r) ? r : null;
            throw TranslateVaultError(error1, raw, "save card");
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "save card");
        }
    }

    // ----------------------------------------------------------------- Vault: delete card
    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            var error1 = ex.Error.TryGetError1(out var e) ? e : null;
            var raw = error1 is null && ex.Error.TryGetRawError(out var r) ? r : null;
            throw TranslateVaultError(error1, raw, "delete card");
        }
        catch (Exception ex) when (IsTransportOrParse(ex))
        {
            throw TranslateTransportOrParse(ex, "delete card");
        }
    }

    // ----------------------------------------------------------------- Transaction search (reconciliation)
    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        var byId = new Dictionary<string, PayPalTransaction>(StringComparer.Ordinal);
        var unkeyed = new List<PayPalTransaction>();

        // PayPal limits each search to a ~31-day window; chunk the range and page each window fully.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31);
            if (windowEnd > to)
            {
                windowEnd = to;
            }

            await SearchWindowAsync(windowStart, windowEnd, byId, unkeyed, ct);

            if (windowEnd >= to)
            {
                break;
            }

            windowStart = windowEnd;
        }

        var results = new List<PayPalTransaction>(byId.Values);
        results.AddRange(unkeyed);
        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset windowStart, DateTimeOffset windowEnd,
        IDictionary<string, PayPalTransaction> byId, List<PayPalTransaction> unkeyed, CancellationToken ct)
    {
        var page = 1;
        while (true)
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: FormatSearchDate(windowStart),
                    endDate: FormatSearchDate(windowEnd),
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
            }
            catch (SdkException<RawError> ex)
            {
                var status = (int)ex.Error.StatusCode;
                throw new PayPalPaymentException(
                    $"PayPal transaction search failed with HTTP {status}.", status, ex);
            }
            catch (Exception ex) when (IsTransportOrParse(ex))
            {
                throw TranslateTransportOrParse(ex, "transaction search");
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    var transaction = new PayPalTransaction(
                        info?.TransactionId,
                        info?.TransactionStatus,
                        MoneyFormatter.ParseOrNull(info?.TransactionAmount?.Value),
                        info?.TransactionAmount?.CurrencyCode,
                        MoneyFormatter.ParseOrNull(info?.FeeAmount?.Value),
                        info?.InvoiceId,
                        ParseDate(info?.TransactionInitiationDate));

                    if (!string.IsNullOrEmpty(transaction.TransactionId))
                    {
                        byId[transaction.TransactionId!] = transaction;
                    }
                    else
                    {
                        unkeyed.Add(transaction);
                    }
                }
            }

            var totalPages = response.TotalPages ?? 1;
            if (page >= totalPages)
            {
                break;
            }

            page++;
        }
    }

    // ----------------------------------------------------------------- helpers
    private static CardRequest BuildCardRequest(PaymentSourceInput source)
    {
        if (!string.IsNullOrEmpty(source.VaultId))
        {
            // Reuse a previously vaulted card (see contract sheet: vaulted-card reuse is CardRequest.VaultId).
            return new CardRequest { VaultId = source.VaultId };
        }

        var card = source.Card
            ?? throw new PayPalPaymentException("No card details or saved card supplied for the payment.", 400);

        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card)
        };
    }

    private static Address BuildAddress(CardDetails card)
    {
        return new Address
        {
            CountryCode = card.CountryCode,
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea1 = card.AdminArea1,
            AdminArea2 = card.AdminArea2,
            PostalCode = card.PostalCode
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(OrderAuthorizeResponse response)
    {
        if (response.PurchaseUnits is null)
        {
            return null;
        }

        foreach (var purchaseUnit in response.PurchaseUnits)
        {
            var authorizations = purchaseUnit.Payments?.Authorizations;
            if (authorizations is null)
            {
                continue;
            }

            foreach (var authorization in authorizations)
            {
                if (authorization is not null && !string.IsNullOrEmpty(authorization.Id))
                {
                    return authorization;
                }
            }
        }

        return null;
    }

    private static (string? rel, string? href) FindActionLink(IReadOnlyList<LinkDescription>? links)
    {
        if (links is null)
        {
            return (null, null);
        }

        foreach (var link in links)
        {
            if (!string.IsNullOrEmpty(link.Rel) && !OrdinaryRels.Contains(link.Rel))
            {
                return (link.Rel, link.Href);
            }
        }

        return (null, null);
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
            : (DateTimeOffset?)null;
    }

    private static string FormatSearchDate(DateTimeOffset value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static bool IsStaleAuthorization(PayPalPaymentException ex)
    {
        bool Matches(string s) => s.IndexOf("EXPIR", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("REAUTHOR", StringComparison.OrdinalIgnoreCase) >= 0
            || s.IndexOf("HONOR", StringComparison.OrdinalIgnoreCase) >= 0;

        return ex.Issues.Any(Matches) || Matches(ex.Message);
    }

    private static bool IsTransportOrParse(Exception ex)
        => ex is HttpRequestException
        || ex is TaskCanceledException
        || ex is System.Text.Json.JsonException;

    private static PayPalPaymentException TranslateTransportOrParse(Exception ex, string operation)
    {
        if (ex is System.Text.Json.JsonException)
        {
            // A 2xx or error body that did not match the SDK model. Surface as an unknown provider outcome —
            // never leak System.Text.Json path detail.
            return new PayPalPaymentException(
                $"PayPal returned a response for '{operation}' that could not be processed.", 502, ex);
        }

        return new PayPalPaymentException(
            $"PayPal is currently unreachable while trying to {operation}. Please try again.", null, ex);
    }

    private static PayPalPaymentException TranslateApiError(Exception ex, string operation, Error? error, RawError? raw)
    {
        var (message, status, issues) = DescribeError(error, raw, operation);
        return new PayPalPaymentException(message, status, ex, issues);
    }

    private static (string message, int? status, IReadOnlyList<string> issues) DescribeError(Error? error,
        RawError? raw, string operation)
    {
        if (error is not null)
        {
            var issues = error.Details is null
                ? new List<string>()
                : error.Details.Where(d => d is not null && !string.IsNullOrEmpty(d.Issue))
                    .Select(d => d.Issue!)
                    .ToList();

            var issueText = issues.Count > 0 ? $" [{string.Join("; ", issues)}]" : string.Empty;
            var debug = string.IsNullOrEmpty(error.DebugId) ? string.Empty : $" (debug_id {error.DebugId})";
            var message = $"PayPal rejected the {operation} request: {error.Message}{issueText}{debug}";

            // Typed PayPal errors are caller-actionable client faults (validation/conflict/declined) → 400.
            return (message, 400, issues);
        }

        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            return ($"PayPal rejected the {operation} request with HTTP {status}.", status, new List<string>());
        }

        return ($"PayPal rejected the {operation} request.", 502, new List<string>());
    }

    private static PayPalPaymentException TranslateVaultError(Error1? error, RawError? raw, string operation)
    {
        if (error is not null)
        {
            var issues = error.Details is null
                ? new List<string>()
                : error.Details.Where(d => d is not null && !string.IsNullOrEmpty(d.Issue))
                    .Select(d => d.Issue!)
                    .ToList();

            var issueText = issues.Count > 0 ? $" [{string.Join("; ", issues)}]" : string.Empty;
            var debug = string.IsNullOrEmpty(error.DebugId) ? string.Empty : $" (debug_id {error.DebugId})";
            return new PayPalPaymentException(
                $"PayPal rejected the {operation} request: {error.Message}{issueText}{debug}", 400, null, issues);
        }

        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            return new PayPalPaymentException($"PayPal rejected the {operation} request with HTTP {status}.", status);
        }

        return new PayPalPaymentException($"PayPal rejected the {operation} request.", 502);
    }
}
