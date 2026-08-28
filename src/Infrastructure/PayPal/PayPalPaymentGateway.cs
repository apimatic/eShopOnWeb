using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPal;
using PayPal.Core.ErrorResponse;
using PayPal.Core.Exceptions;
using PayPal.Errors;
using PayPal.Models;
using PayPal.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The PayPal implementation of <see cref="IPaymentGateway"/>. Everything PayPal-shaped stops here:
/// SDK types, wire status strings and provider error bodies are translated into the application's
/// own <see cref="PaymentGatewayException"/> before they leave.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    /// <summary>
    /// PayPal returns only ids, status and links unless asked for the whole resource. Every call
    /// whose result we persist — an authorization id, a capture's fee and net, a refund id, a void's
    /// resulting status — asks for the full representation.
    /// </summary>
    private const string PreferRepresentation = "return=representation";

    /// <summary>The provider caps a transaction-search window at 31 days, so longer ranges are chunked.</summary>
    private static readonly TimeSpan MaxSearchWindow = TimeSpan.FromDays(31);

    /// <summary>Reporting only reaches back three years, so a wider range is a caller mistake.</summary>
    private static readonly TimeSpan MaxSearchRange = TimeSpan.FromDays(3 * 366);

    /// <summary>
    /// A page loop must not depend on the provider ever saying "stop". This is the backstop; hitting
    /// it is logged rather than silently truncating.
    /// </summary>
    private const int MaxPagesPerWindow = 100;

    private const int SearchPageSize = 100;

    /// <summary>
    /// The whole-call budget for one payment operation. The SDK's own timeout is per attempt, so
    /// only a deadline token bounds what the caller actually waits for.
    /// </summary>
    private static readonly TimeSpan PaymentBudget = TimeSpan.FromSeconds(30);

    /// <summary>Reconciliation walks many windows and pages, so it gets its own, wider budget.</summary>
    private static readonly TimeSpan ReportBudget = TimeSpan.FromMinutes(2);

    private readonly PayPalClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
        CurrencyCode = settings.Value.Currency!.Trim().ToUpperInvariant();
    }

    public string CurrencyCode { get; }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        // Step 1 — create the order with intent AUTHORIZE. No payment source here; the card goes on
        // the authorize call below, which is what lets a direct card payment skip buyer approval.
        var order = await InvokeAsync<Order, CreateOrderError>(
            "create order",
            ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.CreateIdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits =
                    [
                        new PurchaseUnitRequest
                        {
                            ReferenceId = "default",
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = CurrencyCode,
                                Value = FormatAmount(request.Amount)
                            },
                            Description = Truncate(request.Description, 127),
                            // Both carry our own reference so reconciliation can line the transaction
                            // up whichever of the two the provider's reporting echoes back.
                            InvoiceId = request.InvoiceId,
                            CustomId = request.InvoiceId
                        }
                    ]
                },
                prefer: PreferRepresentation,
                ct: ct),
            static (CreateOrderError e, out Error v) => e.TryGetError(out v),
            noContent: null,
            budget.Token);

        var payPalOrderId = order.Id
            ?? throw new PaymentGatewayException(
                "PayPal accepted the order but returned no order id, so the payment cannot be continued.",
                PaymentGatewayFailure.Unavailable);

        // Step 2 — authorize it with the card. This is the call that places the hold.
        var authorized = await InvokeAsync<OrderAuthorizeResponse, AuthorizeOrderError>(
            "authorize order",
            ct => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: request.AuthorizeIdempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = BuildCard(request.Instrument)
                    }
                },
                prefer: PreferRepresentation,
                ct: ct),
            static (AuthorizeOrderError e, out Error v) => e.TryGetError(out v),
            noContent: null,
            budget.Token);

        // A challenge the shopper would have to approve in a browser. This integration is
        // server-to-server by design, so it is surfaced rather than worked around.
        if (string.Equals(authorized.Status?.Value, OrderStatus.PayerActionRequired.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this payment in a browser (order status " +
                "PAYER_ACTION_REQUIRED). This integration authorizes cards server-to-server and has no " +
                "browser approval step.",
                PaymentGatewayFailure.ApprovalRequired);
        }

        var authorization = authorized.PurchaseUnits?
            .Select(unit => unit.Payments?.Authorizations?.FirstOrDefault())
            .FirstOrDefault(a => a is not null);

        if (authorization?.Id is null)
        {
            // The order call succeeded, so a hold may exist even though we cannot see its id.
            // Treating that as a plain failure is how a shopper ends up holding an orphaned
            // authorization nobody ever releases.
            throw new PaymentGatewayException(
                $"PayPal authorized order {payPalOrderId} but returned no authorization id " +
                $"(order status {authorized.Status?.Value ?? "unknown"}). Whether funds are held is unknown; " +
                "reconcile before retrying.",
                PaymentGatewayFailure.OutcomeUnknown);
        }

        var amount = ParseAmount(authorization.Amount?.Value)
            ?? throw new PaymentGatewayException(
                $"PayPal returned authorization {authorization.Id} without an amount.",
                PaymentGatewayFailure.OutcomeUnknown);

        return new AuthorizationResult(
            payPalOrderId,
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            amount,
            authorization.Amount?.CurrencyCode ?? CurrencyCode,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        var authorization = await InvokeAsync<PaymentAuthorization, GetAuthorizedPaymentError>(
            "read authorization",
            ct => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct),
            static (GetAuthorizedPaymentError e, out Error v) => e.TryGetError(out v),
            static (GetAuthorizedPaymentError e, out RawError v) => e.TryGetNoContent(out v),
            budget.Token);

        return new AuthorizationSnapshot(
            authorization.Id ?? authorizationId,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseAmount(authorization.Amount?.Value),
            authorization.Amount?.CurrencyCode,
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        var renewed = await InvokeAsync<PaymentAuthorization, ReauthorizePaymentError>(
            "re-authorize payment",
            ct => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = CurrencyCode, Value = FormatAmount(amount) }
                },
                prefer: PreferRepresentation,
                ct: ct),
            static (ReauthorizePaymentError e, out Error v) => e.TryGetError(out v),
            static (ReauthorizePaymentError e, out RawError v) => e.TryGetNoContent(out v),
            budget.Token);

        if (renewed.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal re-authorized the payment but returned no new authorization id.",
                PaymentGatewayFailure.OutcomeUnknown);
        }

        return new AuthorizationSnapshot(
            renewed.Id,
            renewed.Status?.Value ?? "UNKNOWN",
            ParseAmount(renewed.Amount?.Value),
            renewed.Amount?.CurrencyCode,
            ParseTimestamp(renewed.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string invoiceId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        var captured = await InvokeAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            "capture payment",
            ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = CurrencyCode, Value = FormatAmount(amount) },
                    InvoiceId = invoiceId,
                    // Nothing further will be captured against this hold, so release whatever is left.
                    FinalCapture = true
                },
                prefer: PreferRepresentation,
                ct: ct),
            static (CaptureAuthorizedPaymentError e, out Error v) => e.TryGetError(out v),
            static (CaptureAuthorizedPaymentError e, out RawError v) => e.TryGetNoContent(out v),
            budget.Token);

        if (captured.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal captured the payment but returned no capture id; whether money moved is unknown.",
                PaymentGatewayFailure.OutcomeUnknown);
        }

        var breakdown = captured.SellerReceivableBreakdown;

        return new CaptureResult(
            captured.Id,
            captured.Status?.Value ?? "UNKNOWN",
            ParseAmount(captured.Amount?.Value) ?? amount,
            captured.Amount?.CurrencyCode ?? CurrencyCode,
            ParseAmount(breakdown?.PaypalFee?.Value),
            ParseAmount(breakdown?.NetAmount?.Value));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        var voided = await InvokeAsync<PaymentAuthorization, VoidPaymentError>(
            "void authorization",
            ct => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                // Without this PayPal answers 204 with no body, which the generated return type
                // (a PaymentAuthorization) cannot be deserialized from.
                prefer: PreferRepresentation,
                ct: ct),
            static (VoidPaymentError e, out Error v) => e.TryGetError(out v),
            static (VoidPaymentError e, out RawError v) => e.TryGetNoContent(out v),
            budget.Token);

        _logger.LogInformation("Released PayPal authorization {AuthorizationId}; it now reads {Status}.",
            authorizationId, voided.Status?.Value ?? "unknown");
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        // A full refund is an empty body; a partial one names its amount.
        var body = amount is null
            ? null
            : new RefundRequest
            {
                Amount = new Money { CurrencyCode = CurrencyCode, Value = FormatAmount(amount.Value) }
            };

        var refund = await InvokeAsync<Refund, RefundCapturedPaymentError>(
            "refund capture",
            ct => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct),
            static (RefundCapturedPaymentError e, out Error v) => e.TryGetError(out v),
            static (RefundCapturedPaymentError e, out RawError v) => e.TryGetNoContent(out v),
            budget.Token);

        if (refund.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal accepted the refund but returned no refund id; whether money was returned is unknown.",
                PaymentGatewayFailure.OutcomeUnknown);
        }

        var refunded = ParseAmount(refund.Amount?.Value)
            ?? throw new PaymentGatewayException(
                $"PayPal returned refund {refund.Id} without an amount.",
                PaymentGatewayFailure.OutcomeUnknown);

        return new RefundResult(
            refund.Id,
            refund.Status?.Value ?? "UNKNOWN",
            refunded,
            refund.Amount?.CurrencyCode ?? CurrencyCode);
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        var token = await InvokeAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            "vault card",
            ct => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
                {
                    // Reusing the customer keeps one shopper's cards under one vault customer.
                    Customer = string.IsNullOrEmpty(existingCustomerId)
                        ? null
                        : new Customer { Id = existingCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = BuildAddress(card.BillingAddress)
                        }
                    }
                },
                ct: ct),
            static (CreatePaymentTokenError e, out Error v) => e.TryGetError(out v),
            noContent: null,
            budget.Token);

        if (token.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal vaulted the card but returned no payment-token id, so it cannot be used to pay.",
                PaymentGatewayFailure.Unavailable);
        }

        var vaultedCard = token.PaymentSource?.Card;

        return new VaultedCard(
            token.Id,
            token.Customer?.Id,
            vaultedCard?.Brand?.Value,
            vaultedCard?.LastDigits,
            vaultedCard?.Expiry,
            vaultedCard?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
    {
        using var budget = Budget(cancellationToken, PaymentBudget);

        await InvokeAsync<bool, DeletePaymentTokenError>(
            "delete vaulted card",
            async ct =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
                return true;
            },
            static (DeletePaymentTokenError e, out Error v) => e.TryGetError(out v),
            noContent: null,
            budget.Token);
    }

    public async Task<GatewayTransactionPage> ListTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to - from > MaxSearchRange)
        {
            throw new PaymentGatewayException(
                "PayPal's transaction reporting only covers the previous three years; narrow the range.",
                PaymentGatewayFailure.Rejected);
        }

        using var budget = Budget(cancellationToken, ReportBudget);

        var transactions = new List<GatewayTransaction>();
        DateTimeOffset? lastRefreshed = null;

        // The provider caps one search at 31 days, so the requested range is walked window by
        // window — the report covers the whole range, not just its first window or first page.
        foreach (var (windowStart, windowEnd) in SplitIntoWindows(from, to))
        {
            var page = 1;
            var totalPages = 1;

            do
            {
                var pageNumber = page;
                var response = await InvokeRawAsync<SearchResponse>(
                    "search transactions",
                    ct => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatTimestamp(windowStart),
                        endDate: FormatTimestamp(windowEnd),
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
                        pageSize: SearchPageSize,
                        page: pageNumber,
                        ct: ct),
                    budget.Token);

                lastRefreshed = ParseTimestamp(response.LastRefreshedDatetime) ?? lastRefreshed;
                totalPages = response.TotalPages ?? 1;

                foreach (var detail in response.TransactionDetails ?? [])
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    transactions.Add(new GatewayTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        info.TransactionEventCode,
                        ParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseAmount(info.FeeAmount?.Value),
                        ParseTimestamp(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }

                page++;

                // Never let the loop depend solely on the provider agreeing to stop.
                if (page > MaxPagesPerWindow)
                {
                    _logger.LogWarning(
                        "Transaction search for {Start:u}..{End:u} hit the {MaxPages}-page cap; the report for " +
                        "that window is truncated.", windowStart, windowEnd, MaxPagesPerWindow);
                    break;
                }
            }
            while (page <= totalPages);
        }

        return new GatewayTransactionPage(transactions, lastRefreshed);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(
        DateTimeOffset from, DateTimeOffset to)
    {
        var cursor = from;
        while (cursor < to)
        {
            var end = cursor + MaxSearchWindow;
            if (end > to)
            {
                end = to;
            }

            yield return (cursor, end);
            cursor = end;
        }
    }

    private CardRequest BuildCard(PaymentInstrument instrument) => instrument switch
    {
        // Paying with a saved card sends the vault reference and nothing else — no card data is
        // held by this application to send even if we wanted to.
        PaymentInstrument.VaultToken vault => new CardRequest { VaultId = vault.VaultId },

        PaymentInstrument.OneOffCard oneOff => new CardRequest
        {
            Name = oneOff.Card.CardholderName,
            Number = oneOff.Card.Number,
            Expiry = oneOff.Card.Expiry,
            SecurityCode = oneOff.Card.SecurityCode,
            BillingAddress = BuildAddress(oneOff.Card.BillingAddress)
        },

        _ => throw new PaymentGatewayException(
            "A payment needs either card details or a saved card.", PaymentGatewayFailure.Rejected)
    };

    private static Address? BuildAddress(CardBillingAddress? address) =>
        address is null
            ? null
            : new Address
            {
                CountryCode = address.CountryCode,
                AddressLine1 = address.Line1,
                AddressLine2 = address.Line2,
                AdminArea2 = address.City,
                AdminArea1 = address.State,
                PostalCode = address.PostalCode
            };

    private delegate bool TryGetTypedError<in TError>(TError error, out Error value);

    private delegate bool TryGetRawSlot<in TError>(TError error, out RawError value);

    /// <summary>
    /// Runs one Case-A operation and converts every failure shape it can produce into a
    /// <see cref="PaymentGatewayException"/>. The typed accessors are passed in by the call site,
    /// because they live on the concrete per-operation error type and not on its base.
    /// </summary>
    private async Task<TResult> InvokeAsync<TResult, TError>(
        string operation,
        Func<CancellationToken, Task<TResult>> call,
        TryGetTypedError<TError> typed,
        TryGetRawSlot<TError>? noContent,
        CancellationToken cancellationToken)
        where TError : ApiError
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (SdkException<TError> ex)
        {
            if (typed(ex.Error, out var error))
            {
                throw Translate(operation, error, ex);
            }

            if (noContent is not null && noContent(ex.Error, out var empty))
            {
                throw new PaymentGatewayException(
                    $"PayPal returned an empty error response to '{operation}' (HTTP {(int)empty.StatusCode}).",
                    PaymentGatewayFailure.Unavailable, statusCode: empty.StatusCode, innerException: ex);
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                throw Translate(operation, raw, ex);
            }

            throw new PaymentGatewayException(
                $"PayPal returned an error to '{operation}' in a shape this integration does not recognise.",
                PaymentGatewayFailure.Unavailable, innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            // Either a 2xx whose body no longer matches the model, or an error body that did not
            // match its generated shape — in which case the status was destroyed with it. Both mean
            // the outcome of a write is unknown, which is not the same as "it failed".
            throw new PaymentGatewayException(
                $"PayPal returned a response to '{operation}' that could not be read. Whether it took effect " +
                "is unknown; reconcile before retrying.",
                PaymentGatewayFailure.OutcomeUnknown, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw TranslateTransport(operation, ex, cancellationToken);
        }
    }

    /// <summary>Runs one Case-B operation, whose only error shape is <see cref="RawError"/>.</summary>
    private async Task<TResult> InvokeRawAsync<TResult>(
        string operation,
        Func<CancellationToken, Task<TResult>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            return await call(cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, ex.Error, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException(
                $"PayPal returned a response to '{operation}' that could not be read.",
                PaymentGatewayFailure.OutcomeUnknown, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw TranslateTransport(operation, ex, cancellationToken);
        }
    }

    private PaymentGatewayException Translate(string operation, Error error, Exception inner)
    {
        // debug_id is the correlation id PayPal support asks for, so it always reaches our logs.
        _logger.LogError(inner,
            "PayPal rejected '{Operation}': {Name} — {Message} (debug_id {DebugId}).",
            operation, error.Name, error.Message, error.DebugId);

        var kind = error.Name switch
        {
            // Our credentials or our permissions — never something the caller can fix.
            "NOT_AUTHORIZED" or "AUTHENTICATION_FAILURE" or "PERMISSION_DENIED"
                => PaymentGatewayFailure.Unavailable,

            // The resource is not in a state that allows this operation.
            "UNPROCESSABLE_ENTITY" or "RESOURCE_NOT_FOUND" => PaymentGatewayFailure.Conflict,

            "INVALID_REQUEST" => PaymentGatewayFailure.Rejected,

            // An error name we have not mapped is an unknown, not the caller's fault.
            _ => PaymentGatewayFailure.Rejected
        };

        var detail = error.Details?.FirstOrDefault();
        var description = detail?.Description ?? error.Message;
        var issue = detail?.Issue;

        return new PaymentGatewayException(
            $"PayPal rejected the request to {operation}: {description}" +
            (issue is null ? string.Empty : $" ({issue})"),
            kind,
            providerCode: issue ?? error.Name,
            debugId: error.DebugId,
            innerException: inner);
    }

    private PaymentGatewayException Translate(string operation, RawError raw, Exception inner)
    {
        var status = raw.StatusCode;

        // ReadAsString is safe on any body; ReadAsJson would throw on the HTML a gateway can return.
        string body;
        try
        {
            body = raw.ReadAsString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read PayPal's error body for '{Operation}'.", operation);
            body = string.Empty;
        }

        _logger.LogError(inner, "PayPal returned HTTP {Status} to '{Operation}'.", (int)status, operation);

        var kind = (int)status switch
        {
            401 or 403 or 429 => PaymentGatewayFailure.Unavailable,
            404 or 409 or 422 => PaymentGatewayFailure.Conflict,
            >= 400 and < 500 => PaymentGatewayFailure.Rejected,
            _ => PaymentGatewayFailure.Unavailable
        };

        return new PaymentGatewayException(
            $"PayPal returned HTTP {(int)status} to the request to {operation}." +
            (string.IsNullOrWhiteSpace(body) ? string.Empty : $" {Truncate(body, 500)}"),
            kind,
            statusCode: status,
            innerException: inner);
    }

    private PaymentGatewayException TranslateTransport(string operation, Exception ex,
        CancellationToken cancellationToken)
    {
        _logger.LogError(ex, "PayPal was unreachable for '{Operation}'.", operation);

        // The bytes may have reached PayPal before the connection died, so a write's outcome is
        // genuinely unknown. Reads have nothing to reconcile, so they are simply unavailable.
        var kind = IsWrite(operation)
            ? PaymentGatewayFailure.OutcomeUnknown
            : PaymentGatewayFailure.Unavailable;

        var reason = cancellationToken.IsCancellationRequested
            ? "the request was cancelled or ran past its deadline"
            : "the payment processor could not be reached";

        return new PaymentGatewayException(
            $"The request to {operation} did not complete: {reason}." +
            (kind == PaymentGatewayFailure.OutcomeUnknown
                ? " Whether it took effect is unknown; reconcile before retrying."
                : string.Empty),
            kind, innerException: ex);
    }

    private static bool IsWrite(string operation) =>
        operation is not ("read authorization" or "search transactions");

    private static CancellationTokenSource Budget(CancellationToken cancellationToken, TimeSpan budget)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(budget);
        return source;
    }

    /// <summary>
    /// PayPal takes amounts as strings whose precision depends on the currency. Two decimals is
    /// right for the currencies this deployment uses; the caller re-checks the authorized amount
    /// against the order total afterwards, so a mismatch can never be silently accepted.
    /// </summary>
    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
