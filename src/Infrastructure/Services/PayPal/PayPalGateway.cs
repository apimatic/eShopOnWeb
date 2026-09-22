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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The only place the PayPal Server SDK is used. Translates domain payment operations into PayPal
/// Orders/Payments/Vault/TransactionSearch calls, and every provider/transport failure into a
/// <see cref="PaymentGatewayException"/> (or <see cref="BrowserApprovalRequiredException"/>).
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;
    private readonly TimeSpan _budget;
    private readonly int _maxReconciliationPages;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
        Currency = options.Value.Currency;
        _budget = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);
        _maxReconciliationPages = options.Value.MaxReconciliationPages;
    }

    public string Currency { get; }

    // ---------- Authorize (create PayPal order + place hold) ----------

    public async Task<AuthorizeResult> AuthorizeAsync(AuthorizeCommand command, CancellationToken ct)
    {
        var purchaseUnit = new PurchaseUnitRequest
        {
            Amount = new AmountWithBreakdown { CurrencyCode = Currency, Value = FormatAmount(command.Amount) },
            InvoiceId = command.InvoiceId,
            CustomId = command.CustomId,
            Description = command.Description
        };
        var paymentSource = command.VaultTokenId is not null
            ? new PaymentSource { Card = new CardRequest { VaultId = command.VaultTokenId } }
            : new PaymentSource { Card = BuildCard(command.Card!) };

        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[] { purchaseUnit },
            PaymentSource = paymentSource
        };

        // 1) Create the PayPal order (single-step card create requires a PayPal-Request-Id).
        Order order;
        try
        {
            order = await WithBudget(t => _client.Orders.CreateOrder(
                payPalMockResponse: null, payPalRequestId: command.CreateRequestId,
                payPalPartnerAttributionId: null, payPalClientMetadataId: null, payPalAuthAssertion: null,
                body: body, prefer: "return=representation", ct: t), ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            // Create did not return an id, so there is nothing to re-read; the deterministic
            // PayPal-Request-Id makes a retry of this same call return the original order.
            throw Unreachable(ex);
        }

        var payPalOrderId = order.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id.");

        if (order.Status == OrderStatus.PayerActionRequired)
            throw new BrowserApprovalRequiredException(
                $"PayPal requires the shopper to approve this payment in a browser (order {payPalOrderId}). " +
                "This integration does not perform browser approval.");

        // A single-step card create with intent=AUTHORIZE places the hold during CreateOrder itself, so
        // the authorization is already on the response. Use it and skip a redundant AuthorizeOrder call
        // (which would fail ORDER_ALREADY_AUTHORIZED).
        var createdAuthorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (!string.IsNullOrEmpty(createdAuthorization?.Id))
        {
            return BuildAuthorizeResult(payPalOrderId, createdAuthorization!.Id, createdAuthorization.Status?.Value);
        }

        // 2) Otherwise authorize the order explicitly (e.g. an approved order without an inline auth).
        try
        {
            var authResponse = await WithBudget(t => _client.Orders.AuthorizeOrder(
                payPalOrderId, payPalMockResponse: null, payPalRequestId: command.AuthorizeRequestId,
                payPalClientMetadataId: null, payPalAuthAssertion: null, body: null,
                prefer: "return=representation", ct: t), ct);

            if (authResponse.Status == OrderStatus.PayerActionRequired)
                throw new BrowserApprovalRequiredException(
                    $"PayPal requires the shopper to approve this payment in a browser (order {payPalOrderId}). " +
                    "This integration does not perform browser approval.");

            var authorization = authResponse.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            return BuildAuthorizeResult(payPalOrderId, authorization?.Id, authorization?.Status?.Value);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            // Unknown outcome: re-read the order to see whether the hold actually landed.
            var recovered = await TryRecoverAuthorizationAsync(payPalOrderId, ct);
            if (recovered is not null) return recovered;
            throw Unreachable(ex);
        }
    }

    private AuthorizeResult BuildAuthorizeResult(string payPalOrderId, string? authorizationId, string? status)
    {
        var denied = string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase);
        var success = !string.IsNullOrEmpty(authorizationId) && !denied;
        return new AuthorizeResult
        {
            Success = success,
            PayPalOrderId = payPalOrderId,
            AuthorizationId = authorizationId,
            AuthorizationStatus = status,
            FailureReason = success ? null : $"Authorization was not approved (status {status ?? "none"})."
        };
    }

    private async Task<AuthorizeResult?> TryRecoverAuthorizationAsync(string payPalOrderId, CancellationToken ct)
    {
        try
        {
            var order = await WithBudget(t => _client.Orders.GetOrder(
                payPalOrderId, fields: null, payPalMockResponse: null, payPalAuthAssertion: null, ct: t), ct);
            var authorization = order.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (!string.IsNullOrEmpty(authorization?.Id))
            {
                _logger.LogWarning($"Recovered authorization {authorization!.Id} for order {payPalOrderId} after a transport failure.");
                return BuildAuthorizeResult(payPalOrderId, authorization.Id, authorization.Status?.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not re-read order {payPalOrderId} to recover authorization: {ex.Message}");
        }
        return null;
    }

    // ---------- Capture (take the money at fulfilment) ----------

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string payPalOrderId, decimal amount,
        string requestId, CancellationToken ct)
    {
        try
        {
            var captured = await WithBudget(t => _client.Payments.CaptureAuthorizedPayment(
                authorizationId, payPalMockResponse: null, payPalRequestId: requestId, payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true }, prefer: "return=representation", ct: t), ct);
            return BuildCaptureResult(captured);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            var recovered = await TryRecoverCaptureAsync(payPalOrderId, ct);
            if (recovered is not null) return recovered;
            throw Unreachable(ex);
        }
    }

    private CaptureResult BuildCaptureResult(CapturedPayment captured)
    {
        var status = captured.Status?.Value;
        var success = string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(captured.Id);
        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult
        {
            Success = success,
            CaptureId = captured.Id,
            Status = status,
            CapturedAmount = ParseAmount(captured.Amount?.Value) ?? ParseAmount(breakdown?.GrossAmount?.Value) ?? 0m,
            PaypalFee = ParseAmount(breakdown?.PaypalFee?.Value),
            NetAmount = ParseAmount(breakdown?.NetAmount?.Value),
            FailureReason = success ? null : $"Capture was not completed (status {status ?? "none"})."
        };
    }

    private async Task<CaptureResult?> TryRecoverCaptureAsync(string payPalOrderId, CancellationToken ct)
    {
        try
        {
            var order = await WithBudget(t => _client.Orders.GetOrder(
                payPalOrderId, fields: null, payPalMockResponse: null, payPalAuthAssertion: null, ct: t), ct);
            var capture = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Captures?.FirstOrDefault();
            if (!string.IsNullOrEmpty(capture?.Id))
            {
                _logger.LogWarning($"Recovered capture {capture!.Id} for order {payPalOrderId} after a transport failure.");
                return new CaptureResult
                {
                    Success = string.Equals(capture.Status?.Value, "COMPLETED", StringComparison.OrdinalIgnoreCase),
                    CaptureId = capture.Id,
                    Status = capture.Status?.Value,
                    CapturedAmount = ParseAmount(capture.Amount?.Value)
                        ?? ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount?.Value) ?? 0m,
                    PaypalFee = ParseAmount(capture.SellerReceivableBreakdown?.PaypalFee?.Value),
                    NetAmount = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not re-read order {payPalOrderId} to recover capture: {ex.Message}");
        }
        return null;
    }

    // ---------- Reauthorize (renew a stale hold) ----------

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId,
        CancellationToken ct)
    {
        try
        {
            var reauth = await WithBudget(t => _client.Payments.ReauthorizePayment(
                authorizationId, payPalRequestId: requestId, payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) } },
                prefer: "return=representation", ct: t), ct);

            var status = reauth.Status?.Value;
            var denied = string.Equals(status, "DENIED", StringComparison.OrdinalIgnoreCase);
            var success = !string.IsNullOrEmpty(reauth.Id) && !denied;
            return new ReauthorizeResult
            {
                Success = success,
                AuthorizationId = reauth.Id,
                Status = status,
                FailureReason = success ? null : $"Reauthorization was not approved (status {status ?? "none"})."
            };
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
    }

    // ---------- Void (release a hold before fulfilment) ----------

    public async Task<VoidResult> VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            var voided = await WithBudget(t => _client.Payments.VoidPayment(
                authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, payPalRequestId: requestId, ct: t), ct);
            return new VoidResult { Success = true, Status = voided?.Status?.Value ?? "VOIDED" };
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        // A successful void commonly answers 204 with an empty body; a JsonException here means
        // "nothing to deserialize", i.e. the void succeeded.
        catch (JsonException) { return new VoidResult { Success = true, Status = "VOIDED" }; }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
    }

    // ---------- Refund (full or partial, caller idempotency key) ----------

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken ct)
    {
        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount.Value) } };
        try
        {
            var refund = await WithBudget(t => _client.Payments.RefundCapturedPayment(
                captureId, payPalMockResponse: null, payPalRequestId: idempotencyKey, payPalAuthAssertion: null,
                body: body, prefer: "return=representation", ct: t), ct);

            var status = refund.Status?.Value;
            var success = !string.IsNullOrEmpty(refund.Id)
                && !string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
            return new RefundResult
            {
                Success = success,
                RefundId = refund.Id,
                Status = status,
                Amount = ParseAmount(refund.Amount?.Value) ?? amount ?? 0m,
                FailureReason = success ? null : $"Refund was not completed (status {status ?? "none"})."
            };
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
    }

    // ---------- Vault (save a card) ----------

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string? customerId, string requestId,
        CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = string.IsNullOrEmpty(customerId) ? null : new Customer { Id = customerId },
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
            var response = await WithBudget(t => _client.Vault.CreatePaymentToken(
                payPalRequestId: requestId, body: body, ct: t), ct);

            var tokenId = response.Id
                ?? throw new PaymentGatewayException("PayPal did not return a vault token id.");
            var cardEntity = response.PaymentSource?.Card;
            return new VaultCardResult
            {
                TokenId = tokenId,
                CustomerId = response.Customer?.Id,
                Brand = cardEntity?.Brand?.Value,
                LastDigits = cardEntity?.LastDigits,
                Expiry = cardEntity?.Expiry,
                CardholderName = cardEntity?.Name
            };
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await WithBudget(async t =>
            {
                await _client.Vault.DeletePaymentToken(vaultTokenId, ct: t);
                return true;
            }, ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // A token that is already gone is a successful delete (idempotent).
            if (ex.Error.TryGetRawError(out var raw) && raw.StatusCode == HttpStatusCode.NotFound)
                return;
            if (ex.Error.TryGetError(out var error)) throw FromError(error, ex);
            if (ex.Error.TryGetRawError(out var raw2)) throw FromRaw(raw2, ex);
            throw Unrecognised(ex);
        }
        catch (JsonException) { /* empty 204 body on success */ }
        catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }
    }

    // ---------- Reconciliation (transaction search over the whole range) ----------

    public async Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        var startDate = FormatDate(from);
        var endDate = FormatDate(to);
        var transactions = new List<GatewayTransaction>();
        var page = 1;
        var totalPages = 1;
        var pagesRead = 0;
        var truncated = false;

        while (true)
        {
            SearchResponse response;
            var currentPage = page;
            try
            {
                response = await WithBudget(t => _client.TransactionSearch.SearchTransactions(
                    startDate: startDate, endDate: endDate,
                    transactionId: null, transactionType: null, transactionStatus: null,
                    transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                    storeId: null, terminalId: null,
                    fields: "transaction_info", balanceAffectingRecordsOnly: "Y",
                    pageSize: 100, page: currentPage, ct: t), ct);
            }
            catch (SdkException<RawError> ex) { throw FromRaw(ex.Error, ex); }
            catch (JsonException ex) { throw Unprocessable(ex); }
            catch (Exception ex) when (IsTransport(ex, ct)) { throw Unreachable(ex); }

            foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                transactions.Add(new GatewayTransaction
                {
                    TransactionId = info.TransactionId,
                    InvoiceId = info.InvoiceId,
                    CustomField = info.CustomField,
                    Amount = ParseAmount(info.TransactionAmount?.Value),
                    Currency = info.TransactionAmount?.CurrencyCode,
                    Fee = ParseAmount(info.FeeAmount?.Value),
                    Status = info.TransactionStatus,
                    InitiatedAt = ParseDate(info.TransactionInitiationDate)
                });
            }

            pagesRead = page;
            totalPages = response.TotalPages ?? page;

            if (page >= totalPages) break;
            if (page >= _maxReconciliationPages)
            {
                truncated = true;
                _logger.LogWarning($"Reconciliation stopped at the {_maxReconciliationPages}-page cap; report is partial (totalPages={totalPages}).");
                break;
            }
            page++;
        }

        return new TransactionSearchResult
        {
            Transactions = transactions,
            PagesRead = pagesRead,
            TotalPages = totalPages,
            Truncated = truncated
        };
    }

    // ---------- helpers ----------

    private async Task<T> WithBudget<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_budget);
        return await call(cts.Token);
    }

    private static CardRequest BuildCard(CardDetails card) => new()
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card)
    };

    private static Address? BuildAddress(CardDetails card)
    {
        // PayPal's Address requires a country code; only send a billing address when we have one.
        if (string.IsNullOrWhiteSpace(card.BillingCountryCode)) return null;
        return new Address
        {
            AddressLine1 = card.BillingLine1,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode!
        };
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested)
        || (ex is OperationCanceledException && !ct.IsCancellationRequested);

    private PaymentGatewayException FromError(Error error, Exception source)
    {
        var detail = error.Details?.FirstOrDefault()?.Issue;
        var message = detail is null
            ? $"PayPal rejected the request: {error.Name} — {error.Message}"
            : $"PayPal rejected the request: {error.Name} — {error.Message} ({detail})";
        _logger.LogWarning($"PayPal error {error.Name} debug_id={error.DebugId}: {error.Message}");
        return new PaymentGatewayException(message, error.Name, error.DebugId, inner: source);
    }

    private PaymentGatewayException FromRaw(RawError raw, Exception source)
    {
        _logger.LogWarning($"PayPal raw error HTTP {(int)raw.StatusCode}.");
        return new PaymentGatewayException($"PayPal returned an error (HTTP {(int)raw.StatusCode}).",
            statusCode: (int)raw.StatusCode, inner: source);
    }

    private static PaymentGatewayException Unprocessable(Exception ex) =>
        new("PayPal returned a response that could not be processed.", inner: ex);

    private static PaymentGatewayException Unreachable(Exception ex) =>
        new("PayPal was unreachable or timed out; the outcome may be unknown. Retry the same request.", inner: ex);

    private static PaymentGatewayException Unrecognised(Exception ex) =>
        new("PayPal returned an unrecognised error.", inner: ex);
}
