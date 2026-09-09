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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>. This is the single place any PayPal Server
/// SDK type is used; it translates SDK exceptions into <see cref="PaymentGatewayException"/> /
/// <see cref="PaymentChallengeRequiredException"/> so the rest of the app stays SDK-free. Card
/// details flow straight through to PayPal and are never persisted or logged here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const int MaxReconciliationPages = 200;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(
        PayPalServerSdkClient client,
        IOptions<PayPalSettings> settings,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string CurrencyCode => _settings.Currency;

    // ---------------------------------------------------------------- authorize

    public async Task<GatewayAuthorization> AuthorizeAsync(GatewayAuthorizeRequest request, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new()
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = CurrencyFormatter.Format(request.Amount, request.CurrencyCode)
                    },
                    InvoiceId = request.InvoiceId,
                    CustomId = request.CustomId,
                    Description = Truncate(request.Description, 127)
                }
            },
            PaymentSource = BuildPaymentSource(request)
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex) { throw Translate(ex.Error, ex); }
        catch (Exception ex) { throw TranslateCommon(ex); }

        var payPalOrderId = order.Id
            ?? throw new PaymentGatewayException("PayPal did not return an order id.");
        GuardAgainstChallenge(order.Status?.Value, order.Links);

        var authorization = ExtractAuthorization(order.PurchaseUnits);

        // Direct-card create with intent=AUTHORIZE may or may not create the authorization inline.
        // If it did not, authorize explicitly against the payment source already on the order.
        if (authorization is null)
        {
            OrderAuthorizeResponse authResp;
            try
            {
                authResp = await _client.Orders.AuthorizeOrder(
                    id: payPalOrderId,
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex) { throw Translate(ex.Error, ex); }
            catch (Exception ex) { throw TranslateCommon(ex); }

            GuardAgainstChallenge(authResp.Status?.Value, authResp.Links);
            authorization = ExtractAuthorization(authResp.PurchaseUnits);
        }

        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentGatewayException("PayPal did not return an authorization for the order.");
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentGatewayException("The card authorization was declined by PayPal.");
        }

        return new GatewayAuthorization(payPalOrderId, authorization.Id!, authorization.Status ?? string.Empty, authorization.ExpiresAt);
    }

    // ---------------------------------------------------------------- get authorization

    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var pa = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
            return new GatewayAuthorizationState(pa.Status?.Value ?? string.Empty, ParseTime(pa.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Translate(ex.Error, ex); }
        catch (Exception ex) { throw TranslateCommon(ex); }
    }

    // ---------------------------------------------------------------- reauthorize

    public async Task<GatewayAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        var body = new ReauthorizeRequest { Amount = Money(amount, currencyCode) };
        try
        {
            var pa = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
            return new GatewayAuthorization(string.Empty, pa.Id ?? authorizationId, pa.Status?.Value ?? string.Empty, ParseTime(pa.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw Translate(ex.Error, ex); }
        catch (Exception ex) { throw TranslateCommon(ex); }
    }

    // ---------------------------------------------------------------- capture

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = Money(amount, currencyCode),
            FinalCapture = true
        };

        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Translate(ex.Error, ex); }
        catch (Exception ex) { throw TranslateCommon(ex); }

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = CurrencyFormatter.TryParse(breakdown?.GrossAmount?.Value)
                    ?? CurrencyFormatter.TryParse(capture.Amount?.Value)
                    ?? amount;
        var fee = CurrencyFormatter.TryParse(breakdown?.PaypalFee?.Value);
        var net = CurrencyFormatter.TryParse(breakdown?.NetAmount?.Value);

        return new GatewayCapture(capture.Id ?? string.Empty, capture.Status?.Value ?? string.Empty, gross, fee, net, currencyCode);
    }

    // ---------------------------------------------------------------- void

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex) { throw Translate(ex.Error, ex); }
        catch (JsonException)
        {
            // A successful void returns 204 No Content; the empty body cannot be deserialized.
            // That is success, not failure — there is nothing to read back.
        }
        catch (Exception ex) { throw TranslateCommon(ex); }
    }

    // ---------------------------------------------------------------- refund

    public async Task<GatewayRefund> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct)
    {
        var body = amount.HasValue ? new RefundRequest { Amount = Money(amount.Value, currencyCode) } : null;
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
            var refunded = CurrencyFormatter.TryParse(refund.Amount?.Value) ?? amount ?? 0m;
            return new GatewayRefund(refund.Id ?? string.Empty, refund.Status?.Value ?? string.Empty, refunded, currencyCode);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Translate(ex.Error, ex); }
        catch (Exception ex) { throw TranslateCommon(ex); }
    }

    // ---------------------------------------------------------------- vault card

    public async Task<GatewayVaultedCard> VaultCardAsync(GatewayVaultCardRequest request, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer
            {
                Id = string.IsNullOrEmpty(request.PayPalCustomerId) ? null : request.PayPalCustomerId,
                MerchantCustomerId = request.MerchantCustomerId
            },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = request.Card.CardholderName,
                    Number = request.Card.Number,
                    Expiry = request.Card.Expiry,
                    SecurityCode = request.Card.SecurityCode,
                    BillingAddress = BuildAddress(request.Card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: Guid.NewGuid().ToString("N"),
                body: body,
                ct: ct);

            var card = token.PaymentSource?.Card;
            return new GatewayVaultedCard(
                VaultId: token.Id ?? throw new PaymentGatewayException("PayPal did not return a vault token id."),
                CustomerId: token.Customer?.Id,
                Brand: card?.Brand?.Value,
                Last4: card?.LastDigits,
                Expiry: card?.Expiry,
                CardholderName: card?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Translate(ex.Error, ex); }
        catch (Exception ex) { throw TranslateCommon(ex); }
    }

    // ---------------------------------------------------------------- delete card

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Translate(ex.Error, ex); }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at PayPal — deletion is idempotent, treat as success.
        }
        catch (Exception ex) { throw TranslateCommon(ex); }
    }

    // ---------------------------------------------------------------- reconciliation

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<GatewayTransaction>();
        var startDate = FormatSearchTime(from);
        var endDate = FormatSearchTime(to);

        var page = 1;
        while (page <= MaxReconciliationPages)
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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PaymentGatewayException(
                    $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).", ex.Error.StatusCode, null, ex);
            }
            catch (Exception ex) { throw TranslateCommon(ex); }

            if (response.TransactionDetails is { Count: > 0 })
            {
                foreach (var td in response.TransactionDetails)
                {
                    var info = td.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }
                    results.Add(new GatewayTransaction(
                        TransactionId: info.TransactionId,
                        InvoiceId: info.InvoiceId,
                        Status: info.TransactionStatus,
                        Amount: CurrencyFormatter.TryParse(info.TransactionAmount?.Value),
                        CurrencyCode: info.TransactionAmount?.CurrencyCode,
                        Fee: CurrencyFormatter.TryParse(info.FeeAmount?.Value),
                        InitiationDate: ParseTime(info.TransactionInitiationDate)));
                }
            }

            var totalPages = response.TotalPages ?? 1;
            if (page >= totalPages)
            {
                break;
            }
            page++;
        }

        return results;
    }

    // ---------------------------------------------------------------- SDK model helpers

    private PaymentSource BuildPaymentSource(GatewayAuthorizeRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            return new PaymentSource { Card = new CardRequest { VaultId = request.VaultId } };
        }

        var card = request.Card ?? throw new PaymentConflictException("A card or a saved card is required to authorize a payment.");
        return new PaymentSource
        {
            Card = new CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = BuildAddress(card.BillingAddress)
            }
        };
    }

    private static Address? BuildAddress(GatewayBillingAddress? address)
    {
        if (address?.CountryCode is null)
        {
            return null;
        }
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static Money Money(decimal amount, string currencyCode) => new()
    {
        CurrencyCode = currencyCode,
        Value = CurrencyFormatter.Format(amount, currencyCode)
    };

    private sealed record ExtractedAuthorization(string? Id, string? Status, DateTimeOffset? ExpiresAt);

    private static ExtractedAuthorization? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        var authorization = purchaseUnits?
            .Where(pu => pu.Payments?.Authorizations is { Count: > 0 })
            .SelectMany(pu => pu.Payments!.Authorizations!)
            .FirstOrDefault();

        return authorization is null
            ? null
            : new ExtractedAuthorization(authorization.Id, authorization.Status?.Value, ParseTime(authorization.ExpirationTime));
    }

    private static void GuardAgainstChallenge(string? status, IReadOnlyList<LinkDescription>? links)
    {
        var payerActionRequired = string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)
            || (links?.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false);

        if (payerActionRequired)
        {
            throw new PaymentChallengeRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (payer action / 3-D Secure). " +
                "This server-to-server integration does not support an approval round-trip.");
        }
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }

    private static string FormatSearchTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value.Substring(0, max);

    // ---------------------------------------------------------------- error translation

    /// <summary>Map a typed <c>{Operation}Error</c> — every one exposes <c>TryGetError(out Error)</c>.</summary>
    private PaymentGatewayException Translate(ApiError apiError, Exception inner)
    {
        // The typed Error accessor is generated per operation on the concrete error type, but the
        // shape is uniform across the operations in scope, so read it via the concrete type here.
        // Orders + Vault operations: typed Error [4xx/5xx] then the RawError fallback.
        if (apiError is AuthorizeOrderError aoe && aoe.TryGetError(out var e1)) return FromError(e1, inner);
        if (apiError is CreateOrderError coe && coe.TryGetError(out var e2)) return FromError(e2, inner);
        if (apiError is CreatePaymentTokenError cpte && cpte.TryGetError(out var e8)) return FromError(e8, inner);
        if (apiError is DeletePaymentTokenError dpte && dpte.TryGetError(out var e9)) return FromError(e9, inner);

        // Payments operations: typed Error, then a status-specific TryGetNoContent(out RawError) [500].
        if (apiError is CaptureAuthorizedPaymentError cape)
        {
            if (cape.TryGetError(out var e3)) return FromError(e3, inner);
            if (cape.TryGetNoContent(out var nc3)) return FromRaw(nc3, inner);
        }
        if (apiError is GetAuthorizedPaymentError gape)
        {
            if (gape.TryGetError(out var e4)) return FromError(e4, inner);
            if (gape.TryGetNoContent(out var nc4)) return FromRaw(nc4, inner);
        }
        if (apiError is ReauthorizePaymentError rpe)
        {
            if (rpe.TryGetError(out var e5)) return FromError(e5, inner);
            if (rpe.TryGetNoContent(out var nc5)) return FromRaw(nc5, inner);
        }
        if (apiError is RefundCapturedPaymentError rcpe)
        {
            if (rcpe.TryGetError(out var e6)) return FromError(e6, inner);
            if (rcpe.TryGetNoContent(out var nc6)) return FromRaw(nc6, inner);
        }
        if (apiError is VoidPaymentError vpe)
        {
            if (vpe.TryGetError(out var e7)) return FromError(e7, inner);
            if (vpe.TryGetNoContent(out var nc7)) return FromRaw(nc7, inner);
        }

        if (apiError.TryGetRawError(out var raw)) return FromRaw(raw, inner);
        return new PaymentGatewayException("PayPal returned an unrecognised error.", null, null, inner);
    }

    private PaymentGatewayException FromRaw(RawError raw, Exception inner) =>
        new($"PayPal error (HTTP {(int)raw.StatusCode}).", raw.StatusCode, null, inner);

    private PaymentGatewayException FromError(Error error, Exception inner)
    {
        var issues = error.Details is { Count: > 0 }
            ? " [" + string.Join("; ", error.Details.Select(d => d.Issue)) + "]"
            : string.Empty;

        _logger.LogWarning("PayPal error {Name} (debug_id {DebugId}): {Message}{Issues}",
            error.Name, error.DebugId, error.Message, issues);

        return new PaymentGatewayException($"{error.Name}: {error.Message}{issues}", null, error.DebugId, inner);
    }

    private PaymentGatewayException TranslateCommon(Exception ex)
    {
        switch (ex)
        {
            case PaymentGatewayException pge:
                return pge; // already translated (e.g. a challenge thrown from the happy path)
            case PaymentConflictException:
                throw ex; // domain rule — let it propagate unchanged
            case SdkException<RawError> raw:
                return new PaymentGatewayException($"PayPal error (HTTP {(int)raw.Error.StatusCode}).", raw.Error.StatusCode, null, ex);
            case JsonException:
                return new PaymentGatewayException("PayPal returned a response that could not be processed.", null, null, ex);
            case HttpRequestException:
            case TaskCanceledException:
                return new PaymentGatewayException("PayPal is currently unreachable. Please try again.", null, null, ex);
            default:
                return new PaymentGatewayException("An unexpected error occurred talking to PayPal.", null, null, ex);
        }
    }
}
