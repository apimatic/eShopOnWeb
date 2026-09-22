using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal boundary implemented over the PayPal Server SDK. Owns every SDK call, bounds each with a
/// whole-call deadline, and translates every SDK/transport/parse failure into
/// <see cref="PaymentGatewayException"/>. No SDK type escapes this class.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // Max window PayPal's transaction search accepts is 31 days; use 30 to stay safely inside it.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(30);
    private const int SearchPageSize = 100;
    private const int MaxPagesPerWindow = 200; // protective cap: 20k txns/window before we flag truncation

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly TimeSpan _callTimeout;

    public PayPalPaymentGateway(PayPalServerSdkClient client, PayPalSettings settings, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
        _callTimeout = TimeSpan.FromSeconds(settings.CallTimeoutSeconds);
    }

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeCommand command, CancellationToken ct)
    {
        var card = command.VaultId is not null
            ? new CardRequest { VaultId = command.VaultId }
            : new CardRequest
            {
                Number = command.Card!.Number,
                Expiry = command.Card.Expiry,
                SecurityCode = command.Card.SecurityCode,
                Name = command.Card.CardholderName,
                BillingAddress = MapAddress(command.Card.BillingAddress),
            };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = command.CurrencyCode,
                        Value = FormatAmount(command.Amount),
                    },
                    InvoiceId = command.InvoiceReference,
                    CustomId = command.InvoiceReference,
                    Description = Truncate(command.Description, 127),
                },
            },
            PaymentSource = new PaymentSource { Card = card },
        };

        // Create the order (idempotent via the stable PayPal-Request-Id) with the card supplied inline.
        var created = await CallOrders<CreateOrderError, Order>(
            inner => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: command.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: inner),
            e => e.TryGetError(out var err) ? (err, null) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
            "CreateOrder", ct);

        var payPalOrderId = created.Id ?? throw Provider("PayPal did not return an order id.");
        EnsureNoApprovalRequired(created.Status?.Value, "authorize");

        // A single-step card order may already carry the authorization; only call authorize if it doesn't.
        var auth = ExtractAuthorization(created.PurchaseUnits);
        if (auth is null)
        {
            var authResp = await CallOrders<AuthorizeOrderError, OrderAuthorizeResponse>(
                inner => _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: command.IdempotencyKey + "-auth",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: inner),
                e => e.TryGetError(out var err) ? (err, null) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
                "AuthorizeOrder", ct);

            EnsureNoApprovalRequired(authResp.Status?.Value, "authorize");
            auth = ExtractAuthorization(authResp.PurchaseUnits);
        }

        if (auth?.Id is null)
            throw Provider("PayPal did not return an authorization for the order.");

        var status = auth.Status?.Value;
        if (status == AuthorizationStatus.Denied.Value)
            throw new PaymentGatewayException("PayPal denied the card authorization.", kind: PaymentGatewayFailureKind.CallerError, errorName: "AUTHORIZATION_DENIED");

        _logger.LogInformation("PayPal authorized order {PayPalOrderId} → authorization {AuthorizationId} ({Status})",
            payPalOrderId, auth.Id, status);

        return new PayPalAuthorizationResult
        {
            PayPalOrderId = payPalOrderId,
            AuthorizationId = auth.Id,
            Status = status,
            AuthorizedAt = ParseDate(auth.CreateTime),
            ExpiresAt = ParseDate(auth.ExpirationTime),
        };
    }

    public async Task<PayPalAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var a = await CallPayments<GetAuthorizedPaymentError, PaymentAuthorization>(
            inner => _client.Payments.GetAuthorizedPayment(authorizationId, null, null, ct: inner),
            e => e.TryGetError(out var err) ? (err, null) : e.TryGetNoContent(out var nc) ? (null, nc) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
            "GetAuthorizedPayment", ct);

        return new PayPalAuthorizationState
        {
            AuthorizationId = a.Id ?? authorizationId,
            Status = a.Status?.Value,
            ExpiresAt = ParseDate(a.ExpirationTime),
        };
    }

    public async Task<PayPalAuthorizationState> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var a = await CallPayments<ReauthorizePaymentError, PaymentAuthorization>(
            inner => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: inner),
            e => e.TryGetError(out var err) ? (err, null) : e.TryGetNoContent(out var nc) ? (null, nc) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
            "ReauthorizePayment", ct);

        _logger.LogInformation("PayPal reauthorized {OldAuthorizationId} → {NewAuthorizationId}", authorizationId, a.Id);

        return new PayPalAuthorizationState
        {
            AuthorizationId = a.Id ?? authorizationId,
            Status = a.Status?.Value,
            ExpiresAt = ParseDate(a.ExpirationTime),
        };
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var cap = await CallPayments<CaptureAuthorizedPaymentError, CapturedPayment>(
            inner => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: inner),
            e => e.TryGetError(out var err) ? (err, null) : e.TryGetNoContent(out var nc) ? (null, nc) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
            "CaptureAuthorizedPayment", ct);

        var captureId = cap.Id ?? throw Provider("PayPal did not return a capture id.");
        var status = cap.Status?.Value;
        if (status == CaptureStatus.Declined.Value || status == CaptureStatus.Failed.Value)
            throw new PaymentGatewayException($"PayPal capture was not completed (status {status}).",
                kind: PaymentGatewayFailureKind.CallerError, errorName: status);

        var breakdown = cap.SellerReceivableBreakdown;
        _logger.LogInformation("PayPal captured authorization {AuthorizationId} → capture {CaptureId} ({Status})",
            authorizationId, captureId, status);

        return new PayPalCaptureResult
        {
            CaptureId = captureId,
            Status = status,
            CapturedAt = ParseDate(cap.CreateTime),
            GrossAmount = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(cap.Amount),
            PayPalFee = ParseMoney(breakdown?.PaypalFee),
            NetAmount = ParseMoney(breakdown?.NetAmount),
        };
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callTimeout);
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: cts.Token);
        }
        catch (JsonException)
        {
            // A successful void returns HTTP 204 No Content; the SDK cannot deserialize the empty body and
            // throws JsonException. The void succeeded (2xx) — treat as success. A real failure is non-2xx
            // and surfaces as SdkException<VoidPaymentError> below, keeping its status/reason.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            var (error, raw) = ex.Error.TryGetError(out var e) ? (e, (RawError?)null)
                : ex.Error.TryGetNoContent(out var nc) ? (null, nc)
                : ex.Error.TryGetRawError(out var r) ? (null, r) : (null, null);
            throw Translate("VoidPayment", error, raw, ex);
        }
        catch (Exception ex)
        {
            throw TranslateNonSdk("VoidPayment", ex, ct);
        }

        _logger.LogInformation("PayPal voided authorization {AuthorizationId}", authorizationId);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } };

        var r = await CallPayments<RefundCapturedPaymentError, Refund>(
            inner => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: inner),
            e => e.TryGetError(out var err) ? (err, null) : e.TryGetNoContent(out var nc) ? (null, nc) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
            "RefundCapturedPayment", ct);

        var refundId = r.Id ?? throw Provider("PayPal did not return a refund id.");
        _logger.LogInformation("PayPal refunded capture {CaptureId} → refund {RefundId} ({Status})",
            captureId, refundId, r.Status?.Value);

        return new PayPalRefundResult { RefundId = refundId, Status = r.Status?.Value, Amount = ParseMoney(r.Amount) };
    }

    public async Task<PayPalOrderState?> GetOrderAsync(string payPalOrderId, CancellationToken ct)
    {
        try
        {
            var o = await CallOrders<GetOrderError, Order>(
                inner => _client.Orders.GetOrder(payPalOrderId, fields: null, payPalMockResponse: null, payPalAuthAssertion: null, ct: inner),
                e => e.TryGetError(out var err) ? (err, null) : e.TryGetRawError(out var raw) ? (null, raw) : (null, null),
                "GetOrder", ct);

            var auth = ExtractAuthorization(o.PurchaseUnits);
            return new PayPalOrderState
            {
                Status = o.Status?.Value,
                AuthorizationId = auth?.Id,
                AuthorizationStatus = auth?.Status?.Value,
            };
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalVaultCardCommand command, CancellationToken ct)
    {
        var customer = command.PayPalCustomerId is not null || command.MerchantCustomerId is not null
            ? new Customer { Id = command.PayPalCustomerId, MerchantCustomerId = command.MerchantCustomerId }
            : null;

        // Canonical, no-money-movement card vaulting: create a setup token from the raw card, then exchange
        // it for a permanent payment token. (Posting a raw card directly to /v3/vault/payment-tokens can be
        // rejected by some accounts, so the two-step setup-token flow is the reliable path.)
        var setupBody = new SetupTokenRequest
        {
            Customer = customer,
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = command.Card.Number,
                    Expiry = command.Card.Expiry,
                    Name = command.Card.CardholderName,
                    BillingAddress = MapAddress(command.Card.BillingAddress),
                },
            },
        };

        var setup = await CallSetupCreate(
            inner => _client.Vault.CreateSetupToken(payPalRequestId: null, body: setupBody, ct: inner),
            "CreateSetupToken", ct);
        var setupTokenId = setup.Id ?? throw Provider("PayPal did not return a setup token id.");

        var body = new PaymentTokenRequest
        {
            Customer = customer,
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken },
            },
        };

        var resp = await CallVaultCreate(
            inner => _client.Vault.CreatePaymentToken(payPalRequestId: null, body: body, ct: inner),
            "CreatePaymentToken", ct);

        var vaultId = resp.Id ?? throw Provider("PayPal did not return a vault token id.");
        var card = resp.PaymentSource?.Card;
        _logger.LogInformation("PayPal vaulted a card → token {VaultId} (brand {Brand} ****{Last4})",
            vaultId, card?.Brand?.Value, card?.LastDigits);

        return new PayPalVaultResult
        {
            VaultId = vaultId,
            PayPalCustomerId = resp.Customer?.Id,
            CardBrand = card?.Brand?.Value,
            LastFourDigits = card?.LastDigits,
            Expiry = card?.Expiry,
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        await CallVaultDelete(
            inner => _client.Vault.DeletePaymentToken(vaultId, ct: inner),
            "DeletePaymentToken", ct);

        _logger.LogInformation("PayPal deleted vault token {VaultId}", vaultId);
    }

    public async Task<PayPalReconciliationResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var transactions = new List<PayPalTransaction>();
        var windows = 0;
        var pages = 0;
        var complete = true;

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;
            windows++;

            var page = 1;
            while (true)
            {
                var response = await CallSearch(windowStart, windowEnd, page, ct);
                pages++;

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        transactions.Add(new PayPalTransaction
                        {
                            TransactionId = info.TransactionId,
                            Status = info.TransactionStatus,
                            Amount = ParseMoney(info.TransactionAmount),
                            CurrencyCode = info.TransactionAmount?.CurrencyCode,
                            InvoiceId = info.InvoiceId,
                            CustomField = info.CustomField,
                            InitiationDate = ParseDate(info.TransactionInitiationDate),
                        });
                    }
                }

                var totalPages = response.TotalPages ?? 1;
                if (page >= totalPages) break;
                if (page >= MaxPagesPerWindow) { complete = false; break; }
                page++;
            }

            // advance one second past this window's end to avoid re-fetching the boundary row
            windowStart = windowEnd.AddSeconds(1);
        }

        _logger.LogInformation("PayPal reconciliation swept {Windows} window(s), {Pages} page(s), {Count} transaction(s); complete={Complete}",
            windows, pages, transactions.Count, complete);

        return new PayPalReconciliationResult
        {
            Transactions = transactions,
            WindowsScanned = windows,
            PagesScanned = pages,
            Complete = complete,
        };
    }

    // --- SDK call wrappers (error boundary + whole-call deadline) ---

    private async Task<TResp> CallOrders<TErr, TResp>(
        Func<CancellationToken, Task<TResp>> call,
        Func<TErr, (Error?, RawError?)> extract,
        string op, CancellationToken ct) where TErr : PayPalServerSdk.Core.ErrorResponse.ApiError
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callTimeout);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<TErr> ex)
        {
            var (error, raw) = extract(ex.Error);
            throw Translate(op, error, raw, ex);
        }
        catch (Exception ex)
        {
            throw TranslateNonSdk(op, ex, ct);
        }
    }

    // Payments operations share the same accessor shape but each has its own error type.
    private Task<TResp> CallPayments<TErr, TResp>(
        Func<CancellationToken, Task<TResp>> call,
        Func<TErr, (Error?, RawError?)> extract,
        string op, CancellationToken ct) where TErr : PayPalServerSdk.Core.ErrorResponse.ApiError
        => CallOrders(call, extract, op, ct);

    private async Task<SetupTokenResponse> CallSetupCreate(
        Func<CancellationToken, Task<SetupTokenResponse>> call, string op, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callTimeout);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            var (error, raw) = ex.Error.TryGetError(out var e) ? (e, (RawError?)null)
                : ex.Error.TryGetRawError(out var r) ? (null, r) : (null, null);
            throw Translate(op, error, raw, ex);
        }
        catch (Exception ex)
        {
            throw TranslateNonSdk(op, ex, ct);
        }
    }

    private async Task<PaymentTokenResponse> CallVaultCreate(
        Func<CancellationToken, Task<PaymentTokenResponse>> call, string op, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callTimeout);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            var (error, raw) = ex.Error.TryGetError(out var e) ? (e, (RawError?)null)
                : ex.Error.TryGetRawError(out var r) ? (null, r) : (null, null);
            throw Translate(op, error, raw, ex);
        }
        catch (Exception ex)
        {
            throw TranslateNonSdk(op, ex, ct);
        }
    }

    private async Task CallVaultDelete(Func<CancellationToken, Task> call, string op, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callTimeout);
        try
        {
            await call(cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            var (error, raw) = ex.Error.TryGetError(out var e) ? (e, (RawError?)null)
                : ex.Error.TryGetRawError(out var r) ? (null, r) : (null, null);
            throw Translate(op, error, raw, ex);
        }
        catch (Exception ex)
        {
            throw TranslateNonSdk(op, ex, ct);
        }
    }

    // SearchTransactions is Case B (SdkException<RawError>).
    private async Task<SearchResponse> CallSearch(DateTimeOffset from, DateTimeOffset to, int page, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callTimeout);
        try
        {
            return await _client.TransactionSearch.SearchTransactions(
                startDate: FormatDate(from),
                endDate: FormatDate(to),
                transactionId: null,
                transactionType: null,
                transactionStatus: null,
                transactionAmount: null,
                transactionCurrency: _settings.Currency,
                paymentInstrumentType: null,
                storeId: null,
                terminalId: null,
                fields: "transaction_info",
                balanceAffectingRecordsOnly: "N",
                pageSize: SearchPageSize,
                page: page,
                ct: cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var raw = ex.Error;
            throw Translate("SearchTransactions", null, raw, ex);
        }
        catch (Exception ex)
        {
            throw TranslateNonSdk("SearchTransactions", ex, ct);
        }
    }

    // --- error translation ---

    private PaymentGatewayException Translate(string op, Error? error, RawError? raw, Exception inner)
    {
        if (error is not null)
        {
            var name = error.Name;
            var detail = error.Details?.FirstOrDefault();
            var issue = detail?.Issue;
            var message = detail?.Description ?? error.Message;
            var kind = ClassifyByName(name, issue);
            var allDetails = error.Details is null ? "(none)" : string.Join(" | ", error.Details.Select(d => $"{d.Issue}:{d.Description}"));
            _logger.LogError(inner, "PayPal {Op} failed: {Name} — {Message}; details: {Details} (debug_id {DebugId})", op, name, error.Message, allDetails, error.DebugId);
            return new PaymentGatewayException(
                $"PayPal rejected the {op} request: {message}",
                inner, statusCode: null, errorName: issue ?? name, debugId: error.DebugId, kind: kind);
        }

        if (raw is not null)
        {
            var status = (int)raw.StatusCode;
            var body = SafeReadRaw(raw);
            _logger.LogError(inner, "PayPal {Op} failed with HTTP {Status}: {Body}", op, status, body);
            return new PaymentGatewayException(
                $"PayPal returned HTTP {status} for {op}.",
                inner, statusCode: status, kind: ClassifyByStatus(status));
        }

        _logger.LogError(inner, "PayPal {Op} failed with an unrecognised error shape", op);
        return new PaymentGatewayException($"PayPal returned an unrecognised error for {op}.", inner, kind: PaymentGatewayFailureKind.Provider);
    }

    private PaymentGatewayException TranslateNonSdk(string op, Exception ex, CancellationToken ct)
    {
        switch (ex)
        {
            case PaymentGatewayException pge:
                return pge; // already translated (e.g. from a nested helper)
            case AuthSchemeException:
                _logger.LogError(ex, "PayPal authentication could not be applied for {Op}", op);
                return new PaymentGatewayException("PayPal authentication failed.", ex, kind: PaymentGatewayFailureKind.Provider);
            case JsonException:
                _logger.LogError(ex, "PayPal {Op} returned a response that could not be processed", op);
                return new PaymentGatewayException($"PayPal returned a response that could not be processed during {op}.", ex, kind: PaymentGatewayFailureKind.Provider);
            case OperationCanceledException when !ct.IsCancellationRequested:
                _logger.LogError(ex, "PayPal {Op} timed out", op);
                return new PaymentGatewayException($"The PayPal {op} request timed out.", ex, kind: PaymentGatewayFailureKind.Provider);
            case OperationCanceledException:
                throw new OperationCanceledException(ct); // caller cancelled — propagate
            case HttpRequestException:
                _logger.LogError(ex, "PayPal is unreachable during {Op}", op);
                return new PaymentGatewayException($"PayPal is currently unreachable ({op}).", ex, kind: PaymentGatewayFailureKind.Provider);
            default:
                _logger.LogError(ex, "Unexpected failure during PayPal {Op}", op);
                return new PaymentGatewayException($"Unexpected failure talking to PayPal during {op}.", ex, kind: PaymentGatewayFailureKind.Provider);
        }
    }

    private static PaymentGatewayFailureKind ClassifyByName(string? name, string? issue)
    {
        var token = (issue ?? name ?? string.Empty).ToUpperInvariant();
        if (token.Contains("PAYER_ACTION") || token.Contains("3D") || token.Contains("AUTHENTICATION_REQUIRED"))
            return PaymentGatewayFailureKind.ApprovalRequired;
        if (name is "AUTHENTICATION_FAILURE" or "NOT_AUTHORIZED" or "RATE_LIMIT_REACHED")
            return PaymentGatewayFailureKind.Provider;
        if (name is "INVALID_REQUEST" or "UNPROCESSABLE_ENTITY" or "VALIDATION_ERROR" or "RESOURCE_NOT_FOUND")
            return PaymentGatewayFailureKind.CallerError;
        return PaymentGatewayFailureKind.Provider;
    }

    private static PaymentGatewayFailureKind ClassifyByStatus(int status) => status switch
    {
        401 or 403 or 429 => PaymentGatewayFailureKind.Provider,
        >= 400 and < 500 => PaymentGatewayFailureKind.CallerError,
        _ => PaymentGatewayFailureKind.Provider,
    };

    /// <summary>Throws when PayPal signals a browser approval is required (unsupported here — must be reported).</summary>
    private static void EnsureNoApprovalRequired(string? orderStatus, string op)
    {
        if (orderStatus == OrderStatus.PayerActionRequired.Value)
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this payment in a browser (3-D Secure / payer action). " +
                "This unbranded/direct-card integration cannot complete an in-browser approval.",
                kind: PaymentGatewayFailureKind.ApprovalRequired, errorName: "PAYER_ACTION_REQUIRED");
    }

    private static PaymentGatewayException Provider(string message) =>
        new(message, kind: PaymentGatewayFailureKind.Provider);

    // --- mapping helpers ---

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .Select(pu => pu.Payments?.Authorizations)
            .FirstOrDefault(a => a is { Count: > 0 })?
            .FirstOrDefault();

    private static SdkAddress? MapAddress(PayPalBillingAddress? a) =>
        a is null ? null : new SdkAddress
        {
            AddressLine1 = a.AddressLine1,
            AdminArea2 = a.AdminArea2,
            AdminArea1 = a.AdminArea1,
            PostalCode = a.PostalCode,
            CountryCode = a.CountryCode ?? "US",
        };

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money?.Value is { Length: > 0 } v && decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value.Substring(0, max);

    private static string SafeReadRaw(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return "<unreadable>"; }
    }
}
