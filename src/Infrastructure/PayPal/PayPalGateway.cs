using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Requests.Orders;
using PayPalServerSdk.Requests.Payments;
using PayPalServerSdk.Requests.TransactionSearch;
using PayPalServerSdk.Requests.Vault;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The only place the PayPal SDK is referenced. Translates the SDK's operations, models and error
/// taxonomy into the application's domain-facing types (<see cref="IPayPalGateway"/>) and into
/// <see cref="PaymentGatewayException"/>. Card details are never logged.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
        Currency = settings.Value.Currency;
    }

    public string Currency { get; }

    // ---- Flow 1: authorize -> capture / void / refund -------------------------------------

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(PayPalAuthorizeRequest request,
        CancellationToken cancellationToken)
    {
        // 1. Create the PayPal order with intent = AUTHORIZE. Deterministic PayPal-Request-Id so a
        //    double-click reuses the same PayPal order rather than creating a second.
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = "default",
                    CustomId = request.OrderReference,
                    InvoiceId = request.InvoiceId,
                    Description = Truncate(request.Description, 127),
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = MoneyFormatter.Format(request.Amount, request.Currency),
                    },
                },
            },
        };

        var created = await RunAsync<Order, CreateOrderError>(
            c => _client.Orders.CreateOrder(new CreateOrderRequest
            {
                Body = orderRequest,
                PayPalRequestId = request.IdempotencyKey + "-create",
                Prefer = "return=minimal",
            }, cancellationToken: c),
            e => e.TryGetError(out var er) ? er : null,
            "create-order", isWrite: true, cancellationToken).ConfigureAwait(false);

        var payPalOrderId = created.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id.");

        // 2. Authorize (place the hold) with the payment source (one-off card or saved vault id).
        var paymentSource = BuildAuthorizePaymentSource(request);
        OrderAuthorizeResponse authorized;
        try
        {
            authorized = await _client.Orders.AuthorizeOrder(new AuthorizeOrderRequest
            {
                Id = payPalOrderId,
                Body = new OrderAuthorizeRequest { PaymentSource = paymentSource },
                PayPalRequestId = request.IdempotencyKey + "-authorize",
                Prefer = "return=representation",
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException<AuthorizeOrderError> ex)
        {
            Error? typed = ex.Error.TryGetError(out var er) ? er : null;
            RawError? raw = null;
            if (typed is null) ex.Error.TryGetRawError(out raw);
            throw FromApi("authorize-order", ex.StatusCode, typed, raw, ex);
        }
        catch (Exception ex) when (ex is SdkConnectionException or SdkTimeoutException or ResponseDeserializationException)
        {
            // The hold may still have been placed — re-read the order to settle the outcome.
            var settled = await TrySettleAuthorizationAsync(payPalOrderId, cancellationToken).ConfigureAwait(false);
            if (settled is not null) return settled;
            throw new PaymentGatewayException(
                "PayPal did not confirm the authorization; its outcome is unknown.",
                null, null, outcomeUnknown: true, operatorActionable: false, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentGatewayException("PayPal credentials were rejected.", null, null, false, false, ex);
        }

        return ReadAuthorization(payPalOrderId, authorized);
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var captured = await RunAsync<CapturedPayment, CaptureAuthorizedPaymentError>(
            c => _client.Payments.CaptureAuthorizedPayment(new CaptureAuthorizedPaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = idempotencyKey,
                Prefer = "return=representation",
                Body = new CaptureRequest { FinalCapture = true },
            }, cancellationToken: c),
            e => e.TryGetError(out var er) ? er : null,
            "capture", isWrite: true, cancellationToken).ConfigureAwait(false);

        return ReadCapture(captured);
    }

    public async Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(string authorizationId,
        string currency, decimal amount, CancellationToken cancellationToken)
    {
        var reauth = await RunAsync<PaymentAuthorization, ReauthorizePaymentError>(
            c => _client.Payments.ReauthorizePayment(new ReauthorizePaymentRequest
            {
                AuthorizationId = authorizationId,
                Prefer = "return=representation",
                Body = new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = MoneyFormatter.Format(amount, currency) },
                },
            }, cancellationToken: c),
            e => e.TryGetError(out var er) ? er : null,
            "reauthorize", isWrite: true, cancellationToken).ConfigureAwait(false);

        return new PayPalAuthorizationSnapshot(
            reauth.Id ?? authorizationId,
            reauth.Status?.Value ?? "UNKNOWN",
            ParseDate(reauth.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        await RunAsync<PaymentAuthorization, VoidPaymentError>(
            c => _client.Payments.VoidPayment(new VoidPaymentRequest { AuthorizationId = authorizationId },
                cancellationToken: c),
            e => e.TryGetError(out var er) ? er : null,
            "void", isWrite: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount,
        string currency, string payPalRequestId, CancellationToken cancellationToken)
    {
        var body = amount is decimal a
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = MoneyFormatter.Format(a, currency) } }
            : new RefundRequest();

        var refund = await RunAsync<Refund, RefundCapturedPaymentError>(
            c => _client.Payments.RefundCapturedPayment(new RefundCapturedPaymentRequest
            {
                CaptureId = captureId,
                PayPalRequestId = payPalRequestId,
                Prefer = "return=representation",
                Body = body,
            }, cancellationToken: c),
            e => e.TryGetError(out var er) ? er : null,
            "refund", isWrite: true, cancellationToken).ConfigureAwait(false);

        return new PayPalRefundResult(
            refund.Id ?? "",
            refund.Status?.Value ?? "UNKNOWN",
            MoneyFormatter.Parse(refund.Amount?.Value) ?? amount ?? 0m,
            refund.Amount?.CurrencyCode ?? currency);
    }

    // ---- Flow 2: vault -------------------------------------------------------------------

    public async Task<PayPalVaultedCard> VaultCardAsync(PayPalVaultCardRequest request,
        CancellationToken cancellationToken)
    {
        var body = new PaymentTokenRequest
        {
            Customer = request.ExistingPayPalCustomerId is not null
                ? new Customer { Id = request.ExistingPayPalCustomerId }
                : null,
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    Name = request.Card.CardholderName,
                    BillingAddress = ToAddress(request.Card.BillingAddress),
                },
            },
        };

        var token = await RunAsync<PaymentTokenResponse, CreatePaymentTokenError>(
            c => _client.Vault.CreatePaymentToken(new CreatePaymentTokenRequest { Body = body },
                cancellationToken: c),
            e => e.TryGetError(out var er) ? er : null,
            "vault-card", isWrite: true, cancellationToken).ConfigureAwait(false);

        return ReadVaultedCard(token)
            ?? throw new PaymentGatewayException("PayPal did not return a saved-card token.");
    }

    public async Task<IReadOnlyList<PayPalVaultedCard>> ListVaultedCardsAsync(string payPalCustomerId,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalVaultedCard>();
        const int maxPages = 10; // ListCustomerPaymentTokens caps `page` at 10 (page_size max 5)
        int page = 1;
        while (page <= maxPages)
        {
            var pageNo = page;
            var resp = await RunAsync<CustomerVaultPaymentTokensResponse, ListCustomerPaymentTokensError>(
                c => _client.Vault.ListCustomerPaymentTokens(new ListCustomerPaymentTokensRequest
                {
                    CustomerId = payPalCustomerId,
                    PageSize = 5,
                    Page = pageNo,
                    TotalRequired = true,
                }, cancellationToken: c),
                e => e.TryGetError(out var er) ? er : null,
                "list-vaulted-cards", isWrite: false, cancellationToken).ConfigureAwait(false);

            var tokens = resp.PaymentTokens ?? new List<PaymentTokenResponse>();
            foreach (var t in tokens)
            {
                // Only card tokens are saved cards in this integration.
                if (t.PaymentSource?.Card is null) continue;
                var card = ReadVaultedCard(t);
                if (card is not null) results.Add(card);
            }

            var totalPages = resp.TotalPages ?? 1;
            if (tokens.Count == 0 || page >= totalPages) break;
            page++;
        }
        return results;
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await RunVoidAsync<DeletePaymentTokenError>(
                c => _client.Vault.DeletePaymentToken(new DeletePaymentTokenRequest { Id = paymentTokenId },
                    cancellationToken: c),
                e => e.TryGetError(out var er) ? er : null,
                "delete-vaulted-card", isWrite: true, cancellationToken).ConfigureAwait(false);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
        {
            // Already gone at PayPal — treat as success so our record can be removed.
            _logger.LogWarning("Vault token {TokenId} not found at PayPal on delete; treating as already removed.", paymentTokenId);
        }
    }

    // ---- Reconciliation ------------------------------------------------------------------

    public async Task<PayPalReconciliationResult> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var all = new List<PayPalTransaction>();
        bool complete = true;
        int windows = 0, pagesRetrieved = 0;
        const int maxPagesPerWindow = 1000; // safety backstop against a non-advancing provider

        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(31); // SearchTransactions supports at most 31 days
            if (windowEnd > to) windowEnd = to;
            windows++;

            int page = 1, totalPages = 1;
            do
            {
                var pageNo = page;
                SearchResponse resp;
                try
                {
                    resp = await _client.TransactionSearch.SearchTransactions(new SearchTransactionsRequest
                    {
                        StartDate = FormatIso(windowStart),
                        EndDate = FormatIso(windowEnd),
                        Fields = "transaction_info",
                        BalanceAffectingRecordsOnly = "N",
                        PageSize = 500,
                        Page = pageNo,
                    }, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (ApiException<RawError> ex)
                {
                    _logger.LogError(ex,
                        "PayPal transaction search failed for {Start}..{End} page {Page}: HTTP {Status} {Body}",
                        windowStart, windowEnd, pageNo, (int)ex.StatusCode, ex.Error.ReadAsString());
                    complete = false;
                    break;
                }
                catch (Exception ex) when (ex is SdkConnectionException or SdkTimeoutException or ResponseDeserializationException)
                {
                    _logger.LogError(ex, "PayPal transaction search transport failure for {Start}..{End} page {Page}",
                        windowStart, windowEnd, pageNo);
                    complete = false;
                    break;
                }

                pagesRetrieved++;
                foreach (var d in resp.TransactionDetails ?? new List<TransactionDetails>())
                {
                    var info = d.TransactionInfo;
                    if (info is null) continue;
                    all.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionStatus,
                        MoneyFormatter.Parse(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionEventCode,
                        ParseDate(info.TransactionInitiationDate)));
                }

                totalPages = resp.TotalPages ?? 1;
                page++;
            } while (page <= totalPages && page <= maxPagesPerWindow);

            windowStart = windowEnd;
        }

        return new PayPalReconciliationResult(all, complete, windows, pagesRetrieved);
    }

    // ---- Re-read helpers (settle unknown outcomes) ---------------------------------------

    public async Task<PayPalAuthorizationSnapshot?> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var auth = await RunAsync<PaymentAuthorization, GetAuthorizedPaymentError>(
                c => _client.Payments.GetAuthorizedPayment(new GetAuthorizedPaymentRequest
                {
                    AuthorizationId = authorizationId,
                }, cancellationToken: c),
                e => e.TryGetError(out var er) ? er : null,
                "get-authorization", isWrite: false, cancellationToken).ConfigureAwait(false);
            return new PayPalAuthorizationSnapshot(auth.Id ?? authorizationId,
                auth.Status?.Value ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404) { return null; }
    }

    public async Task<PayPalCaptureResult?> GetCaptureAsync(string captureId,
        CancellationToken cancellationToken)
    {
        try
        {
            var cap = await RunAsync<CapturedPayment, GetCapturedPaymentError>(
                c => _client.Payments.GetCapturedPayment(new GetCapturedPaymentRequest { CaptureId = captureId },
                    cancellationToken: c),
                e => e.TryGetError(out var er) ? er : null,
                "get-capture", isWrite: false, cancellationToken).ConfigureAwait(false);
            return ReadCapture(cap);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404) { return null; }
    }

    public async Task<PayPalOrderSnapshot?> GetOrderAsync(string payPalOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await RunAsync<Order, GetOrderError>(
                c => _client.Orders.GetOrder(new GetOrderRequest { Id = payPalOrderId }, cancellationToken: c),
                e => e.TryGetError(out var er) ? er : null,
                "get-order", isWrite: false, cancellationToken).ConfigureAwait(false);

            var auth = order.PurchaseUnits?
                .SelectMany(p => p.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
                .FirstOrDefault();
            var cap = order.PurchaseUnits?
                .SelectMany(p => p.Payments?.Captures ?? new List<OrdersCapture>())
                .FirstOrDefault();

            PayPalCaptureResult? capture = cap?.Id is null
                ? null
                : new PayPalCaptureResult(
                    cap.Id,
                    cap.Status?.Value ?? "UNKNOWN",
                    MoneyFormatter.Parse(cap.SellerReceivableBreakdown?.GrossAmount?.Value ?? cap.Amount?.Value) ?? 0m,
                    MoneyFormatter.Parse(cap.SellerReceivableBreakdown?.PaypalFee?.Value),
                    MoneyFormatter.Parse(cap.SellerReceivableBreakdown?.NetAmount?.Value),
                    cap.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode ?? cap.Amount?.CurrencyCode ?? Currency);

            return new PayPalOrderSnapshot(order.Id ?? payPalOrderId, order.Status?.Value ?? "UNKNOWN",
                auth?.Id, auth?.Status?.Value, ParseDate(auth?.ExpirationTime), capture);
        }
        catch (PaymentGatewayException ex) when (ex.StatusCode == 404) { return null; }
    }

    // ---- helpers -------------------------------------------------------------------------

    private PayPalAuthorizationResult ReadAuthorization(string payPalOrderId, OrderAuthorizeResponse response)
    {
        // A browser approval (payer action / 3-D Secure challenge) is out of scope — stop & report,
        // never build an approval round-trip.
        if (response.Status is not null && response.Status == OrderStatus.PayerActionRequired)
            throw new PaymentGatewayException(
                "PayPal requires the shopper to approve this payment in a browser (payer action / 3-D Secure). " +
                "This integration does not perform browser approval.",
                402, null, false, operatorActionable: true);

        var auth = response.PurchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (auth?.Id is null)
            throw new PaymentGatewayException(
                $"PayPal did not return an authorization for order {payPalOrderId} (status {response.Status?.Value}).",
                422);

        var status = auth.Status?.Value ?? "UNKNOWN";
        if (status == AuthorizationStatus.Denied.Value)
            throw new PaymentGatewayException("The card was declined by PayPal.", 422);

        return new PayPalAuthorizationResult(payPalOrderId, auth.Id, status, ParseDate(auth.ExpirationTime));
    }

    private async Task<PayPalAuthorizationResult?> TrySettleAuthorizationAsync(string payPalOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var snap = await GetOrderAsync(payPalOrderId, cancellationToken).ConfigureAwait(false);
            if (snap?.AuthorizationId is not null)
                return new PayPalAuthorizationResult(payPalOrderId, snap.AuthorizationId,
                    snap.AuthorizationStatus ?? "UNKNOWN", snap.ExpiresAt);
        }
        catch
        {
            // Re-read failed; the outcome remains unknown to the caller.
        }
        return null;
    }

    private static OrderAuthorizeRequestPaymentSource BuildAuthorizePaymentSource(PayPalAuthorizeRequest request)
    {
        CardRequest card = request.VaultId is not null
            ? new CardRequest { VaultId = request.VaultId }
            : new CardRequest
            {
                Number = request.Card!.Number,
                Expiry = request.Card.Expiry,
                SecurityCode = request.Card.SecurityCode,
                Name = request.Card.CardholderName,
                BillingAddress = ToAddress(request.Card.BillingAddress),
            };
        return new OrderAuthorizeRequestPaymentSource { Card = card };
    }

    private static Address? ToAddress(PayPalBillingAddress? a)
    {
        if (a?.CountryCode is null) return null; // CountryCode is required on the SDK Address
        return new Address
        {
            AddressLine1 = a.AddressLine1,
            AdminArea2 = a.AdminArea2,
            AdminArea1 = a.AdminArea1,
            PostalCode = a.PostalCode,
            CountryCode = a.CountryCode,
        };
    }

    private static PayPalVaultedCard? ReadVaultedCard(PaymentTokenResponse token)
    {
        if (token.Id is null) return null;
        var card = token.PaymentSource?.Card;
        return new PayPalVaultedCard(token.Id, token.Customer?.Id,
            card?.Brand?.Value, card?.LastDigits, card?.Expiry, card?.Name);
    }

    private static PayPalCaptureResult ReadCapture(CapturedPayment captured)
    {
        var breakdown = captured.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            captured.Id ?? "",
            captured.Status?.Value ?? "UNKNOWN",
            MoneyFormatter.Parse(breakdown?.GrossAmount?.Value ?? captured.Amount?.Value) ?? 0m,
            MoneyFormatter.Parse(breakdown?.PaypalFee?.Value),
            MoneyFormatter.Parse(breakdown?.NetAmount?.Value),
            breakdown?.GrossAmount?.CurrencyCode ?? captured.Amount?.CurrencyCode ?? "");
    }

    /// <summary>
    /// Runs a Case-A SDK call, translating its error taxonomy into a single domain exception.
    /// Handles typed API errors, drifted/malformed bodies, transport failures, timeouts and auth
    /// failures. For a write, a transport failure is reported as an <em>unknown</em> outcome so the
    /// caller can settle it by re-reading provider state.
    /// </summary>
    private async Task<TResp> RunAsync<TResp, TError>(
        Func<CancellationToken, Task<TResp>> call,
        Func<TError, Error?> readTyped,
        string operation, bool isWrite, CancellationToken cancellationToken)
        where TError : ApiError
    {
        try
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException<TError> ex)
        {
            Error? typed = readTyped(ex.Error);
            RawError? raw = null;
            if (typed is null) ex.Error.TryGetRawError(out raw);
            throw FromApi(operation, ex.StatusCode, typed, raw, ex);
        }
        catch (ResponseDeserializationException ex)
        {
            var status = (int)ex.StatusCode;
            bool unknown = isWrite && status is >= 200 and < 300;
            _logger.LogError(ex, "PayPal {Operation} returned an unreadable response (HTTP {Status}).", operation, status);
            throw new PaymentGatewayException(
                "PayPal returned a response that could not be processed.", status, null, unknown, false, ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} timed out.", operation);
            throw new PaymentGatewayException("PayPal did not respond in time.", null, null, isWrite, false, ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} could not reach the provider.", operation);
            throw new PaymentGatewayException("PayPal is currently unreachable.", null, null, isWrite, false, ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal {Operation} credentials could not be applied.", operation);
            throw new PaymentGatewayException("PayPal credentials were rejected.", null, null, false, false, ex);
        }
    }

    private async Task RunVoidAsync<TError>(
        Func<CancellationToken, Task> call,
        Func<TError, Error?> readTyped,
        string operation, bool isWrite, CancellationToken cancellationToken)
        where TError : ApiError
    {
        await RunAsync<bool, TError>(async c => { await call(c).ConfigureAwait(false); return true; },
            readTyped, operation, isWrite, cancellationToken).ConfigureAwait(false);
    }

    private PaymentGatewayException FromApi(string operation, System.Net.HttpStatusCode status,
        Error? typed, RawError? raw, Exception ex)
    {
        var code = (int)status;
        var debugId = typed?.DebugId;
        var providerName = typed?.Name;
        var issues = typed?.Details is { Count: > 0 }
            ? string.Join("; ", typed.Details.Select(d => $"{d.Issue}:{d.Description}"))
            : (raw is not null ? raw.ReadAsString() : "(none)");
        _logger.LogError(ex, "PayPal {Operation} failed: HTTP {Status} {Name} debug_id={DebugId} issues={Issues}",
            operation, code, providerName ?? "(none)", debugId ?? "(none)", issues);
        var caller = providerName is null
            ? $"PayPal {operation} failed ({code})."
            : $"PayPal {operation} failed ({code}: {providerName}).";
        return new PaymentGatewayException(caller, code, debugId, false, false, ex);
    }

    private static string? Truncate(string? s, int max)
        => s is null || s.Length <= max ? s : s.Substring(0, max);

    private static DateTimeOffset? ParseDate(string? s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : (DateTimeOffset?)null;

    private static string FormatIso(DateTimeOffset dt)
        => dt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
