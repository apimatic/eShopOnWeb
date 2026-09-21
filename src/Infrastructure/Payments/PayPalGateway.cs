using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
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
/// The one place PayPal is spoken to. Translates the plain application requests into SDK calls and every SDK
/// or transport failure into <see cref="PayPalGatewayException"/>. Card details flow through here for a
/// single call and are never persisted or logged.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    // Currencies that do not use minor units (no decimal places).
    private static readonly HashSet<string> ZeroDecimalCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "JPY", "KRW", "VND", "CLP", "HUF", "TWD"
    };

    private const int TransactionPageSize = 100;
    private const int MaxPagesPerWindow = 100;      // backstop against a non-advancing provider
    private const int MaxSearchWindowDays = 31;     // PayPal caps a single search at 31 days

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string Currency => _options.Currency;

    // ---------------------------------------------------------------------------------------------
    // Authorize (hold)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalAuthorizationOutcome> AuthorizeAsync(PayPalAuthorizeRequest request, CancellationToken ct)
    {
        var paymentSource = BuildPaymentSource(request);

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown { CurrencyCode = Currency, Value = FormatAmount(request.Amount) },
                    InvoiceId = string.IsNullOrWhiteSpace(request.InvoiceId) ? null : request.InvoiceId,
                    CustomId = request.CustomId,
                    Description = request.Description
                }
            },
            PaymentSource = paymentSource
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
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "create order", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "create order", ex);
            }
            throw new PayPalGatewayException("create order failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("create order", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException("create order: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
        }

        var payPalOrderId = order.Id ?? throw new PayPalGatewayException(
            "create order: PayPal did not return an order id", PayPalFailureKind.Unknown);
        var orderStatus = order.Status?.Value ?? string.Empty;
        var (authId, authStatus, authAt) = ExtractAuthorization(order.PurchaseUnits);

        // If a card was supplied and the order was approved but no authorization was created inline, do the
        // explicit authorize step. (For single-step card create, the authorization is already present.)
        if (authId is null && orderStatus == OrderStatus.Approved.Value)
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
            catch (SdkException<AuthorizeOrderError> ex)
            {
                if (ex.Error.TryGetError(out var e))
                {
                    throw TranslateError(e, "authorize order", ex);
                }
                if (ex.Error.TryGetRawError(out var raw))
                {
                    throw FromRaw(raw, "authorize order", ex);
                }
                throw new PayPalGatewayException("authorize order failed", PayPalFailureKind.Unknown, inner: ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure("authorize order", ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalGatewayException("authorize order: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
            }

            orderStatus = authResp.Status?.Value ?? orderStatus;
            (authId, authStatus, authAt) = ExtractAuthorization(authResp.PurchaseUnits);
        }

        var requiresApproval = orderStatus == OrderStatus.PayerActionRequired.Value;
        return new PayPalAuthorizationOutcome(payPalOrderId, authId, orderStatus, authStatus, authAt, requiresApproval);
    }

    // ---------------------------------------------------------------------------------------------
    // Capture (money moves)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalCaptureOutcome> CaptureAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
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
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "capture payment", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw FromRaw(nc, "capture payment", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "capture payment", ex);
            }
            throw new PayPalGatewayException("capture payment failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("capture payment", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException("capture payment: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
        }

        var captureId = captured.Id ?? throw new PayPalGatewayException(
            "capture payment: PayPal did not return a capture id", PayPalFailureKind.Unknown);
        var status = captured.Status?.Value ?? string.Empty;

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = TryParseAmount(breakdown?.GrossAmount.Value) ?? TryParseAmount(captured.Amount?.Value) ?? amount;
        var fee = TryParseAmount(breakdown?.PaypalFee?.Value);
        var net = TryParseAmount(breakdown?.NetAmount?.Value);
        var currency = breakdown?.GrossAmount.CurrencyCode ?? captured.Amount?.CurrencyCode ?? Currency;

        return new PayPalCaptureOutcome(captureId, status, gross, fee, net, currency, ParseDate(captured.CreateTime));
    }

    // ---------------------------------------------------------------------------------------------
    // Reauthorize a stale hold
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalReauthorizeOutcome> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount) } },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "reauthorize payment", ex, forceNotRenewable: true);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw FromRaw(nc, "reauthorize payment", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "reauthorize payment", ex);
            }
            throw new PayPalGatewayException("reauthorize payment failed", PayPalFailureKind.AuthorizationNotRenewable, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("reauthorize payment", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException("reauthorize payment: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
        }

        var newAuthId = auth.Id ?? throw new PayPalGatewayException(
            "reauthorize payment: PayPal did not return an authorization id", PayPalFailureKind.Unknown);
        return new PayPalReauthorizeOutcome(newAuthId, auth.Status?.Value ?? string.Empty, ParseDate(auth.CreateTime));
    }

    public async Task<PayPalAuthorizationStatus> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
            return new PayPalAuthorizationStatus(auth.Status?.Value ?? string.Empty, ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "get authorization", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw FromRaw(nc, "get authorization", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "get authorization", ex);
            }
            throw new PayPalGatewayException("get authorization failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("get authorization", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Void (cancel before capture)
    // ---------------------------------------------------------------------------------------------

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
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "void authorization", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw FromRaw(nc, "void authorization", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "void authorization", ex);
            }
            throw new PayPalGatewayException("void authorization failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("void authorization", ex);
        }
        catch (JsonException)
        {
            // A successful void returns 204 No Content (empty body); the SDK's JSON reader throws on the
            // empty body. The void reached a 2xx, so it succeeded — treat this as success.
            _logger.LogInformation("Void of {AuthorizationId} returned an empty (204) body; treating as success.", authorizationId);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Refund (after capture)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalRefundOutcome> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        // Full refund: omit the amount (empty body). Partial refund: set the amount.
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount.Value) } }
            : new RefundRequest();

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "refund payment", ex);
            }
            if (ex.Error.TryGetNoContent(out var nc))
            {
                throw FromRaw(nc, "refund payment", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "refund payment", ex);
            }
            throw new PayPalGatewayException("refund payment failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("refund payment", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalGatewayException("refund payment: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
        }

        var refundId = refund.Id ?? throw new PayPalGatewayException(
            "refund payment: PayPal did not return a refund id", PayPalFailureKind.Unknown);
        var refundedAmount = TryParseAmount(refund.Amount?.Value) ?? amount ?? 0m;
        return new PayPalRefundOutcome(refundId, refund.Status?.Value ?? string.Empty, refundedAmount);
    }

    // ---------------------------------------------------------------------------------------------
    // Vault (save a card)
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalVaultOutcome> VaultCardAsync(PayPalVaultCardRequest request, CancellationToken ct)
    {
        var card = request.Card;
        // On the first save, let PayPal generate the customer (returned in the response); on later saves,
        // reuse the shopper's existing PayPal customer id so their cards group together.
        var customer = request.ExistingCustomerId is not null
            ? new Customer { Id = request.ExistingCustomerId }
            : null;

        // Step 1: create a setup token from the raw card (the supported direct-card vaulting path).
        SetupTokenResponse setup;
        try
        {
            setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: $"setup-{request.IdempotencyKey}",
                body: new SetupTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = MapAddress(card.BillingAddress),
                            // Verify the card at vault time; only steps up to a challenge when the card
                            // actually requires SCA (the sandbox test card does not).
                            VerificationMethod = VaultCardVerificationMethod.ScaWhenRequired,
                            // Required by PayPal when a verification method is set. The URLs are only used
                            // if the card triggers a 3DS contingency (the sandbox test card does not); if a
                            // real card ever requires a browser approval, the pay/save flow surfaces that as
                            // an actionable error rather than performing an approval round-trip.
                            ExperienceContext = new VaultCardExperienceContext
                            {
                                ReturnUrl = "https://example.com/paypal/return",
                                CancelUrl = "https://example.com/paypal/cancel"
                            }
                        }
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "create setup token", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "create setup token", ex);
            }
            throw new PayPalGatewayException("create setup token failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("create setup token", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PayPal create setup token: response did not match the SDK model.");
            throw new PayPalGatewayException("create setup token: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
        }

        var setupTokenId = setup.Id ?? throw new PayPalGatewayException(
            "create setup token: PayPal did not return a token id", PayPalFailureKind.Unknown);

        // Step 2: exchange the setup token for a permanent payment token (the saved card).
        PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"vault-{request.IdempotencyKey}",
                body: new PaymentTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setupTokenId, Type = VaultTokenRequestType.SetupToken }
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "vault card", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "vault card", ex);
            }
            throw new PayPalGatewayException("vault card failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("vault card", ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PayPal vault card (payment token): response did not match the SDK model.");
            throw new PayPalGatewayException("vault card: unreadable PayPal response", PayPalFailureKind.Unknown, inner: ex);
        }

        var vaultId = token.Id ?? throw new PayPalGatewayException(
            "vault card: PayPal did not return a token id", PayPalFailureKind.Unknown);
        var cardEntity = token.PaymentSource?.Card;
        // Always keep a safe descriptor; never the PAN. Fall back to the input's last 4 / expiry if the
        // response omits them.
        var lastDigits = cardEntity?.LastDigits ?? (card.Number.Length >= 4 ? card.Number[^4..] : null);
        var expiry = cardEntity?.Expiry ?? card.Expiry;
        var name = cardEntity?.Name ?? card.CardholderName;
        return new PayPalVaultOutcome(
            vaultId,
            token.Customer?.Id ?? setup.Customer?.Id,
            cardEntity?.Brand?.Value,
            lastDigits,
            expiry,
            name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var e))
            {
                throw TranslateError(e, "delete vaulted card", ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw FromRaw(raw, "delete vaulted card", ex);
            }
            throw new PayPalGatewayException("delete vaulted card failed", PayPalFailureKind.Unknown, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure("delete vaulted card", ex);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Reconciliation search
    // ---------------------------------------------------------------------------------------------

    public async Task<PayPalTransactionSearch> SearchTransactionsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        var records = new List<PayPalTransactionRecord>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;
        var windows = 0;

        var windowStart = fromUtc.ToUniversalTime();
        var end = toUtc.ToUniversalTime();

        while (windowStart < end)
        {
            var windowEnd = windowStart.AddDays(MaxSearchWindowDays);
            if (windowEnd > end)
            {
                windowEnd = end;
            }
            windows++;

            var page = 1;
            var totalPages = 1;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatRfc3339(windowStart),
                        endDate: FormatRfc3339(windowEnd),
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
                        pageSize: TransactionPageSize,
                        page: page,
                        ct: ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw FromRaw(ex.Error, "search transactions", ex);
                }
                catch (Exception ex) when (IsTransport(ex))
                {
                    throw TransportFailure("search transactions", ex);
                }

                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null)
                    {
                        continue;
                    }
                    var key = info.TransactionId ?? info.PaypalReferenceId ?? Guid.NewGuid().ToString();
                    if (!seen.Add(key))
                    {
                        continue;
                    }
                    records.Add(new PayPalTransactionRecord(
                        info.TransactionId,
                        info.PaypalReferenceId,
                        TryParseAmount(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseDate(info.TransactionInitiationDate),
                        info.TransactionEventCode));
                }

                totalPages = response.TotalPages ?? 1;
                page++;

                if (page > MaxPagesPerWindow && page <= totalPages)
                {
                    truncated = true;
                    _logger.LogWarning(
                        "Reconciliation truncated: window {Start}..{End} has {TotalPages} pages, capped at {Cap}.",
                        windowStart, windowEnd, totalPages, MaxPagesPerWindow);
                    break;
                }
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return new PayPalTransactionSearch(records, truncated, windows);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private PaymentSource BuildPaymentSource(PayPalAuthorizeRequest request)
    {
        if (request.VaultId is not null)
        {
            // Pay with a saved card: reference the vaulted payment token by id (never card details).
            return new PaymentSource { Card = new CardRequest { VaultId = request.VaultId } };
        }

        var card = request.Card ?? throw new PayPalGatewayException(
            "authorize: neither card details nor a saved card were supplied", PayPalFailureKind.InvalidRequest);

        return new PaymentSource
        {
            Card = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.CardholderName,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
    }

    private static Address? MapAddress(PayPalBillingAddress? address)
    {
        // Address.CountryCode is required by the SDK model, so only build one when a country code exists.
        if (address?.CountryCode is null)
        {
            return null;
        }
        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static (string? Id, string? Status, DateTimeOffset? CreatedAt) ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits)
    {
        var auth = purchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (auth is null)
        {
            return (null, null, null);
        }
        return (auth.Id, auth.Status?.Value, ParseDate(auth.CreateTime));
    }

    private string FormatAmount(decimal amount)
    {
        var decimals = ZeroDecimalCurrencies.Contains(Currency) ? 0 : 2;
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static decimal? TryParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto
            : null;

    private static string FormatRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private PayPalGatewayException TranslateError(Error error, string context, Exception inner, bool forceNotRenewable = false)
    {
        var issue = error.Details?.FirstOrDefault()?.Issue;
        var code = issue ?? error.Name;
        var kind = forceNotRenewable ? PayPalFailureKind.AuthorizationNotRenewable : Classify(code);
        _logger.LogWarning("PayPal {Context} failed: name={Name} issue={Issue} message={Message} details={Details} debugId={DebugId}",
            context, error.Name, issue, error.Message,
            error.Details is null ? "(none)" : string.Join("; ", error.Details.Select(d => $"{d.Issue}@{d.Field}:{d.Description}")),
            error.DebugId);
        return new PayPalGatewayException(
            $"{context} failed: {error.Message}", kind, code, error.DebugId, inner: inner);
    }

    private PayPalGatewayException FromRaw(RawError raw, string context, Exception inner)
    {
        var status = (int)raw.StatusCode;
        var kind = ClassifyStatus(status);
        _logger.LogWarning("PayPal {Context} failed: status={Status}", context, status);
        return new PayPalGatewayException($"{context} failed (HTTP {status})", kind, statusCode: status, inner: inner);
    }

    private PayPalGatewayException TransportFailure(string context, Exception inner)
    {
        // The request may have reached PayPal; the outcome is unknown and must be reconciled by the caller.
        _logger.LogWarning(inner, "PayPal {Context}: transport failure (outcome unknown)", context);
        return new PayPalGatewayException($"{context}: PayPal unreachable (outcome unknown)", PayPalFailureKind.Unknown, inner: inner);
    }

    private static PayPalFailureKind Classify(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return PayPalFailureKind.InvalidRequest;
        }
        var c = code.ToUpperInvariant();
        if (c.Contains("AUTHORIZATION_EXPIRED"))
        {
            return PayPalFailureKind.AuthorizationExpired;
        }
        if (c.Contains("REAUTHORIZATION"))
        {
            return PayPalFailureKind.AuthorizationNotRenewable;
        }
        if (c.Contains("RESOURCE_NOT_FOUND") || c.Contains("INVALID_RESOURCE_ID"))
        {
            return PayPalFailureKind.NotFound;
        }
        if (c.Contains("ALREADY_CAPTURED") || c.Contains("ALREADY_AUTHORIZED") || c.Contains("PREVIOUSLY_CAPTURED")
            || c.Contains("AUTHORIZATION_ALREADY_VOIDED") || c.Contains("VOIDED") || c.Contains("FULLY_REFUNDED")
            || c.Contains("ORDER_ALREADY_COMPLETED") || c.Contains("MAX_CAPTURE") || c.Contains("REFUND_AMOUNT_EXCEEDED"))
        {
            return PayPalFailureKind.Conflict;
        }
        if (c.Contains("AUTHENTICATION_FAILURE") || c.Contains("NOT_AUTHORIZED") || c.Contains("RATE_LIMIT")
            || c.Contains("INTERNAL_SERVER_ERROR") || c.Contains("INTERNAL_SERVICE_ERROR"))
        {
            return PayPalFailureKind.ProviderUnavailable;
        }
        return PayPalFailureKind.InvalidRequest;
    }

    private static PayPalFailureKind ClassifyStatus(int status) => status switch
    {
        401 or 403 => PayPalFailureKind.ProviderUnavailable,
        404 => PayPalFailureKind.NotFound,
        409 => PayPalFailureKind.Conflict,
        429 => PayPalFailureKind.ProviderUnavailable,
        >= 400 and < 500 => PayPalFailureKind.InvalidRequest,
        _ => PayPalFailureKind.ProviderUnavailable
    };
}
