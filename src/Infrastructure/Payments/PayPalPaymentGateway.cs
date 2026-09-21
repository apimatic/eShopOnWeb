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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The PayPal implementation of <see cref="IPaymentGateway"/>. This is the only place the PayPal Server SDK
/// is referenced. Every call is bounded by a total deadline, and every SDK/transport failure is translated
/// into a single <see cref="PaymentGatewayException"/> so the rest of the app has one failure type to reason
/// about. Card details are never logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxReconciliationPages = 200;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.Currency;

    // ---- Flow 1: authorize / reauthorize / capture / void / refund -----------------------------------

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        var reference = Truncate(request.OrderReference, 127);
        var currency = _options.Currency;

        // Step 1 — create the PayPal order with AUTHORIZE intent (no payment source yet).
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
                        Value = FormatAmount(request.Amount, currency)
                    },
                    InvoiceId = reference,
                    CustomId = reference,
                    Description = string.IsNullOrWhiteSpace(request.Description)
                        ? null
                        : Truncate(request.Description!, 127)
                }
            }
        };

        string payPalOrderId;
        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{request.IdempotencyKey}-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=minimal",
                ct: token);
            payPalOrderId = created.Id
                ?? throw new PaymentGatewayException("PayPal did not return an order id on create.");
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate(ex, "create the PayPal order"); }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "create the PayPal order", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "create the PayPal order"); }

        // Step 2 — authorize with the funding source in the request (no buyer approval needed for cards).
        var authorizeBody = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = BuildCard(request) }
        };

        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: $"{request.IdempotencyKey}-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeBody,
                prefer: "return=representation",
                ct: token);
        }
        catch (SdkException<AuthorizeOrderError> ex) { throw Translate(ex, "authorize the payment"); }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "authorize the payment", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "authorize the payment"); }

        // A returned order id is an acknowledgement, not an outcome — read the status.
        if (authorized.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this payment in a browser (payer action required).",
                statusCode: 422, issues: new[] { "PAYER_ACTION_REQUIRED" });
        }

        var authorization = authorized.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for the payment (order status: {authorized.Status?.Value ?? "unknown"}).");
        }
        if (authorization.Status == AuthorizationStatus.Denied)
        {
            throw new PaymentGatewayException("The card authorization was denied by PayPal.",
                statusCode: 402, issues: new[] { "AUTHORIZATION_DENIED" });
        }

        var responseCard = authorized.PaymentSource?.Card;
        var last4 = responseCard?.LastDigits ?? LastFour(request.Card?.Number);
        var brand = responseCard?.Brand?.Value;

        _logger.LogInformation(
            "PayPal authorized order {PayPalOrderId} authorization {AuthorizationId} status {Status} for {Reference}",
            payPalOrderId, authorization.Id, authorization.Status?.Value, reference);

        return new AuthorizationResult(
            PayPalOrderId: payPalOrderId,
            AuthorizationId: authorization.Id,
            Status: authorization.Status?.Value ?? "UNKNOWN",
            Amount: ParseMoney(authorization.Amount) ?? request.Amount,
            Currency: authorization.Amount?.CurrencyCode ?? currency,
            ExpiresAt: ParseDate(authorization.ExpirationTime),
            CardBrand: brand,
            CardLast4: last4);
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;
        var currency = _options.Currency;

        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount, currency) }
        };

        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: token);

            if (reauth.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a new authorization on reauthorize.");
            }

            _logger.LogInformation("PayPal reauthorized {Old} -> {New} status {Status}",
                authorizationId, reauth.Id, reauth.Status?.Value);

            return new AuthorizationResult(
                PayPalOrderId: string.Empty,
                AuthorizationId: reauth.Id,
                Status: reauth.Status?.Value ?? "UNKNOWN",
                Amount: ParseMoney(reauth.Amount) ?? amount,
                Currency: reauth.Amount?.CurrencyCode ?? currency,
                ExpiresAt: ParseDate(reauth.ExpirationTime),
                CardBrand: null,
                CardLast4: null);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "reauthorize the payment");
            if (ex.Error.TryGetNoContent(out var raw)) throw FromRaw(raw, "reauthorize the payment");
            if (ex.Error.TryGetRawError(out var raw2)) throw FromRaw(raw2, "reauthorize the payment");
            throw new PaymentGatewayException("Failed to reauthorize the payment.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "reauthorize the payment", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "reauthorize the payment"); }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null, // full capture of the authorized amount
                prefer: "return=representation",
                ct: token);

            if (captured.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a capture id.");
            }
            if (captured.Status == CaptureStatus.Declined || captured.Status == CaptureStatus.Failed)
            {
                throw new PaymentGatewayException(
                    $"The capture was {captured.Status?.Value}.", statusCode: 402,
                    issues: new[] { $"CAPTURE_{captured.Status?.Value}" });
            }

            var breakdown = captured.SellerReceivableBreakdown;
            var result = new CaptureResult(
                CaptureId: captured.Id,
                Status: captured.Status?.Value ?? "UNKNOWN",
                CapturedAmount: ParseMoney(captured.Amount) ?? ParseMoney(breakdown?.GrossAmount) ?? 0m,
                PayPalFee: ParseMoney(breakdown?.PaypalFee),
                NetAmount: ParseMoney(breakdown?.NetAmount),
                Currency: captured.Amount?.CurrencyCode ?? _options.Currency);

            _logger.LogInformation(
                "PayPal captured {CaptureId} amount {Amount} fee {Fee} net {Net} status {Status}",
                result.CaptureId, result.CapturedAmount, result.PayPalFee, result.NetAmount, result.Status);
            return result;
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "capture the payment");
            if (ex.Error.TryGetNoContent(out var raw)) throw FromRaw(raw, "capture the payment");
            if (ex.Error.TryGetRawError(out var raw2)) throw FromRaw(raw2, "capture the payment");
            throw new PaymentGatewayException("Failed to capture the payment.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "capture the payment", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "capture the payment"); }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: token);
            _logger.LogInformation("PayPal voided authorization {AuthorizationId}", authorizationId);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "void the authorization");
            if (ex.Error.TryGetNoContent(out var raw)) throw FromRaw(raw, "void the authorization");
            if (ex.Error.TryGetRawError(out var raw2)) throw FromRaw(raw2, "void the authorization");
            throw new PaymentGatewayException("Failed to void the authorization.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "void the authorization", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "void the authorization"); }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, string orderReference, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        var body = new RefundRequest
        {
            Amount = amount is null
                ? null // omit for a full refund
                : new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value, currency) },
            CustomId = Truncate(orderReference, 127)
        };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: token);

            if (refund.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a refund id.");
            }

            _logger.LogInformation("PayPal refunded {RefundId} amount {Amount} status {Status}",
                refund.Id, ParseMoney(refund.Amount), refund.Status?.Value);

            return new RefundResult(
                RefundId: refund.Id,
                Status: refund.Status?.Value ?? "UNKNOWN",
                Amount: ParseMoney(refund.Amount) ?? amount ?? 0m,
                Currency: refund.Amount?.CurrencyCode ?? currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "refund the payment");
            if (ex.Error.TryGetNoContent(out var raw)) throw FromRaw(raw, "refund the payment");
            if (ex.Error.TryGetRawError(out var raw2)) throw FromRaw(raw2, "refund the payment");
            throw new PaymentGatewayException("Failed to refund the payment.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "refund the payment", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "refund the payment"); }
    }

    // ---- Flow 2: saved cards (Vault) ------------------------------------------------------------------

    public async Task<SavedCardResult> SaveCardAsync(SaveCardRequest request, string? existingCustomerId,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        var body = new PaymentTokenRequest
        {
            Customer = existingCustomerId is null ? null : new Customer { Id = existingCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardholderName,
                    BillingAddress = BuildAddress(request.Card.BillingAddress)
                }
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: token);

            if (response.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a saved-card id.");
            }
            _logger.LogInformation("PayPal vaulted a card {PaymentMethodId} for customer {CustomerId}",
                response.Id, response.Customer?.Id);
            return MapSavedCard(response);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "save the card");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "save the card");
            throw new PaymentGatewayException("Failed to save the card.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "save the card", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "save the card"); }
    }

    public async Task<IReadOnlyList<SavedCardResult>> ListCardsAsync(string customerId, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        var results = new List<SavedCardResult>();
        var page = 1;
        const int maxPages = 20;
        try
        {
            while (page <= maxPages)
            {
                var response = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: customerId,
                    pageSize: 50,
                    page: page,
                    totalRequired: true,
                    ct: token);

                var tokens = response.PaymentTokens;
                if (tokens is { Count: > 0 })
                {
                    results.AddRange(tokens.Where(t => t.Id is not null).Select(MapSavedCard));
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages || tokens is null || tokens.Count == 0) break;
                page++;
            }
            return results;
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "list saved cards");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "list saved cards");
            throw new PaymentGatewayException("Failed to list saved cards.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "list saved cards", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "list saved cards"); }
    }

    public async Task DeleteCardAsync(string paymentMethodId, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        try
        {
            await _client.Vault.DeletePaymentToken(id: paymentMethodId, ct: token);
            _logger.LogInformation("PayPal deleted saved card {PaymentMethodId}", paymentMethodId);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err, "delete the saved card");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "delete the saved card");
            throw new PaymentGatewayException("Failed to delete the saved card.", inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "delete the saved card", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "delete the saved card"); }
    }

    // ---- Reconciliation -------------------------------------------------------------------------------

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        var token = cts.Token;

        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);
        var transactions = new List<GatewayTransaction>();
        var page = 1;

        try
        {
            while (page <= MaxReconciliationPages)
            {
                var response = await _client.TransactionSearch.SearchTransactions(
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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: token);

                var details = response.TransactionDetails;
                if (details is { Count: > 0 })
                {
                    transactions.AddRange(details.Select(d => MapTransaction(d)));
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages || details is null || details.Count == 0) break;
                page++;
            }

            if (page > MaxReconciliationPages)
            {
                _logger.LogWarning(
                    "Reconciliation stopped at the {MaxPages}-page cap for {From}..{To}; result may be partial.",
                    MaxReconciliationPages, startDate, endDate);
            }
            return transactions;
        }
        catch (SdkException<RawError> ex) // TransactionSearch is Case B
        {
            throw FromRaw(ex.Error, "search PayPal transactions");
        }
        catch (Exception ex) when (IsTransport(ex, token, ct)) { throw Transport(ex, "search PayPal transactions", ct); }
        catch (JsonException ex) { throw Unprocessable(ex, "search PayPal transactions"); }
    }

    // ---- mapping helpers ------------------------------------------------------------------------------

    private CardRequest BuildCard(AuthorizeCardRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SavedCardVaultId))
        {
            // Pay with a saved (vaulted) card by referencing its vault token id.
            return new CardRequest { VaultId = request.SavedCardVaultId };
        }

        var card = request.Card
            ?? throw new PaymentGatewayException("No card details or saved card were supplied.");
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address? BuildAddress(CardBillingAddress? address)
    {
        // Address.CountryCode is required by the SDK, so only build one when a country is supplied.
        if (address is null || string.IsNullOrWhiteSpace(address.CountryCode)) return null;
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode!
        };
    }

    private static SavedCardResult MapSavedCard(PaymentTokenResponse response)
    {
        var card = response.PaymentSource?.Card;
        return new SavedCardResult(
            PaymentMethodId: response.Id!,
            CustomerId: response.Customer?.Id,
            CardBrand: card?.Brand?.Value,
            CardLast4: card?.LastDigits,
            Expiry: card?.Expiry,
            CardholderName: card?.Name);
    }

    private static GatewayTransaction MapTransaction(TransactionDetails details)
    {
        var info = details.TransactionInfo;
        return new GatewayTransaction(
            TransactionId: info?.TransactionId,
            InvoiceId: info?.InvoiceId,
            CustomField: info?.CustomField,
            Amount: ParseMoney(info?.TransactionAmount),
            CurrencyCode: info?.TransactionAmount?.CurrencyCode,
            Status: info?.TransactionStatus,
            InitiatedAt: ParseDate(info?.TransactionInitiationDate));
    }

    // ---- error translation ----------------------------------------------------------------------------

    private PaymentGatewayException Translate(SdkException<CreateOrderError> ex, string action)
    {
        if (ex.Error.TryGetError(out var err)) return FromError(err, action);
        if (ex.Error.TryGetRawError(out var raw)) return FromRaw(raw, action);
        return new PaymentGatewayException($"Failed to {action}.", inner: ex);
    }

    private PaymentGatewayException Translate(SdkException<AuthorizeOrderError> ex, string action)
    {
        if (ex.Error.TryGetError(out var err)) return FromError(err, action);
        if (ex.Error.TryGetRawError(out var raw)) return FromRaw(raw, action);
        return new PaymentGatewayException($"Failed to {action}.", inner: ex);
    }

    private PaymentGatewayException FromError(Error err, string action)
    {
        var issues = err.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrEmpty(i)).ToList()
                     ?? new List<string>();
        _logger.LogWarning("PayPal rejected attempt to {Action}: {Name} {Message} debug_id={DebugId} issues={Issues}",
            action, err.Name, err.Message, err.DebugId, string.Join(",", issues));
        return new PaymentGatewayException(
            message: $"PayPal rejected the request to {action}: {err.Message}",
            statusCode: null,
            providerErrorName: err.Name,
            issues: issues,
            debugId: err.DebugId);
    }

    private PaymentGatewayException FromRaw(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        string body;
        try { body = raw.ReadAsString(); } catch { body = string.Empty; }
        _logger.LogWarning("PayPal returned HTTP {Status} on attempt to {Action}", status, action);
        return new PaymentGatewayException(
            message: $"PayPal returned HTTP {status} while trying to {action}.",
            statusCode: status,
            providerErrorName: null,
            issues: ExtractIssues(body),
            debugId: ExtractDebugId(body));
    }

    private PaymentGatewayException Unprocessable(JsonException ex, string action) =>
        new($"PayPal returned a response that could not be processed while trying to {action}.", inner: ex);

    private static PaymentGatewayException Transport(Exception ex, string action, CancellationToken outerCt)
    {
        // Our own budget elapsing and the SDK's timeout both surface as TaskCanceledException.
        var timedOut = ex is TaskCanceledException or OperationCanceledException && !outerCt.IsCancellationRequested;
        var message = timedOut
            ? $"The request to {action} timed out before PayPal responded."
            : $"PayPal was unreachable while trying to {action}.";
        return new PaymentGatewayException(message, inner: ex);
    }

    private static bool IsTransport(Exception ex, CancellationToken callToken, CancellationToken outerCt)
    {
        // A cancellation from the *caller's* token is a real cancellation — let it propagate.
        if (outerCt.IsCancellationRequested) return false;
        return ex is HttpRequestException or TaskCanceledException or OperationCanceledException;
    }

    private static List<string> ExtractIssues(string body)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(body)) return issues;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("details", out var details) &&
                details.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in details.EnumerateArray())
                {
                    if (d.TryGetProperty("issue", out var issue) && issue.ValueKind == JsonValueKind.String)
                    {
                        issues.Add(issue.GetString()!);
                    }
                }
            }
        }
        catch (JsonException) { /* non-JSON error body — nothing to extract */ }
        return issues;
    }

    private static string? ExtractDebugId(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("debug_id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    // ---- value helpers --------------------------------------------------------------------------------

    private static string FormatAmount(decimal amount, string currency)
    {
        var decimals = CurrencyDecimals(currency);
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static int CurrencyDecimals(string currency) => currency?.ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "VND" or "CLP" or "HUF" or "TWD" or "UGX" or "RWF" => 0,
        "BHD" or "KWD" or "OMR" or "TND" or "IQD" or "JOD" or "LYD" => 3,
        _ => 2
    };

    private static decimal? ParseMoney(Money? money)
    {
        if (money?.Value is null) return null;
        return decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            ? v : (decimal?)null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var dto) ? dto : (DateTimeOffset?)null;
    }

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    private static string? LastFour(string? number) =>
        string.IsNullOrEmpty(number) || number!.Length < 4 ? null : number.Substring(number.Length - 4);
}
