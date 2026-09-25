using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using OrdersReq = PayPalServerSdk.Requests.Orders;
using PaymentsReq = PayPalServerSdk.Requests.Payments;
using VaultReq = PayPalServerSdk.Requests.Vault;
using SearchReq = PayPalServerSdk.Requests.TransactionSearch;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The single boundary to PayPal, built on the PayPal Server SDK. Every provider/transport failure is
/// translated to a caller-safe <see cref="PaymentException"/> here; no SDK type escapes. Each public method
/// bounds the whole call (all its SDK round-trips) with a linked deadline — the SDK's own timeout is per
/// attempt, not a call budget.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;

    // Whole-call budgets (the CancellationToken is the only thing that bounds a whole call).
    private const int SingleCallBudgetSeconds = 30;
    private const int MultiCallBudgetSeconds = 60;   // authorize = create + authorize; fulfil = reauthorize + capture
    private const int SearchBudgetSeconds = 150;     // reconciliation walks every page

    public PayPalGateway(PayPalServerSdkClient client, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizationResult> AuthorizeOrderAsync(
        string invoiceId, decimal amount, string currencyCode, PaymentInstrument instrument,
        string idempotencyBase, CancellationToken ct) =>
        Run(MultiCallBudgetSeconds, "authorize order", async token =>
        {
            // 1) Create the order with intent=AUTHORIZE (hold, not capture). Deterministic request id → idempotent.
            var createReq = new OrdersReq.CreateOrderRequest
            {
                PayPalRequestId = $"create-{idempotencyBase}",
                Prefer = "return=minimal",
                Body = new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown { CurrencyCode = currencyCode, Value = Format(amount) },
                            InvoiceId = invoiceId,
                            CustomId = invoiceId
                        }
                    }
                }
            };
            var order = await _client.Orders.CreateOrder(createReq, cancellationToken: token);
            var payPalOrderId = order.Id
                ?? throw PaymentException.ProviderUnavailable("PayPal did not return an order id.");

            // 2) Authorize the order with the card / vaulted card as the payment source (browserless direct card).
            var authReq = new OrdersReq.AuthorizeOrderRequest
            {
                Id = payPalOrderId,
                PayPalRequestId = $"auth-{idempotencyBase}",
                Prefer = "return=representation",
                Body = new OrderAuthorizeRequest { PaymentSource = BuildPaymentSource(instrument) }
            };
            var authResp = await _client.Orders.AuthorizeOrder(authReq, cancellationToken: token);

            var authorization = authResp.PurchaseUnits?
                .SelectMany(pu => pu.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault(a => !string.IsNullOrEmpty(a.Id));

            if (authorization?.Id is null)
            {
                if (authResp.Status == OrderStatus.PayerActionRequired || HasPayerActionLink(authResp.Links))
                    throw new PaymentChallengeRequiredException(
                        "PayPal requires the shopper to approve this card payment in a browser (3-D Secure challenge). " +
                        "This integration does not support a browser approval round-trip.");

                throw PaymentException.ProviderUnavailable(
                    $"PayPal authorized order '{payPalOrderId}' without returning an authorization (status {authResp.Status?.Value ?? "unknown"}).");
            }

            return new AuthorizationResult(
                payPalOrderId,
                authorization.Id!,
                authorization.Status?.Value,
                ParseDate(authorization.ExpirationTime));
        }, ct);

    public Task<AuthorizationStatusInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "get authorization", async token =>
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                new PaymentsReq.GetAuthorizedPaymentRequest { AuthorizationId = authorizationId },
                cancellationToken: token);
            return new AuthorizationStatusInfo(auth.Status?.Value, ParseDate(auth.ExpirationTime));
        }, ct);

    public Task<RenewalResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "reauthorize payment", async token =>
        {
            var auth = await _client.Payments.ReauthorizePayment(new PaymentsReq.ReauthorizePaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = idempotencyKey,
                Prefer = "return=representation",
                Body = new ReauthorizeRequest { Amount = new Money { CurrencyCode = currencyCode, Value = Format(amount) } }
            }, cancellationToken: token);

            return new RenewalResult(auth.Id ?? authorizationId, auth.Status?.Value, ParseDate(auth.ExpirationTime));
        }, ct);

    public Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string invoiceId, string idempotencyKey, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "capture payment", async token =>
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(new PaymentsReq.CaptureAuthorizedPaymentRequest
            {
                AuthorizationId = authorizationId,
                PayPalRequestId = idempotencyKey,
                Prefer = "return=representation",
                Body = new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = currencyCode, Value = Format(amount) },
                    FinalCapture = true
                    // invoice_id intentionally omitted: PayPal reports the authorizing transaction's invoice_id,
                    // which we already stamped on the order, and re-sending it risks a duplicate-invoice-id error.
                }
            }, cancellationToken: token);

            var breakdown = capture.SellerReceivableBreakdown;
            var captured = ParseAmount(capture.Amount?.Value) ?? ParseAmount(breakdown?.GrossAmount?.Value) ?? amount;
            return new CaptureResult(
                capture.Id ?? throw PaymentException.ProviderUnavailable("PayPal did not return a capture id."),
                capture.Status?.Value,
                captured,
                ParseAmount(breakdown?.PaypalFee?.Value),
                ParseAmount(breakdown?.NetAmount?.Value));
        }, ct);

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "void authorization", async token =>
        {
            try
            {
                await _client.Payments.VoidPayment(new PaymentsReq.VoidPaymentRequest
                {
                    AuthorizationId = authorizationId,
                    PayPalRequestId = idempotencyKey
                }, cancellationToken: token);
            }
            catch (ResponseDeserializationException rde) when ((int)rde.StatusCode is >= 200 and < 300)
            {
                // Void succeeds with 204 No Content (empty body); the SDK's declared PaymentAuthorization
                // return type cannot map an empty body, so a 2xx deserialization failure here IS success.
            }
            return true;
        }, ct);

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "refund payment", async token =>
        {
            var refund = await _client.Payments.RefundCapturedPayment(new PaymentsReq.RefundCapturedPaymentRequest
            {
                CaptureId = captureId,
                PayPalRequestId = idempotencyKey,
                Prefer = "return=representation",
                // Empty body = full refund; an amount = partial refund.
                Body = amount.HasValue
                    ? new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = Format(amount.Value) } }
                    : new RefundRequest()
            }, cancellationToken: token);

            return new RefundResult(
                refund.Id ?? throw PaymentException.ProviderUnavailable("PayPal did not return a refund id."),
                refund.Status?.Value,
                ParseAmount(refund.Amount?.Value) ?? amount ?? 0m);
        }, ct);

    public Task<VaultCardResult> VaultCardAsync(string customerId, CardDetails card, string idempotencyKey, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "vault card", async token =>
        {
            var resp = await _client.Vault.CreatePaymentToken(new VaultReq.CreatePaymentTokenRequest
            {
                PayPalRequestId = idempotencyKey,
                Body = new PaymentTokenRequest
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
                }
            }, cancellationToken: token);

            var entity = resp.PaymentSource?.Card;
            return new VaultCardResult(
                resp.Id ?? throw PaymentException.ProviderUnavailable("PayPal did not return a vault token id."),
                entity?.Brand?.Value,
                entity?.LastDigits,
                entity?.Expiry ?? card.Expiry,
                entity?.Name ?? card.CardholderName);
        }, ct);

    public Task DeleteVaultCardAsync(string vaultId, CancellationToken ct) =>
        Run(SingleCallBudgetSeconds, "delete vaulted card", async token =>
        {
            await _client.Vault.DeletePaymentToken(new VaultReq.DeletePaymentTokenRequest { Id = vaultId }, cancellationToken: token);
            return true;
        }, ct);

    public Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, string currencyCode, int maxPages, CancellationToken ct) =>
        Run(SearchBudgetSeconds, "search transactions", async token =>
        {
            var startDate = FormatDate(from);
            var endDate = FormatDate(to);
            var transactions = new List<PayPalTransaction>();
            int page = 1;
            int totalPages = 1;
            int fetched = 0;

            do
            {
                var resp = await _client.TransactionSearch.SearchTransactions(new SearchReq.SearchTransactionsRequest
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    Fields = "transaction_info",
                    PageSize = 100,
                    Page = page
                }, cancellationToken: token);

                fetched++;
                totalPages = resp.TotalPages ?? 1;

                foreach (var detail in resp.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    transactions.Add(new PayPalTransaction(
                        info?.TransactionId,
                        info?.InvoiceId,
                        ParseAmount(info?.TransactionAmount?.Value),
                        info?.TransactionAmount?.CurrencyCode,
                        ParseAmount(info?.FeeAmount?.Value),
                        info?.TransactionStatus,
                        ParseDate(info?.TransactionInitiationDate)));
                }

                page++;
            }
            while (page <= totalPages && fetched < maxPages);

            var complete = fetched >= totalPages;
            if (!complete)
                _logger.LogWarning("Reconciliation stopped at page cap {MaxPages} of {TotalPages}; report is partial.", maxPages, totalPages);

            return new TransactionSearchResult(transactions, fetched, totalPages, complete);
        }, ct);

    // ----- helpers -----

    private static OrderAuthorizeRequestPaymentSource BuildPaymentSource(PaymentInstrument instrument)
    {
        if (!string.IsNullOrEmpty(instrument.VaultId))
            return new OrderAuthorizeRequestPaymentSource { Card = new CardRequest { VaultId = instrument.VaultId } };

        var card = instrument.Card
            ?? throw PaymentException.Validation("A card or a saved card must be supplied to pay.");

        return new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = BuildAddress(card)
            }
        };
    }

    private static Address? BuildAddress(CardDetails card)
    {
        var country = card.BillingCountryCode?.Trim();
        if (string.IsNullOrEmpty(country) || country!.Length != 2)
            return null; // billing address is optional; PayPal requires a 2-char country code when present

        return new Address
        {
            CountryCode = country.ToUpperInvariant(),
            AddressLine1 = NullIfEmpty(card.BillingStreet),
            AdminArea2 = NullIfEmpty(card.BillingCity),
            AdminArea1 = NullIfEmpty(card.BillingState),
            PostalCode = NullIfEmpty(card.BillingPostalCode)
        };
    }

    private static bool HasPayerActionLink(IReadOnlyList<LinkDescription>? links) =>
        links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(l.Rel, "approve", StringComparison.OrdinalIgnoreCase)) ?? false;

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Runs the SDK work under a whole-call deadline and translates every SDK failure into a caller-safe
    /// <see cref="PaymentException"/>. Application signals thrown inside (challenge, validation) propagate untouched.
    /// </summary>
    private async Task<T> Run<T>(int budgetSeconds, string op, Func<CancellationToken, Task<T>> body, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(budgetSeconds));
        try
        {
            return await body(cts.Token);
        }
        catch (PaymentException)
        {
            throw; // already a caller-safe application error
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "PayPal {Op} returned a body that could not be processed (HTTP {Status}).", op, (int)ex.StatusCode);
            throw new PaymentException(MapStatus((int)ex.StatusCode), $"PayPal returned a response for '{op}' that could not be processed.", ex);
        }
        catch (ApiException ex) // covers ApiException<TError> (typed and RawError) and the base
        {
            var (code, detail, debugId) = DescribeError(ex);
            // debug_id is PayPal's correlation id — the one thing that lets support trace the failure.
            _logger.LogError(ex, "PayPal {Op} failed: HTTP {Status} code={Code} debugId={DebugId} detail={Detail}",
                op, (int)ex.StatusCode, code, debugId, detail);
            var suffix = code is not null ? $" ({code})" : string.Empty;
            throw new PaymentException(MapStatus((int)ex.StatusCode), $"PayPal rejected the '{op}' request (HTTP {(int)ex.StatusCode}){suffix}.", ex);
        }
        catch (SdkTimeoutException ex)
        {
            _logger.LogError(ex, "PayPal {Op} timed out after {Timeout}.", op, ex.Timeout);
            throw PaymentException.ProviderUnavailable($"PayPal did not respond in time for '{op}'.", ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "PayPal {Op} could not reach the provider.", op);
            throw PaymentException.ProviderUnavailable($"PayPal could not be reached for '{op}'.", ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "PayPal {Op} could not apply credentials.", op);
            throw PaymentException.ProviderUnavailable($"PayPal credentials were rejected for '{op}'.", ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our own budget elapsed (not the caller aborting).
            _logger.LogError("PayPal {Op} exceeded the {Budget}s call budget.", op, budgetSeconds);
            throw PaymentException.ProviderUnavailable($"PayPal did not complete '{op}' within {budgetSeconds}s.");
        }
    }

    /// <summary>
    /// Reads PayPal's error name, message and correlation id (debug_id) from a thrown exception. The typed
    /// accessors live on each operation's concrete error type, so this pattern-matches the ones this gateway calls.
    /// </summary>
    private static (string? Code, string? Detail, string? DebugId) DescribeError(ApiException ex)
    {
        Error? e = ex switch
        {
            ApiException<CreateOrderError> x when x.Error.TryGetError(out var err) => err,
            ApiException<AuthorizeOrderError> x when x.Error.TryGetError(out var err) => err,
            ApiException<GetOrderError> x when x.Error.TryGetError(out var err) => err,
            ApiException<CaptureAuthorizedPaymentError> x when x.Error.TryGetError(out var err) => err,
            ApiException<ReauthorizePaymentError> x when x.Error.TryGetError(out var err) => err,
            ApiException<VoidPaymentError> x when x.Error.TryGetError(out var err) => err,
            ApiException<RefundCapturedPaymentError> x when x.Error.TryGetError(out var err) => err,
            ApiException<GetAuthorizedPaymentError> x when x.Error.TryGetError(out var err) => err,
            ApiException<CreatePaymentTokenError> x when x.Error.TryGetError(out var err) => err,
            ApiException<DeletePaymentTokenError> x when x.Error.TryGetError(out var err) => err,
            _ => null
        };
        if (e is not null)
            return (e.Name, e.Message, e.DebugId);

        // Case B (SearchTransactions) or an untyped status: read the raw body directly.
        if (ex is ApiException<RawError> raw)
        {
            try { return (null, raw.Error.ReadAsString(), null); }
            catch { return (null, null, null); }
        }
        return (null, null, null);
    }

    /// <summary>A provider 4xx the caller can act on stays a 4xx; our-credential/quota and transport failures become 502.</summary>
    private static int MapStatus(int providerStatus) => providerStatus switch
    {
        401 or 403 or 429 => 502, // our credential / our quota — not the caller's fault
        >= 400 and < 500 => providerStatus, // caller-actionable rejection
        _ => 502
    };
}
