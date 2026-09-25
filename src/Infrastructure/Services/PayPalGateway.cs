using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
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

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The only seam that talks to PayPal. Wraps the PayPal Server SDK, denominates every amount in the
/// configured currency, and translates provider/transport failures into
/// <see cref="PaymentException"/>. Idempotency keys are supplied by callers and sent to PayPal as its
/// request id (<c>PayPal-Request-Id</c>) so a resend dedupes provider-side. Card details are passed
/// to PayPal and never persisted or logged here.
/// </summary>
public sealed class PayPalGateway : IPaymentGateway
{
    // Whole-call budget: the per-attempt SDK timeout only bounds one attempt, so the caller's
    // ceiling is a CancellationToken deadline enforced here.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private const int MaxReconciliationPages = 50;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string CurrencyCode => _settings.Currency;

    public Task<AuthorizationResult> AuthorizeAsync(PaymentAuthorizationRequest request, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            var reference = request.PaymentReference.ToString();
            var card = BuildCard(request);

            var order = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        Amount = new AmountWithBreakdown { CurrencyCode = _settings.Currency, Value = FormatAmount(request.Amount) },
                        CustomId = reference,
                        InvoiceId = reference,
                        Description = request.Description
                    }
                },
                PaymentSource = new PaymentSource { Card = card }
            };

            var created = await _client.Orders.CreateOrder(new CreateOrderRequest
            {
                Body = order,
                PayPalRequestId = $"create-{reference}",
                Prefer = "return=representation"
            }, cancellationToken: token);

            ThrowIfPayerActionRequired(created.Status);
            var payPalOrderId = created.Id
                ?? throw new PaymentException("PayPal did not return an order id.", HttpStatusCode.BadGateway);

            // With a card at create time PayPal may already have created the authorization; otherwise
            // authorize explicitly.
            var authz = FirstAuthorization(created.PurchaseUnits);
            if (authz?.Id is null)
            {
                var authResponse = await _client.Orders.AuthorizeOrder(new AuthorizeOrderRequest
                {
                    Id = payPalOrderId,
                    PayPalRequestId = $"auth-{reference}",
                    Prefer = "return=representation"
                }, cancellationToken: token);

                ThrowIfPayerActionRequired(authResponse.Status);
                authz = FirstAuthorization(authResponse.PurchaseUnits);
            }

            // If we still cannot see the authorization, re-read the order to settle the outcome.
            if (authz?.Id is null)
            {
                var reread = await _client.Orders.GetOrder(new GetOrderRequest { Id = payPalOrderId }, cancellationToken: token);
                authz = FirstAuthorization(reread.PurchaseUnits);
            }

            if (authz?.Id is null)
                throw new PaymentException("PayPal did not return an authorization for the order.", HttpStatusCode.BadGateway);

            var description = request.VaultId is not null
                ? "Saved card"
                : $"Card ending {Last4(request.Card?.Number)}";

            return new AuthorizationResult(payPalOrderId, authz.Id, authz.Status?.Value ?? "CREATED",
                ParseTime(authz.ExpirationTime), request.Amount, description);
        }, cancellationToken));

    public Task<AuthorizationLookup> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                new GetAuthorizedPaymentRequest { AuthorizationId = authorizationId }, cancellationToken: token);
            return new AuthorizationLookup(auth.Status?.Value ?? "UNKNOWN", ParseTime(auth.ExpirationTime));
        }, cancellationToken));

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            var body = new CaptureRequest { FinalCapture = true };
            if (amount is not null)
                body = body with { Amount = new Money { CurrencyCode = _settings.Currency, Value = FormatAmount(amount.Value) } };

            var capture = await _client.Payments.CaptureAuthorizedPayment(new CaptureAuthorizedPaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = idempotencyKey,
                Prefer = "return=representation",
                Body = body
            }, cancellationToken: token);

            var breakdown = capture.SellerReceivableBreakdown;
            return new CaptureResult(
                capture.Id ?? throw new PaymentException("PayPal did not return a capture id.", HttpStatusCode.BadGateway),
                capture.Status?.Value ?? "COMPLETED",
                ParseAmount(capture.Amount) ?? amount ?? 0m,
                ParseAmount(breakdown?.PaypalFee),
                ParseAmount(breakdown?.NetAmount));
        }, cancellationToken));

    public Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey, decimal amount, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            var reauth = await _client.Payments.ReauthorizePayment(new ReauthorizePaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = idempotencyKey,
                Body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = _settings.Currency, Value = FormatAmount(amount) } }
            }, cancellationToken: token);

            return new AuthorizationResult(string.Empty,
                reauth.Id ?? throw new PaymentException("PayPal did not return a reauthorization id.", HttpStatusCode.BadGateway),
                reauth.Status?.Value ?? "CREATED", ParseTime(reauth.ExpirationTime), amount, null);
        }, cancellationToken));

    public Task<VoidResult> VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            try
            {
                var voided = await _client.Payments.VoidPayment(new VoidPaymentRequest
                {
                    AuthorizationId = authorizationId,
                    PayPalRequestId = idempotencyKey,
                    Prefer = "return=representation"
                }, cancellationToken: token);
                return new VoidResult(voided.Status?.Value ?? "VOIDED");
            }
            catch (ResponseDeserializationException ex) when ((int)ex.StatusCode is >= 200 and < 300)
            {
                // A successful void returns 204 No Content; the SDK cannot deserialize the empty body.
                // The hold has been released — that is success.
                return new VoidResult("VOIDED");
            }
        }, cancellationToken));

    public Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, decimal? amount, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            var body = new RefundRequest();
            if (amount is not null)
                body = body with { Amount = new Money { CurrencyCode = _settings.Currency, Value = FormatAmount(amount.Value) } };

            var refund = await _client.Payments.RefundCapturedPayment(new RefundCapturedPaymentRequest
            {
                CaptureId = captureId,
                PayPalRequestId = idempotencyKey,
                Prefer = "return=representation",
                Body = body
            }, cancellationToken: token);

            return new RefundResult(
                refund.Id ?? throw new PaymentException("PayPal did not return a refund id.", HttpStatusCode.BadGateway),
                refund.Status?.Value ?? "COMPLETED",
                ParseAmount(refund.Amount) ?? amount ?? 0m);
        }, cancellationToken));

    public Task<VaultResult> VaultCardAsync(VaultCardRequest request, string idempotencyKeyBase, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            // Server-side (no-browser) card vaulting: create a setup token from the raw card with a
            // verification method, then exchange it for a permanent payment token (the vault id).
            var setup = await _client.Vault.CreateSetupToken(new CreateSetupTokenRequest
            {
                PayPalRequestId = $"setup-{idempotencyKeyBase}",
                Body = new SetupTokenRequest
                {
                    PaymentSource = new SetupTokenRequestPaymentSource { Card = BuildSetupCard(request.Card) }
                }
            }, cancellationToken: token);

            ThrowIfSetupPayerAction(setup.Status);
            var setupTokenId = setup.Id
                ?? throw new PaymentException("PayPal did not return a setup token id.", HttpStatusCode.BadGateway);

            var tokenResponse = await _client.Vault.CreatePaymentToken(new CreatePaymentTokenRequest
            {
                PayPalRequestId = $"token-{idempotencyKeyBase}",
                Body = new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }
                    }
                }
            }, cancellationToken: token);

            var vaultId = tokenResponse.Id
                ?? throw new PaymentException("PayPal did not return a vault id.", HttpStatusCode.BadGateway);
            var cardEntity = tokenResponse.PaymentSource?.Card;
            return new VaultResult(vaultId, cardEntity?.Brand?.Value, cardEntity?.LastDigits, cardEntity?.Expiry);
        }, cancellationToken));

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken)
        => Guarded(() => Bounded<object?>(async token =>
        {
            await _client.Vault.DeletePaymentToken(new DeletePaymentTokenRequest { Id = vaultId }, cancellationToken: token);
            return null;
        }, cancellationToken));

    public Task<ReconciliationTransactions> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        => Guarded(() => Bounded(async token =>
        {
            var start = FormatInstant(from);
            var end = FormatInstant(to);

            var transactions = new List<ReconciliationTransaction>();
            int page = 1;
            int? totalPages = null;
            bool truncated = false;

            while (true)
            {
                var response = await _client.TransactionSearch.SearchTransactions(new SearchTransactionsRequest
                {
                    StartDate = start,
                    EndDate = end,
                    TransactionCurrency = _settings.Currency,
                    Fields = "transaction_info",
                    PageSize = 500,
                    Page = page
                }, cancellationToken: token);

                totalPages = response.TotalPages;
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    transactions.Add(new ReconciliationTransaction(
                        info?.TransactionId,
                        info?.TransactionStatus,
                        ParseAmount(info?.TransactionAmount),
                        info?.TransactionAmount?.CurrencyCode,
                        info?.InvoiceId,
                        info?.CustomField));
                }

                if (totalPages is null || page >= totalPages) break;
                if (page >= MaxReconciliationPages) { truncated = true; break; }
                page++;
            }

            return new ReconciliationTransactions(transactions, truncated, page, totalPages);
        }, cancellationToken));

    // --- helpers ---

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(a => a.Id is not null);

    private static void ThrowIfPayerActionRequired(OrderStatus? status)
    {
        if (status == OrderStatus.PayerActionRequired)
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (payer action required). " +
                "This integration does not support a browser approval round-trip.");
    }

    private CardRequest? BuildCard(PaymentAuthorizationRequest request)
    {
        if (request.VaultId is not null)
            return new CardRequest { VaultId = request.VaultId };
        if (request.Card is null) return null;

        var c = request.Card;
        return new CardRequest
        {
            Number = c.Number,
            Expiry = c.Expiry,
            SecurityCode = c.SecurityCode,
            Name = c.Name,
            BillingAddress = BuildAddress(c)
        };
    }

    // Name and billing address are safe (the same fields succeed on the pay flow). A verification
    // method (SCA) and a customer id are deliberately omitted: SCA drives a browser-approval path this
    // integration does not support, and card ownership is enforced by our own SavedPaymentMethod store
    // rather than by PayPal customer grouping.
    private static SetupTokenRequestCard BuildSetupCard(CardDetails c) => new()
    {
        Number = c.Number,
        Expiry = c.Expiry,
        SecurityCode = c.SecurityCode,
        Name = c.Name,
        BillingAddress = BuildAddress(c)
    };

    private static void ThrowIfSetupPayerAction(PaymentTokenStatus? status)
    {
        if (status == PaymentTokenStatus.PayerActionRequired)
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve saving this card in a browser (payer action required). " +
                "This integration does not support a browser approval round-trip.");
    }

    private static Address? BuildAddress(CardDetails c)
    {
        // PayPal requires a country code on an address; only build one when we have it.
        if (string.IsNullOrWhiteSpace(c.BillingCountryCode)) return null;
        return new Address
        {
            AddressLine1 = c.BillingAddressLine1,
            AdminArea1 = c.BillingAdminArea1,
            AdminArea2 = c.BillingAdminArea2,
            PostalCode = c.BillingPostalCode,
            CountryCode = c.BillingCountryCode!
        };
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(Money? money) =>
        money?.Value is { } v && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        value is not null && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var t) ? t : null;

    private static string FormatInstant(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Last4(string? number) =>
        string.IsNullOrEmpty(number) || number!.Length < 4 ? "****" : number[^4..];

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<T> Guarded<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception ex)
        {
            var mapped = Translate(ex);
            if (mapped is not null)
            {
                // Observability: log the provider's status, error name and correlation id (debug_id).
                // Never any request/card data. This is the only place the provider's own id is captured.
                _logger.LogWarning("PayPal call failed: status={Status} code={Code} debugId={DebugId} message={Message}",
                    mapped.StatusCode, mapped.ProviderCode, mapped.DebugId, mapped.Message);
                throw mapped;
            }
            throw; // PaymentException we raised, caller cancellation, argument errors — leave unwrapped
        }
    }

    /// <summary>
    /// Converts SDK failures to <see cref="PaymentException"/>, carrying the provider status and
    /// (for typed errors) PayPal's error name and debug id. Returns null for exceptions that must
    /// propagate unchanged (our own PaymentException, caller cancellation, argument errors).
    /// </summary>
    private static PaymentException? Translate(Exception ex) => ex switch
    {
        ApiException<CreateOrderError> e => FromTyped(e, Typed(e.Error)),
        ApiException<AuthorizeOrderError> e => FromTyped(e, Typed(e.Error)),
        ApiException<GetOrderError> e => FromTyped(e, Typed(e.Error)),
        ApiException<CaptureAuthorizedPaymentError> e => FromTyped(e, Typed(e.Error)),
        ApiException<GetAuthorizedPaymentError> e => FromTyped(e, Typed(e.Error)),
        ApiException<ReauthorizePaymentError> e => FromTyped(e, Typed(e.Error)),
        ApiException<VoidPaymentError> e => FromTyped(e, Typed(e.Error)),
        ApiException<RefundCapturedPaymentError> e => FromTyped(e, Typed(e.Error)),
        ApiException<CreateSetupTokenError> e => FromTyped(e, Typed(e.Error)),
        ApiException<CreatePaymentTokenError> e => FromTyped(e, Typed(e.Error)),
        ApiException<DeletePaymentTokenError> e => FromTyped(e, Typed(e.Error)),
        ApiException<RawError> e => new PaymentException(
            $"PayPal returned {(int)e.StatusCode} ({e.StatusCode}).", e.StatusCode, inner: e),
        ResponseDeserializationException e => new PaymentException(
            "PayPal returned a response that could not be processed.", e.StatusCode, inner: e),
        AuthSchemeException e => new PaymentException(
            "PayPal credentials were rejected.", HttpStatusCode.BadGateway, inner: e),
        SdkTimeoutException e => new PaymentException(
            $"PayPal did not respond within {e.Timeout}.", inner: e),
        SdkConnectionException e => new PaymentException(
            "PayPal is unreachable.", inner: e),
        ApiException e => new PaymentException(
            $"PayPal returned {(int)e.StatusCode} ({e.StatusCode}).", e.StatusCode, inner: e),
        _ => null
    };

    // Reads the shared typed Error body from a Case-A error, when present.
    private static Error? Typed(ApiError apiError) =>
        TryGetError(apiError, out var e) ? e : null;

    private static PaymentException FromTyped<T>(ApiException<T> ex, Error? typed) where T : ApiError =>
        new(typed?.Message ?? $"PayPal returned {(int)ex.StatusCode} ({ex.StatusCode}).",
            ex.StatusCode, typed?.Name, typed?.DebugId, ex);

    // CreateOrderError's TryGetError is used directly above; for the rest we access via the generic
    // helper so the switch stays one line each. Each {Operation}Error exposes TryGetError(out Error).
    private static bool TryGetError(ApiError apiError, out Error? error)
    {
        error = null;
        return apiError switch
        {
            CreateOrderError e when e.TryGetError(out var x) => Set(out error, x),
            AuthorizeOrderError e when e.TryGetError(out var x) => Set(out error, x),
            GetOrderError e when e.TryGetError(out var x) => Set(out error, x),
            CaptureAuthorizedPaymentError e when e.TryGetError(out var x) => Set(out error, x),
            GetAuthorizedPaymentError e when e.TryGetError(out var x) => Set(out error, x),
            ReauthorizePaymentError e when e.TryGetError(out var x) => Set(out error, x),
            VoidPaymentError e when e.TryGetError(out var x) => Set(out error, x),
            RefundCapturedPaymentError e when e.TryGetError(out var x) => Set(out error, x),
            CreateSetupTokenError e when e.TryGetError(out var x) => Set(out error, x),
            CreatePaymentTokenError e when e.TryGetError(out var x) => Set(out error, x),
            DeletePaymentTokenError e when e.TryGetError(out var x) => Set(out error, x),
            _ => false
        };
    }

    private static bool Set(out Error? target, Error value)
    {
        target = value;
        return true;
    }
}
