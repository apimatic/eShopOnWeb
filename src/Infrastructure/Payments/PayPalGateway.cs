using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.Enum;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The only place in the solution that talks to the PayPal SDK. Translates the application's
/// <see cref="IPayPalGateway"/> contract onto the APIMatic-generated PayPalServerSdk surface and
/// converts every SDK failure into the application's own <see cref="PayPalGatewayException"/> family.
/// Never logs card numbers, CVV, expiry, or raw vault data — only PayPal ids and statuses.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;
    private readonly PayPalSettings _settings;

    public PayPalGateway(
        PayPalServerSdkClient client,
        IAppLogger<PayPalGateway> logger,
        PayPalSettings settings)
    {
        _client = client;
        _logger = logger;
        _settings = settings;
    }

    // ---------------------------------------------------------------------------------------------
    // Authorize (direct raw card, and vaulted card)
    // ---------------------------------------------------------------------------------------------

    public Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string requestId, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = ToExpiry(card.ExpiryMonth, card.ExpiryYear),
            SecurityCode = card.SecurityCode,
            BillingAddress = ToAddress(card.BillingAddress),
        };
        return AuthorizeCoreAsync(amount, currency, cardRequest, requestId, cancellationToken);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        decimal amount, string currency, string vaultId, string requestId, CancellationToken cancellationToken = default)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return AuthorizeCoreAsync(amount, currency, cardRequest, requestId, cancellationToken);
    }

    private Task<AuthorizationResult> AuthorizeCoreAsync(
        decimal amount, string currency, CardRequest cardRequest, string requestId, CancellationToken ct)
        => RunAsync(async () =>
        {
            var order = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = ResolveCurrency(currency),
                            Value = FormatMoney(amount),
                        },
                    },
                },
                PaymentSource = new PaymentSource { Card = cardRequest },
            };

            Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: order,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex) { throw Translate(ex); }

            if (created.Status == OrderStatus.PayerActionRequired)
            {
                throw new PayPalCardChallengeRequiredException(
                    "PayPal requires shopper approval (card challenge / 3-D Secure) for this card; direct authorization cannot proceed.");
            }

            var orderId = created.Id ?? string.Empty;
            var auth = FindAuthorization(created.PurchaseUnits);

            if (auth is null)
            {
                // Fallback for the case where the create response did not carry the authorization inline.
                OrderAuthorizeResponse authorized;
                try
                {
                    authorized = await _client.Orders.AuthorizeOrder(
                        id: orderId,
                        payPalMockResponse: null,
                        payPalRequestId: requestId,
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: "return=representation",
                        ct: ct);
                }
                catch (SdkException<AuthorizeOrderError> ex) { throw Translate(ex); }

                if (authorized.Status == OrderStatus.PayerActionRequired)
                {
                    throw new PayPalCardChallengeRequiredException(
                        "PayPal requires shopper approval (card challenge / 3-D Secure) for this card; direct authorization cannot proceed.");
                }

                orderId = authorized.Id ?? orderId;
                auth = FindAuthorization(authorized.PurchaseUnits);
            }

            if (auth is null)
            {
                throw new PayPalGatewayException(
                    "PayPal accepted the order but returned no authorization to act on.");
            }

            var status = Wire(auth.Status);
            _logger.LogInformation(
                "PayPal authorization placed. orderId={OrderId} authorizationId={AuthorizationId} status={Status}",
                orderId, auth.Id ?? string.Empty, status);
            return new AuthorizationResult(orderId, auth.Id ?? string.Empty, status);
        }, ct);

    // ---------------------------------------------------------------------------------------------
    // Read authorization
    // ---------------------------------------------------------------------------------------------

    public Task<ApplicationCore.Payments.AuthorizationStatus> GetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            PaymentAuthorization pa;
            try
            {
                pa = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: cancellationToken);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex) { throw Translate(ex); }

            return new ApplicationCore.Payments.AuthorizationStatus(pa.Id ?? authorizationId, Wire(pa.Status));
        }, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Capture
    // ---------------------------------------------------------------------------------------------

    public Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, decimal amount, string currency, string requestId, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            CapturedPayment cap;
            try
            {
                // return=representation so PayPal includes seller_receivable_breakdown (minimal omits it).
                cap = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Translate(ex); }

            var breakdown = cap.SellerReceivableBreakdown;

            // Defensive: if the breakdown is still absent, re-read the capture by id to obtain it.
            if (breakdown is null && cap.Id is { Length: > 0 } captureId)
            {
                try
                {
                    var reread = await _client.Payments.GetCapturedPayment(
                        captureId: captureId,
                        payPalMockResponse: null,
                        ct: cancellationToken);
                    breakdown = reread.SellerReceivableBreakdown;
                }
                catch (SdkException<GetCapturedPaymentError> ex) { throw Translate(ex); }
            }
            var gross = ParseMoney(breakdown?.GrossAmount.Value);
            var fee = ParseMoney(breakdown?.PaypalFee?.Value);
            var net = breakdown?.NetAmount?.Value is { } netValue ? ParseMoney(netValue) : gross - fee;
            var status = Wire(cap.Status);

            _logger.LogInformation(
                "PayPal capture completed. authorizationId={AuthorizationId} captureId={CaptureId} status={Status}",
                authorizationId, cap.Id ?? string.Empty, status);
            return new CaptureResult(cap.Id ?? string.Empty, status, gross, fee, net);
        }, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Reauthorize
    // ---------------------------------------------------------------------------------------------

    public Task<ReauthorizationResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            PaymentAuthorization pa;
            try
            {
                pa = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: null,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = ResolveCurrency(currency), Value = FormatMoney(amount) },
                    },
                    ct: cancellationToken);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                // A typed business error here is the operator-actionable "can no longer be reauthorized"
                // case (voided / captured / expired window / 4xx-22). Surface PayPal's own message + issue
                // verbatim — never a hard-coded issue constant.
                if (ex.Error.TryGetError(out var e))
                {
                    throw new PayPalAuthorizationUnrenewableException(Compose(e.Message, FirstIssue(e.Details)), e.Name, e.DebugId);
                }
                if (ex.Error.TryGetNoContent(out var noContent)) { throw ToGatewayException(noContent); }
                if (ex.Error.TryGetRawError(out var raw)) { throw ToGatewayException(raw); }
                throw Unknown();
            }

            var status = Wire(pa.Status);
            _logger.LogInformation(
                "PayPal reauthorization completed. authorizationId={AuthorizationId} status={Status}",
                pa.Id ?? authorizationId, status);
            return new ReauthorizationResult(pa.Id ?? authorizationId, status);
        }, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Void
    // ---------------------------------------------------------------------------------------------

    public Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            try
            {
                // return=representation so PayPal replies 200 + the PaymentAuthorization body (status VOIDED);
                // the default return=minimal yields 204 No Content, which the SDK fails to deserialize.
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: null,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<VoidPaymentError> ex) { throw Translate(ex); }
            catch (JsonException)
            {
                // Belt-and-suspenders: an empty/unparseable body here means the void request went through
                // but the response could not be deserialized. The void may well have succeeded server-side,
                // so confirm by re-reading the authorization — treat as success ONLY if it is now VOIDED.
                await ConfirmVoidedAsync(authorizationId, cancellationToken);
            }

            _logger.LogInformation("PayPal authorization voided. authorizationId={AuthorizationId}", authorizationId);
        }, cancellationToken);

    private async Task ConfirmVoidedAsync(string authorizationId, CancellationToken ct)
    {
        PaymentAuthorization pa;
        try
        {
            pa = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Translate(ex); }

        if (pa.Status != PayPalServerSdk.Models.Enums.AuthorizationStatus.Voided)
        {
            throw new PayPalGatewayException(
                $"PayPal did not confirm the void; authorization status is '{Wire(pa.Status)}'.");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Refund
    // ---------------------------------------------------------------------------------------------

    public Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string requestId, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            RefundRequest? body = amount is { } refundAmount
                ? new RefundRequest { Amount = new Money { CurrencyCode = ResolveCurrency(currency), Value = FormatMoney(refundAmount) } }
                : null;

            Refund refund;
            try
            {
                // return=representation so the refund response carries its Amount (minimal may omit it);
                // if it is still null we fall back to the requested amount below.
                refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: cancellationToken);
            }
            catch (SdkException<RefundCapturedPaymentError> ex) { throw Translate(ex); }

            var refunded = refund.Amount?.Value is { } responseValue ? ParseMoney(responseValue) : (amount ?? 0m);
            var status = Wire(refund.Status);

            _logger.LogInformation(
                "PayPal refund issued. captureId={CaptureId} refundId={RefundId} status={Status}",
                captureId, refund.Id ?? string.Empty, status);
            return new RefundResult(refund.Id ?? string.Empty, status, refunded);
        }, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Vault
    // ---------------------------------------------------------------------------------------------

    public Task<VaultCardResult> VaultCardAsync(CardDetails card, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            var body = new PaymentTokenRequest
            {
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.CardholderName,
                        Number = card.Number,
                        Expiry = ToExpiry(card.ExpiryMonth, card.ExpiryYear),
                        SecurityCode = card.SecurityCode,
                        BillingAddress = ToAddress(card.BillingAddress),
                    },
                },
            };

            PaymentTokenResponse resp;
            try
            {
                resp = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: null,
                    body: body,
                    ct: cancellationToken);
            }
            catch (SdkException<CreatePaymentTokenError> ex) { throw Translate(ex); }

            var vaulted = resp.PaymentSource?.Card;
            var (expiryMonth, expiryYear) = FromExpiry(vaulted?.Expiry);
            var brand = Wire(vaulted?.Brand);
            var last4 = vaulted?.LastDigits ?? string.Empty;

            _logger.LogInformation(
                "PayPal card vaulted. vaultId={VaultId} brand={Brand} last4={Last4}",
                resp.Id ?? string.Empty, brand, last4);
            // CreatePaymentToken's response (PaymentTokenResponse) carries no status field — hence null.
            return new VaultCardResult(resp.Id ?? string.Empty, brand, last4, expiryMonth, expiryYear, null);
        }, cancellationToken);

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: cancellationToken);
            }
            catch (SdkException<DeletePaymentTokenError> ex) { throw Translate(ex); }

            _logger.LogInformation("PayPal vaulted card deleted. vaultId={VaultId}", vaultId);
        }, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Transaction search (paged through the whole range)
    // ---------------------------------------------------------------------------------------------

    public Task<TransactionSearchResult> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => RunAsync(async () =>
        {
            var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
            var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

            var transactions = new List<PayPalTransaction>();
            var page = 1;
            var totalPages = 1;
            var pagesRead = 0;

            do
            {
                SearchResponse resp;
                try
                {
                    // SearchTransactions is the SDK's sole Case B operation (SdkException<RawError>).
                    resp = await _client.TransactionSearch.SearchTransactions(
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
                        pageSize: 100,
                        page: page,
                        ct: cancellationToken);
                }
                catch (SdkException<RawError> ex) { throw ToGatewayException(ex.Error); }

                pagesRead++;
                totalPages = resp.TotalPages ?? 1;

                foreach (var detail in resp.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) { continue; }

                    transactions.Add(new PayPalTransaction(
                        info.TransactionId ?? string.Empty,
                        info.TransactionStatus ?? string.Empty,
                        info.TransactionEventCode ?? string.Empty,
                        ParseMoney(info.TransactionAmount?.Value),
                        info.TransactionAmount?.CurrencyCode ?? string.Empty,
                        ParseDate(info.TransactionInitiationDate),
                        info.InvoiceId,
                        info.CustomField));
                }

                page++;
            }
            while (page <= totalPages);

            _logger.LogInformation(
                "PayPal transactions listed. pagesRead={PagesRead} count={Count}", pagesRead, transactions.Count);
            return new TransactionSearchResult(transactions, pagesRead);
        }, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Boundary: common failure translation (transport + broken-body), shared by every call.
    // ---------------------------------------------------------------------------------------------

    private static async Task<T> RunAsync<T>(Func<Task<T>> body, CancellationToken ct)
    {
        try
        {
            return await body();
        }
        catch (PayPalGatewayException)
        {
            // Already translated (includes the challenge/unrenewable STOP conditions) — let it through.
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // genuine caller cancellation — not a provider failure
        }
        catch (JsonException ex)
        {
            // A JsonException reaches here two ways: a drifted 2xx body (outcome unknown) OR a non-2xx body
            // that didn't match its generated {Op}Error (a rejection whose status was destroyed with the
            // SdkException). Neither is a certain outage, so we do NOT collapse to a 5xx — StatusCode stays
            // null and the message is caller-safe (no System.Text.Json detail leaks).
            throw new PayPalGatewayException(
                "PayPal returned a response that could not be processed.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalGatewayException("PayPal could not be reached.", inner: ex);
        }
    }

    private static Task RunAsync(Func<Task> body, CancellationToken ct)
        => RunAsync(async () => { await body(); return true; }, ct);

    // ---------------------------------------------------------------------------------------------
    // Per-operation Case A translators (one branch per TryGet* accessor the map lists, RawError last).
    // ---------------------------------------------------------------------------------------------

    private PayPalGatewayException Translate(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetNoContent(out var noContent)) { return ToGatewayException(noContent); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetNoContent(out var noContent)) { return ToGatewayException(noContent); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetNoContent(out var noContent)) { return ToGatewayException(noContent); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetNoContent(out var noContent)) { return ToGatewayException(noContent); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetNoContent(out var noContent)) { return ToGatewayException(noContent); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    private PayPalGatewayException Translate(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var e)) { return ToGatewayException(e); }
        if (ex.Error.TryGetRawError(out var raw)) { return ToGatewayException(raw); }
        return Unknown();
    }

    // ---------------------------------------------------------------------------------------------
    // Error-payload -> PayPalGatewayException. Typed payloads carry name/message/debug_id/issue but no
    // numeric status (the status-bucketed accessor consumed it); RawError carries the numeric status.
    // ---------------------------------------------------------------------------------------------

    private static PayPalGatewayException ToGatewayException(Error e)
        => new(Compose(e.Message, FirstIssue(e.Details)), statusCode: null, payPalName: e.Name, debugId: e.DebugId);

    private static PayPalGatewayException ToGatewayException(Error1 e)
        => new(Compose(e.Message, FirstIssue(e.Details)), statusCode: null, payPalName: e.Name, debugId: e.DebugId);

    private static PayPalGatewayException ToGatewayException(RawError raw)
        => new($"PayPal returned HTTP {(int)raw.StatusCode}.", statusCode: (int)raw.StatusCode);

    private static PayPalGatewayException Unknown()
        => new("PayPal returned an unrecognized error response.");

    private static string? FirstIssue(IReadOnlyList<ErrorDetails>? details)
        => details is { Count: > 0 } ? details[0].Issue : null;

    private static string? FirstIssue(IReadOnlyList<ErrorDetails1>? details)
        => details is { Count: > 0 } ? details[0].Issue : null;

    private static string Compose(string message, string? issue)
        => string.IsNullOrWhiteSpace(issue) ? message : $"{message} ({issue})";

    // ---------------------------------------------------------------------------------------------
    // Mapping helpers.
    // ---------------------------------------------------------------------------------------------

    private static AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? units)
        => units?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static Address? ToAddress(BillingAddress? billing)
        => billing is null
            ? null
            : new Address
            {
                AddressLine1 = billing.AddressLine1,
                AddressLine2 = billing.AddressLine2,
                AdminArea2 = billing.AdminArea2,
                AdminArea1 = billing.AdminArea1,
                PostalCode = billing.PostalCode,
                CountryCode = billing.CountryCode,
            };

    private string ResolveCurrency(string currency)
        => string.IsNullOrWhiteSpace(currency) ? _settings.ResolvedCurrency : currency.Trim().ToUpperInvariant();

    private static string FormatMoney(decimal amount)
        => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var result) ? result : 0m;

    private static DateTimeOffset ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : default;

    /// <summary>Converts CardDetails month/year into PayPal's "YYYY-MM" expiry, padding a 2-digit year.</summary>
    private static string ToExpiry(string month, string year)
    {
        var y = (year ?? string.Empty).Trim();
        if (y.Length == 2) { y = "20" + y; }
        var m = (month ?? string.Empty).Trim().PadLeft(2, '0');
        return $"{y}-{m}";
    }

    /// <summary>Splits PayPal's "YYYY-MM" expiry back into (month, year).</summary>
    private static (string? Month, string? Year) FromExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry)) { return (null, null); }
        var parts = expiry.Split('-');
        return parts.Length == 2 ? (parts[1], parts[0]) : (null, null);
    }

    /// <summary>
    /// Reads a StringEnum back as its raw wire value (e.g. "CREATED"), or "" when null.
    /// Uses the inherited <c>TypedEnum&lt;string&gt;.Value</c> — NOT <c>ToString()</c>, which the concrete
    /// enum re-synthesizes as a record dump ("AuthorizationStatus { Value = CREATED }").
    /// </summary>
    private static string Wire<TEnum>(TEnum? value) where TEnum : StringEnum<TEnum>
        => value?.Value ?? string.Empty;
}
