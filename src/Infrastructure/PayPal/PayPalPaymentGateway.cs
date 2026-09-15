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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// The single place that talks to PayPal. Translates the app's plain <see cref="IPaymentGateway"/> contract
/// into PayPal SDK calls, and every SDK/transport failure into a caller-safe
/// <see cref="PaymentGatewayException"/>. Card details flow straight through to PayPal and are never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const int MaxSearchWindowDays = 31; // PayPal caps a single transaction-search request to 31 days.
    private const int SearchPageSize = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<GatewayAuthorization> AuthorizeOrderAsync(
        decimal amount, string currencyCode, PaymentInstrument instrument, string idempotencyKey, CancellationToken ct = default)
    {
        // 1) Create a PayPal order (intent AUTHORIZE) carrying the card (raw or vaulted).
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currencyCode,
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = BuildCard(instrument) }
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{idempotencyKey}:create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            ex.Error.TryGetError(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildTyped("create order", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("create order", ex); }

        ThrowIfBuyerActionRequired(order.Status, order.Links);

        if (order.Id is null)
        {
            throw new PaymentGatewayException("PayPal accepted the order but returned no order id.", 502);
        }

        // 2) With a direct card and intent=AUTHORIZE, PayPal authorizes the order as part of creation, so
        //    the hold is already present on the create response. Read it; only call AuthorizeOrder explicitly
        //    when it is not (e.g. a flow that leaves the order merely APPROVED).
        var authorization = order.PurchaseUnits?
            .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

        if (authorization?.Id is null)
        {
            OrderAuthorizeResponse authResponse;
            try
            {
                authResponse = await _client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: $"{idempotencyKey}:authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                ex.Error.TryGetError(out var e);
                ex.Error.TryGetRawError(out var raw);
                throw BuildTyped("authorize order", e, raw, ex);
            }
            catch (Exception ex) { throw MapNonApi("authorize order", ex); }

            ThrowIfBuyerActionRequired(authResponse.Status, null);

            authorization = authResponse.PurchaseUnits?
                .FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        }

        if (authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal accepted the order but returned no authorization to hold the funds.", 502);
        }

        return new GatewayAuthorization(
            order.Id,
            authorization.Id,
            authorization.Status?.Value ?? string.Empty,
            ParseDate(authorization.ExpirationTime));
    }

    public async Task<GatewayAuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
            return new GatewayAuthorizationState(
                auth.Id ?? authorizationId,
                auth.Status?.Value ?? string.Empty,
                ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildTyped("get authorization", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("get authorization", ex); }
    }

    public async Task<GatewayCapture> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"{idempotencyKey}:capture",
                payPalAuthAssertion: null,
                body: null, // full capture of the held amount
                prefer: "return=representation",
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            var captured = ParseMoney(breakdown?.GrossAmount?.Value) ?? ParseMoney(capture.Amount?.Value) ?? 0m;
            var currency = breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? _settings.Currency;

            return new GatewayCapture(
                capture.Id ?? string.Empty,
                capture.Status?.Value ?? string.Empty,
                captured,
                ParseMoney(breakdown?.PaypalFee?.Value),
                ParseMoney(breakdown?.NetAmount?.Value),
                currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            ex.Error.TryGetError(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildTyped("capture payment", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("capture payment", ex); }
    }

    public async Task<GatewayAuthorizationState> ReauthorizeAsync(
        string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"{idempotencyKey}:reauth",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: ct);

            return new GatewayAuthorizationState(
                reauth.Id ?? authorizationId,
                reauth.Status?.Value ?? string.Empty,
                ParseDate(reauth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            ex.Error.TryGetError(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildTyped("re-authorize payment", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("re-authorize payment", ex); }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"{idempotencyKey}:void",
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            ex.Error.TryGetError(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildTyped("void authorization", e, raw, ex);
        }
        catch (JsonException)
        {
            // A successful void returns HTTP 204 No Content; the SDK throws JsonException trying to
            // deserialize the empty body. That empty body IS the success signal, not a failure.
        }
        catch (Exception ex) { throw MapNonApi("void authorization", ex); }
    }

    public async Task<GatewayRefund> RefundAsync(
        string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount is null
            ? null // full refund
            : new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } };

        try
        {
            // The caller's idempotency key IS the PayPal-Request-Id: repeating it returns the original refund.
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new GatewayRefund(
                refund.Id ?? string.Empty,
                refund.Status?.Value ?? string.Empty,
                ParseMoney(refund.Amount?.Value) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currencyCode);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            ex.Error.TryGetError(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildTyped("refund payment", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("refund payment", ex); }
    }

    public async Task<GatewayVaultedCard> VaultCardAsync(GatewayCard card, string idempotencyKey, CancellationToken ct = default)
    {
        // 1) Create a setup token for the raw card.
        SetupTokenResponse setup;
        try
        {
            setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: $"{idempotencyKey}:setup",
                body: new SetupTokenRequest
                {
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Name = card.CardholderName,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = BuildAddress(card.BillingAddress)
                        }
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            ex.Error.TryGetError1(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildVault("save card", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("save card", ex); }

        if (setup.Id is null)
        {
            throw new PaymentGatewayException("PayPal did not return a setup token for the card.", 502);
        }

        // 2) Promote the setup token to a reusable payment (vault) token.
        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"{idempotencyKey}:token",
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setup.Id,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                ct: ct);

            if (token.Id is null)
            {
                throw new PaymentGatewayException("PayPal did not return a reusable token for the saved card.", 502);
            }

            var cardEntity = token.PaymentSource?.Card;
            return new GatewayVaultedCard(
                token.Id,
                cardEntity?.Brand?.Value ?? "CARD",
                cardEntity?.LastDigits ?? string.Empty,
                cardEntity?.Expiry ?? card.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            ex.Error.TryGetError1(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildVault("save card", e, raw, ex);
        }
        catch (PaymentGatewayException) { throw; }
        catch (Exception ex) { throw MapNonApi("save card", ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            ex.Error.TryGetError1(out var e);
            ex.Error.TryGetRawError(out var raw);
            throw BuildVault("delete saved card", e, raw, ex);
        }
        catch (Exception ex) { throw MapNonApi("delete saved card", ex); }
    }

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ReconciliationTransaction>();

        // PayPal caps a single search to a 31-day window: split [from,to] into <=31-day sub-ranges and
        // page each to completion so the whole range is covered.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(MaxSearchWindowDays);
            if (windowEnd > to) windowEnd = to;

            int page = 1;
            int totalPages;
            do
            {
                SearchResponse response;
                try
                {
                    response = await _client.TransactionSearch.SearchTransactions(
                        startDate: FormatSearchDate(windowStart),
                        endDate: FormatSearchDate(windowEnd),
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
                        page: page,
                        ct: ct);
                }
                catch (SdkException<RawError> ex) { throw MapRaw("reconciliation search", ex.Error, ex); }
                catch (Exception ex) { throw MapNonApi("reconciliation search", ex); }

                totalPages = response.TotalPages ?? 1;
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null) continue;
                    results.Add(new ReconciliationTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode,
                        ParseMoney(info.FeeAmount?.Value),
                        ParseDate(info.TransactionInitiationDate)));
                }

                page++;
            }
            while (page <= totalPages);

            if (windowEnd >= to) break;
            windowStart = windowEnd;
        }

        return results;
    }

    // --- mapping helpers ---

    private CardRequest BuildCard(PaymentInstrument instrument)
    {
        if (instrument.IsVaulted)
        {
            return new CardRequest { VaultId = instrument.VaultId };
        }

        var card = instrument.Card!;
        return new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
    }

    private static Address? BuildAddress(GatewayBillingAddress? address)
    {
        if (address is null) return null;
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

    private void ThrowIfBuyerActionRequired(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        var payerActionByStatus = status is not null && status == OrderStatus.PayerActionRequired;
        var payerActionByLink = links?.Any(l =>
            string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)) ?? false;

        if (payerActionByStatus || payerActionByLink)
        {
            throw new BuyerActionRequiredException(
                "This card requires the shopper to approve the payment in their browser (3-D Secure). " +
                "That approval flow is not supported here — ask the shopper to use a different card.");
        }
    }

    private static string FormatAmount(decimal amount) =>
        amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto) ? dto : null;

    /// <summary>
    /// Builds a caller-safe exception from a typed PayPal <see cref="Error"/> payload and/or a
    /// <see cref="RawError"/>, extracted at the concrete catch site. A typed body is a
    /// shopper/operator-actionable rejection (4xx); neither present means an untyped 5xx/no-content → 502.
    /// </summary>
    private PaymentGatewayException BuildTyped(string op, Error? typed, RawError? raw, Exception source)
    {
        if (typed is not null)
        {
            var issue = typed.Details?.FirstOrDefault()?.Issue;
            var status = raw is not null ? (int)raw.StatusCode : 422;
            return new PaymentGatewayException(BuildSafeMessage(op, typed.Message, issue), status, issue, source);
        }
        if (raw is not null)
        {
            return MapRaw(op, raw, source);
        }
        return new PaymentGatewayException($"PayPal rejected the {op} request.", 502, inner: source);
    }

    /// <summary>Vault operations carry the <see cref="Error1"/> payload shape.</summary>
    private PaymentGatewayException BuildVault(string op, Error1? typed, RawError? raw, Exception source)
    {
        if (typed is not null)
        {
            var issue = typed.Details?.FirstOrDefault()?.Issue;
            var status = raw is not null ? (int)raw.StatusCode : 422;
            return new PaymentGatewayException(BuildSafeMessage(op, typed.Message, issue), status, issue, source);
        }
        if (raw is not null)
        {
            return MapRaw(op, raw, source);
        }
        return new PaymentGatewayException($"PayPal rejected the {op} request.", 502, inner: source);
    }

    private PaymentGatewayException MapRaw(string op, RawError raw, Exception source)
    {
        var status = (int)raw.StatusCode;
        return new PaymentGatewayException(
            $"PayPal rejected the {op} request (HTTP {status}).", status, inner: source);
    }

    private PaymentGatewayException MapNonApi(string op, Exception source)
    {
        // A drifted/broken JSON body — outcome genuinely unknown -> treat as an upstream problem (502).
        if (source is JsonException)
        {
            return new PaymentGatewayException(
                $"PayPal returned a response for the {op} request that could not be processed.", 502, inner: source);
        }
        // Connection failures (host unreachable, reset, timeout).
        if (source is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return new PaymentGatewayException($"PayPal could not be reached for the {op} request.", 502, inner: source);
        }
        if (source is PaymentGatewayException pge)
        {
            return pge;
        }
        return new PaymentGatewayException($"An unexpected error occurred during the {op} request.", 502, inner: source);
    }

    private static string BuildSafeMessage(string op, string? providerMessage, string? issue)
    {
        var detail = string.IsNullOrWhiteSpace(providerMessage) ? "the request was rejected" : providerMessage;
        return issue is null
            ? $"PayPal rejected the {op} request: {detail}."
            : $"PayPal rejected the {op} request: {detail} (issue: {issue}).";
    }
}
