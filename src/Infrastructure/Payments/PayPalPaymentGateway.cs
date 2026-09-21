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
using PayPalServerSdk.Core;
using PayPalServerSdk.Core.Authentication;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Implements the application's <see cref="IPayPalPaymentGateway"/> boundary over the PayPal Server SDK.
/// Every SDK/transport failure is translated to a caller-safe
/// <see cref="PaymentProcessingException"/>; card data flows through here but is never persisted or logged.
/// </summary>
public sealed class PayPalPaymentGateway : IPayPalPaymentGateway
{
    // PayPal transaction search caps a single query at a 31-day range; larger ranges are chunked.
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);
    private const int ReconciliationPageSize = 100;
    private const int MaxReconciliationPagesPerWindow = 500;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<AuthorizationOutcome> AuthorizeAsync(decimal amount, string currency,
        string invoiceReference, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = FormatAmount(amount) },
                    // Unique per order (the account requires a unique invoice id per transaction);
                    // PayPal propagates it to the resulting capture, so we set it once here.
                    InvoiceId = invoiceReference,
                    CustomId = invoiceReference
                }
            },
            PaymentSource = new PaymentSource { Card = BuildCardRequest(instrument) }
        };

        // Ask for the full representation: a card supplied inline with intent=AUTHORIZE is authorized at
        // creation, so the authorization is already on the CreateOrder response and a second AuthorizeOrder
        // call would be rejected as ORDER_ALREADY_AUTHORIZED.
        var created = await ExecuteAsync<Order, CreateOrderError>(
            "CreateOrder",
            (t, ro) => _client.Orders.CreateOrder(null, idempotencyKey + ":create", null, null, null,
                orderRequest, prefer: "return=representation", requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);

        var payPalOrderId = created.Id
            ?? throw new PaymentProcessingException(502, "The payment provider did not return an order id.");
        EnsureNoChallenge(created.Status);

        var authorization = FindAuthorization(created.PurchaseUnits);
        var cardInfo = created.PaymentSource?.Card;

        if (authorization?.Id is null)
        {
            // Not authorized at creation — authorize explicitly.
            var authorized = await ExecuteAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
                "AuthorizeOrder",
                (t, ro) => _client.Orders.AuthorizeOrder(payPalOrderId, null, idempotencyKey + ":auth", null,
                    null, null, prefer: "return=representation", requestOptions: ro, ct: t),
                e => e.TryGetError(out var err) ? err : null,
                ct).ConfigureAwait(false);

            EnsureNoChallenge(authorized.Status);
            authorization = FindAuthorization(authorized.PurchaseUnits);
            cardInfo ??= authorized.PaymentSource?.Card;
        }

        if (authorization?.Id is null)
        {
            throw new PaymentProcessingException(422,
                "The card was not authorized. Please check the details or try a different card.");
        }

        var outcome = new AuthorizationOutcome(
            payPalOrderId,
            authorization.Id,
            authorization.Status?.Value ?? "CREATED",
            ParseDate(authorization.ExpirationTime),
            DescribeCard(cardInfo));

        _logger.LogInformation(
            "PayPal authorized {InvoiceReference}: paypalOrder={PayPalOrderId} authorization={AuthorizationId} status={Status}",
            invoiceReference, payPalOrderId, outcome.AuthorizationId, outcome.Status);
        return outcome;
    }

    public async Task<AuthorizationOutcome> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var auth = await ExecuteAsync<PaymentAuthorization, GetAuthorizedPaymentError>(
            "GetAuthorizedPayment",
            (t, ro) => _client.Payments.GetAuthorizedPayment(authorizationId, null, null,
                requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);

        return new AuthorizationOutcome(
            string.Empty,
            auth.Id ?? authorizationId,
            auth.Status?.Value ?? "UNKNOWN",
            ParseDate(auth.ExpirationTime),
            null);
    }

    public async Task<AuthorizationOutcome> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, CancellationToken ct)
    {
        var reauth = await ExecuteAsync<PaymentAuthorization, ReauthorizePaymentError>(
            "ReauthorizePayment",
            (t, ro) => _client.Payments.ReauthorizePayment(authorizationId, "reauth:" + authorizationId, null,
                new ReauthorizeRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) } },
                prefer: "return=representation", requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);

        var newId = reauth.Id ?? authorizationId;
        _logger.LogInformation("PayPal reauthorized {OldAuthorization} -> {NewAuthorization} status={Status}",
            authorizationId, newId, reauth.Status?.Value);
        return new AuthorizationOutcome(string.Empty, newId, reauth.Status?.Value ?? "CREATED",
            ParseDate(reauth.ExpirationTime), null);
    }

    public async Task<CaptureOutcome> CaptureAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        // No invoice_id here — the capture inherits the order's unique invoice_id set at authorization,
        // and re-setting it would trip the account's unique-invoice-id check.
        var captured = await ExecuteAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            "CaptureAuthorizedPayment",
            (t, ro) => _client.Payments.CaptureAuthorizedPayment(authorizationId, null, idempotencyKey, null,
                new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                    FinalCapture = true
                },
                prefer: "return=representation", requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);

        var captureId = captured.Id
            ?? throw new PaymentProcessingException(502, "The payment provider did not return a capture id.");
        var breakdown = captured.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount?.Value) ?? ParseMoney(captured.Amount?.Value) ?? amount;
        var fee = ParseMoney(breakdown?.PaypalFee?.Value);
        var net = ParseMoney(breakdown?.NetAmount?.Value);

        _logger.LogInformation(
            "PayPal captured authorization {AuthorizationId}: capture={CaptureId} status={Status} gross={Gross} fee={Fee} net={Net}",
            authorizationId, captureId, captured.Status?.Value, gross, fee, net);
        return new CaptureOutcome(captureId, captured.Status?.Value ?? "COMPLETED", gross, fee, net, currency);
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct)
    {
        // Ask for a representation body: with the default "return=minimal" PayPal answers 204 No Content,
        // which the SDK cannot deserialize into PaymentAuthorization. The tolerant executor treats a
        // successful (2xx) empty/undeserializable body as success rather than a failure.
        await ExecuteVoidTolerantAsync<VoidPaymentError>(
            "VoidPayment",
            (t, ro) => _client.Payments.VoidPayment(authorizationId, null, null, "void:" + authorizationId,
                prefer: "return=representation", requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);
        _logger.LogInformation("PayPal voided authorization {AuthorizationId}", authorizationId);
    }

    public async Task<RefundOutcome> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct)
    {
        // No invoice_id on refunds — the account's unique-invoice-id check would otherwise reject a second
        // partial refund. The caller-supplied idempotency key is what makes a repeat a no-op.
        var refundRequest = new RefundRequest
        {
            Amount = amount.HasValue
                ? new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) }
                : null
        };

        var refund = await ExecuteAsync<Refund, RefundCapturedPaymentError>(
            "RefundCapturedPayment",
            (t, ro) => _client.Payments.RefundCapturedPayment(captureId, null, idempotencyKey, null,
                refundRequest, prefer: "return=representation", requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);

        var refundId = refund.Id
            ?? throw new PaymentProcessingException(502, "The payment provider did not return a refund id.");
        var refunded = ParseMoney(refund.Amount?.Value) ?? amount ?? 0m;
        _logger.LogInformation("PayPal refunded capture {CaptureId}: refund={RefundId} status={Status} amount={Amount}",
            captureId, refundId, refund.Status?.Value, refunded);
        return new RefundOutcome(refundId, refund.Status?.Value ?? "PENDING", refunded);
    }

    public async Task<VaultCardOutcome> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        var token = await ExecuteAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            "CreatePaymentToken",
            (t, ro) => _client.Vault.CreatePaymentToken(idempotencyKey, body, requestOptions: ro, ct: t),
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);

        var vaultId = token.Id
            ?? throw new PaymentProcessingException(502, "The payment provider did not return a vault id.");
        var respCard = token.PaymentSource?.Card;
        var outcome = new VaultCardOutcome(
            vaultId,
            respCard?.Brand?.Value ?? "CARD",
            respCard?.LastDigits ?? "****",
            respCard?.Expiry,
            respCard?.Name ?? card.CardholderName);

        _logger.LogInformation("PayPal vaulted a card: vault={VaultId} brand={Brand} last4={Last4}",
            vaultId, outcome.Brand, outcome.Last4);
        return outcome;
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        await ExecuteAsync<bool, DeletePaymentTokenError>(
            "DeletePaymentToken",
            async (t, ro) =>
            {
                await _client.Vault.DeletePaymentToken(vaultId, requestOptions: ro, ct: t).ConfigureAwait(false);
                return true;
            },
            e => e.TryGetError(out var err) ? err : null,
            ct).ConfigureAwait(false);
        _logger.LogInformation("PayPal deleted vaulted card {VaultId}", vaultId);
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
        {
            throw new PaymentProcessingException(400, "The reconciliation 'to' date must not precede 'from'.");
        }

        var byTransactionId = new Dictionary<string, ReconciliationTransaction>(StringComparer.Ordinal);
        var unkeyed = new List<ReconciliationTransaction>();

        // Chunk the whole range into <=31-day windows (PayPal's search limit) and page every window,
        // so the report covers the entire range rather than just its first page or first window.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxSearchWindow;
            if (windowEnd > to) windowEnd = to;

            await SearchWindowAsync(windowStart, windowEnd, byTransactionId, unkeyed, ct).ConfigureAwait(false);

            if (windowEnd >= to) break;
            windowStart = windowEnd;
        }

        var results = new List<ReconciliationTransaction>(byTransactionId.Values);
        results.AddRange(unkeyed);
        return results;
    }

    private async Task SearchWindowAsync(DateTimeOffset from, DateTimeOffset to,
        Dictionary<string, ReconciliationTransaction> byTransactionId, List<ReconciliationTransaction> unkeyed,
        CancellationToken ct)
    {
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);
        var page = 1;
        var totalPages = 1;

        do
        {
            var currentPage = page;
            var response = await ExecuteRawAsync(
                "SearchTransactions",
                (t, ro) => _client.TransactionSearch.SearchTransactions(
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
                    pageSize: ReconciliationPageSize,
                    page: currentPage,
                    requestOptions: ro,
                    ct: t),
                ct).ConfigureAwait(false);

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var mapped = MapTransaction(detail);
                    if (mapped.TransactionId is { Length: > 0 } id)
                    {
                        byTransactionId[id] = mapped;   // dedupe across window boundaries
                    }
                    else
                    {
                        unkeyed.Add(mapped);
                    }
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;

            if (page > MaxReconciliationPagesPerWindow)
            {
                _logger.LogWarning(
                    "Reconciliation window {From}..{To} truncated at {Max} pages (totalPages={TotalPages}).",
                    startDate, endDate, MaxReconciliationPagesPerWindow, totalPages);
                break;
            }
        }
        while (page <= totalPages);
    }

    // --- Execution / error boundary ---------------------------------------------------------------

    private async Task<T> ExecuteAsync<T, TError>(
        string operation,
        Func<CancellationToken, RequestOptions, Task<T>> call,
        Func<TError, Error?> getTypedError,
        CancellationToken ct)
        where TError : ApiError
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        int? httpStatus = null;
        var requestOptions = new RequestOptions
        {
            Hooks = new SdkHook[] { SdkHook.OnResponse((res, _) => httpStatus = (int)res.StatusCode) }
        };

        try
        {
            return await call(cts.Token, requestOptions).ConfigureAwait(false);
        }
        catch (SdkException<TError> ex)
        {
            var body = getTypedError(ex.Error);
            if (body is null && ex.Error.TryGetRawError(out var raw))
            {
                httpStatus ??= (int)raw.StatusCode;
            }
            throw BuildProviderException(operation, httpStatus, body, ex);
        }
        catch (SdkException<RawError> ex)
        {
            httpStatus ??= (int)ex.Error.StatusCode;
            throw BuildProviderException(operation, httpStatus, null, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} failed: credentials could not be applied.", operation);
            throw new PaymentProcessingException(502, "The payment provider is unavailable.", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response.", operation);
            throw new PaymentProcessingException(502,
                "The payment provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", operation);
            throw new PaymentProcessingException(504,
                "The payment provider could not be reached. Please try again.", ex);
        }
    }

    // Like ExecuteAsync but for an operation whose success body may be empty (e.g. a 204). A JsonException
    // raised while deserializing a 2xx response is treated as success, not a failure.
    private async Task ExecuteVoidTolerantAsync<TError>(
        string operation,
        Func<CancellationToken, RequestOptions, Task<PaymentAuthorization>> call,
        Func<TError, Error?> getTypedError,
        CancellationToken ct)
        where TError : ApiError
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        int? httpStatus = null;
        var requestOptions = new RequestOptions
        {
            Hooks = new SdkHook[] { SdkHook.OnResponse((res, _) => httpStatus = (int)res.StatusCode) }
        };

        try
        {
            await call(cts.Token, requestOptions).ConfigureAwait(false);
        }
        catch (JsonException) when (httpStatus is >= 200 and < 300)
        {
            // Empty/undeserializable body on a successful response — the operation succeeded.
        }
        catch (SdkException<TError> ex)
        {
            var body = getTypedError(ex.Error);
            if (body is null && ex.Error.TryGetRawError(out var raw))
            {
                httpStatus ??= (int)raw.StatusCode;
            }
            throw BuildProviderException(operation, httpStatus, body, ex);
        }
        catch (SdkException<RawError> ex)
        {
            httpStatus ??= (int)ex.Error.StatusCode;
            throw BuildProviderException(operation, httpStatus, null, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} failed: credentials could not be applied.", operation);
            throw new PaymentProcessingException(502, "The payment provider is unavailable.", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response.", operation);
            throw new PaymentProcessingException(502,
                "The payment provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", operation);
            throw new PaymentProcessingException(504,
                "The payment provider could not be reached. Please try again.", ex);
        }
    }

    private async Task<T> ExecuteRawAsync<T>(
        string operation,
        Func<CancellationToken, RequestOptions, Task<T>> call,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));

        int? httpStatus = null;
        var requestOptions = new RequestOptions
        {
            Hooks = new SdkHook[] { SdkHook.OnResponse((res, _) => httpStatus = (int)res.StatusCode) }
        };

        try
        {
            return await call(cts.Token, requestOptions).ConfigureAwait(false);
        }
        catch (SdkException<RawError> ex)
        {
            httpStatus ??= (int)ex.Error.StatusCode;
            throw BuildProviderException(operation, httpStatus, null, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response.", operation);
            throw new PaymentProcessingException(502,
                "The payment provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", operation);
            throw new PaymentProcessingException(504,
                "The payment provider could not be reached. Please try again.", ex);
        }
    }

    private PaymentProcessingException BuildProviderException(string operation, int? httpStatus, Error? body,
        Exception inner)
    {
        var issues = body?.Details is { Count: > 0 } details
            ? string.Join("; ", details.Select(d => $"{d.Issue}({d.Field}): {d.Description}"))
            : null;

        _logger.LogError(inner,
            "PayPal {Operation} failed. providerStatus={Status} name={Name} debug_id={DebugId} issues={Issues}",
            operation, httpStatus, body?.Name ?? "n/a", body?.DebugId ?? "n/a", issues ?? "n/a");

        // Prefer the specific issue description for caller-actionable statuses; PayPal's issue text is safe.
        var callerMessage = issues ?? body?.Message;
        var (status, message) = MapStatus(httpStatus, callerMessage);
        return new PaymentProcessingException(status, message, inner);
    }

    // Keep the caller/provider fault split: our credentials/quota problems become 5xx (the caller cannot
    // act on them); a provider rejection of the caller's request keeps its 4xx so the caller can.
    private static (int Status, string Message) MapStatus(int? httpStatus, string? providerMessage) => httpStatus switch
    {
        401 or 403 => (502, "The payment provider is unavailable."),
        429 => (503, "The payment provider is busy; please try again shortly."),
        404 => (404, "The requested payment resource was not found."),
        409 => (409, Safe(providerMessage, "The payment is in a state that conflicts with this operation.")),
        400 => (400, Safe(providerMessage, "The payment request was invalid.")),
        422 => (422, Safe(providerMessage, "The payment could not be processed.")),
        >= 400 and < 500 => (httpStatus.Value, Safe(providerMessage, "The payment request could not be processed.")),
        _ => (502, "A payment provider error occurred; please try again.")
    };

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException ||
        (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested);

    // --- Mapping helpers --------------------------------------------------------------------------

    private static CardRequest BuildCardRequest(PaymentInstrument instrument)
    {
        if (!string.IsNullOrWhiteSpace(instrument.VaultId))
        {
            return new CardRequest { VaultId = instrument.VaultId };
        }

        var card = instrument.Card
            ?? throw new PaymentProcessingException(400, "No card or saved card was supplied for payment.");

        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.CardholderName,
            BillingAddress = BuildAddress(card)
        };
    }

    private static PayPalServerSdk.Models.Address? BuildAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.CountryCode))
        {
            return null;
        }

        return new PayPalServerSdk.Models.Address
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea1 = card.AdminArea1,
            AdminArea2 = card.AdminArea2,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode!
        };
    }

    private static AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static string? DescribeCard(CardResponse? card)
    {
        if (card is null)
        {
            return null;
        }

        var brand = card.Brand?.Value;
        var last4 = card.LastDigits;
        if (!string.IsNullOrEmpty(brand) && !string.IsNullOrEmpty(last4))
        {
            return $"{brand} ending {last4}";
        }
        if (!string.IsNullOrEmpty(last4))
        {
            return $"Card ending {last4}";
        }
        return brand;
    }

    private static ReconciliationTransaction MapTransaction(TransactionDetails detail)
    {
        var info = detail.TransactionInfo;
        return new ReconciliationTransaction(
            info?.TransactionId,
            info?.PaypalReferenceId,
            info?.InvoiceId,
            ParseMoney(info?.TransactionAmount?.Value),
            ParseMoney(info?.FeeAmount?.Value),
            info?.TransactionAmount?.CurrencyCode,
            info?.TransactionStatus,
            ParseDate(info?.TransactionInitiationDate));
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (decimal?)null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : (DateTimeOffset?)null;

    private static void EnsureNoChallenge(OrderStatus? status)
    {
        if (status == OrderStatus.PayerActionRequired)
        {
            // Per the task: do not build a browser approval round-trip — report it as unsupported.
            throw new PaymentProcessingException(422,
                "This card requires additional authentication (3-D Secure) in a browser, " +
                "which this integration does not support. Please use a different card.");
        }
    }

    private static string Safe(string? providerMessage, string fallback) =>
        string.IsNullOrWhiteSpace(providerMessage) ? fallback : providerMessage!;
}
