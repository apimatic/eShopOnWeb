using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>. All SDK types are confined to this class;
/// it maps application DTOs to/from the SDK and translates every SDK/transport failure into a single
/// <see cref="PaymentGatewayException"/> at this boundary.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    // Whole-call budget (the only true call-level bound; per-attempt timeouts are set on the client).
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(40);

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string Currency => _settings.Currency;

    // ---------------------------------------------------------------- Authorize (hold)

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeInstruction instruction, CancellationToken ct)
    {
        using var cts = Budget(ct);
        var value = FormatAmount(instruction.Amount, instruction.CurrencyCode);

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = instruction.CurrencyCode,
                        Value = value
                    },
                    InvoiceId = instruction.InvoiceId,
                    CustomId = instruction.CustomId
                }
            }
        };

        Order order;
        try
        {
            // Create the order (no payment source yet). Idempotency key stamped as PayPal-Request-Id.
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: instruction.IdempotencyKey + "-create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: cts.Token);
        }
        catch (SdkException<CreateOrderError> ex) { throw TranslateTyped(ex, ex.Error.TryGetError(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }

        StopIfPayerActionRequired(order.Status, order.Links);

        // Build the authorization payment source carrying either the raw card or the vaulted token.
        var card = instruction.VaultId is not null
            ? new CardRequest { VaultId = instruction.VaultId }
            : BuildCard(instruction.Card!);

        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: order.Id,
                payPalMockResponse: null,
                payPalRequestId: instruction.IdempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = card }
                },
                prefer: "return=representation",
                ct: cts.Token);
        }
        catch (SdkException<AuthorizeOrderError> ex) { throw TranslateTyped(ex, ex.Error.TryGetError(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }

        StopIfPayerActionRequired(authResponse.Status, authResponse.Links);

        var authorization = ExtractAuthorization(authResponse);
        if (authorization is null)
            throw new PaymentGatewayException("PayPal did not return an authorization for the order.",
                PaymentFailureKind.Provider);

        return new AuthorizationResult(
            PayPalOrderId: order.Id,
            AuthorizationId: authorization.Id,
            Status: authorization.Status?.Value ?? "UNKNOWN",
            ExpiresAt: ParseDate(authorization.ExpirationTime));
    }

    // ---------------------------------------------------------------- Read authorization

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: cts.Token);
            return new AuthorizationSnapshot(auth.Status?.Value ?? "UNKNOWN", ParseDate(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw TranslateTyped(ex, ex.Error.TryGetError(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Reauthorize (renew stale hold)

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount, Currency) }
                },
                prefer: "return=representation",
                ct: cts.Token);

            return new AuthorizationResult(
                PayPalOrderId: string.Empty,
                AuthorizationId: reauth.Id ?? authorizationId,
                Status: reauth.Status?.Value ?? "UNKNOWN",
                ExpiresAt: ParseDate(reauth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // A hold that can no longer be renewed — report in operator terms.
            var typed = ex.Error.TryGetError(out var e) ? e : null;
            var (message, detail) = Describe(ex, typed);
            throw new AuthorizationNotRenewableException(
                "The payment hold has expired and could not be renewed; a new authorization is required. " + message,
                operatorDetail: detail, inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Capture (take money)

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: cts.Token);

            var breakdown = capture.SellerReceivableBreakdown;
            var gross = ParseAmount(breakdown?.GrossAmount?.Value);
            var currency = breakdown?.GrossAmount?.CurrencyCode ?? Currency;

            return new CaptureResult(
                CaptureId: capture.Id ?? throw new PaymentGatewayException("PayPal capture returned no id.", PaymentFailureKind.Provider),
                Status: capture.Status?.Value ?? "UNKNOWN",
                GrossAmount: gross ?? 0m,
                PayPalFee: ParseAmount(breakdown?.PaypalFee?.Value),
                NetAmount: ParseAmount(breakdown?.NetAmount?.Value),
                CurrencyCode: currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw TranslateTyped(ex, ex.Error.TryGetError(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Void (release hold)

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: cts.Token);
        }
        catch (SdkException<VoidPaymentError> ex) { throw TranslateTyped(ex, ex.Error.TryGetError(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Refund

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new RefundRequest
                {
                    Amount = new Money { CurrencyCode = Currency, Value = FormatAmount(amount, Currency) }
                },
                prefer: "return=representation",
                ct: cts.Token);

            return new RefundResult(
                RefundId: refund.Id ?? throw new PaymentGatewayException("PayPal refund returned no id.", PaymentFailureKind.Provider),
                Status: refund.Status?.Value ?? "UNKNOWN",
                Amount: ParseAmount(refund.Amount?.Value) ?? amount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw TranslateTyped(ex, ex.Error.TryGetError(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Vault a card

    public async Task<VaultCardResult> VaultCardAsync(CardInput card, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new PaymentTokenRequest
                {
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
                ct: cts.Token);

            var cardEntity = token.PaymentSource?.Card;
            return new VaultCardResult(
                VaultId: token.Id ?? throw new PaymentGatewayException("PayPal vault returned no token id.", PaymentFailureKind.Provider),
                Brand: cardEntity?.Brand?.Value,
                LastFourDigits: cardEntity?.LastDigits ?? LastFour(card.Number),
                Expiry: cardEntity?.Expiry ?? card.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw TranslateTyped1(ex, ex.Error.TryGetError1(out var e) ? e : null); }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Delete a vaulted card

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var cts = Budget(ct);
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: cts.Token);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // Already gone at PayPal is fine — the local record is being removed regardless.
            if (ex.Error is not null && ex.Error.TryGetRawError(out RawError raw) && (int)raw.StatusCode == 404)
            {
                _logger.LogWarning($"Vault token {vaultId} already absent at PayPal (404); treating as deleted.");
                return;
            }
            throw TranslateTyped1(ex, ex.Error.TryGetError1(out var e) ? e : null);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }
    }

    // ---------------------------------------------------------------- Reconciliation (paged)

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var cts = Budget(ct);
        var records = new List<TransactionRecord>();
        var startDate = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var endDate = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

        int page = 1;
        int totalPages;
        try
        {
            do
            {
                var response = await _client.TransactionSearch.SearchTransactions(
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
                    ct: cts.Token);

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        records.Add(new TransactionRecord(
                            TransactionId: info.TransactionId ?? string.Empty,
                            InvoiceId: info.InvoiceId,
                            CustomField: info.CustomField,
                            Amount: ParseAmount(info.TransactionAmount?.Value),
                            CurrencyCode: info.TransactionAmount?.CurrencyCode,
                            Status: info.TransactionStatus,
                            Fee: ParseAmount(info.FeeAmount?.Value),
                            Date: ParseDate(info.TransactionInitiationDate),
                            PaypalReferenceId: info.PaypalReferenceId));
                    }
                }

                totalPages = response.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            // Case B: no typed error model — read status/body straight off the RawError.
            var status = (int)ex.Error.StatusCode;
            var kind = status >= 500 ? PaymentFailureKind.Provider : PaymentFailureKind.Rejected;
            throw new PaymentGatewayException(
                "PayPal transaction search failed.", kind, status, operatorDetail: SafeBody(ex.Error), inner: ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Transport(ex); }
        catch (System.Text.Json.JsonException ex) { throw Unreadable(ex); }

        return records;
    }

    // ================================================================ helpers

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return cts;
    }

    private CardRequest BuildCard(CardInput card) => new CardRequest
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = BuildAddress(card.BillingAddress)
    };

    private static PayPalServerSdk.Models.Address? BuildAddress(BillingAddressInput? a)
    {
        if (a is null) return null;
        return new PayPalServerSdk.Models.Address
        {
            CountryCode = a.CountryCode,
            AddressLine1 = a.AddressLine1,
            AddressLine2 = a.AddressLine2,
            AdminArea1 = a.AdminArea1,
            AdminArea2 = a.AdminArea2,
            PostalCode = a.PostalCode
        };
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(OrderAuthorizeResponse response)
    {
        var units = response.PurchaseUnits;
        if (units is null || units.Count == 0) return null;
        var authorizations = units[0].Payments?.Authorizations;
        if (authorizations is null || authorizations.Count == 0) return null;
        return authorizations[0];
    }

    private void StopIfPayerActionRequired(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        var payerAction = status is not null &&
            string.Equals(status.ToString(), OrderStatus.PayerActionRequired.ToString(), StringComparison.OrdinalIgnoreCase);
        if (links is not null)
        {
            foreach (var link in links)
            {
                if (link?.Rel is not null && link.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase))
                    payerAction = true;
            }
        }
        if (payerAction)
        {
            _logger.LogWarning("PayPal requires browser-based buyer approval (PAYER_ACTION_REQUIRED / 3-D Secure); stopping.");
            throw new PaymentGatewayException(
                "This card requires browser-based approval (3-D Secure), which this integration does not support. Payment was not taken.",
                PaymentFailureKind.Rejected);
        }
    }

    private static bool IsTransport(Exception ex) =>
        ex is System.Net.Http.HttpRequestException or TaskCanceledException or OperationCanceledException;

    private PaymentGatewayException Transport(Exception ex)
    {
        _logger.LogWarning($"PayPal request could not be completed: {ex.GetType().Name}.");
        return new PaymentGatewayException("The payment provider could not be reached. Please try again.",
            PaymentFailureKind.Provider, inner: ex);
    }

    private PaymentGatewayException Unreadable(System.Text.Json.JsonException ex)
    {
        _logger.LogWarning("PayPal returned a response that could not be processed.");
        return new PaymentGatewayException("The payment provider returned a response that could not be processed.",
            PaymentFailureKind.Provider, inner: ex);
    }

    // Case A translation. The typed payload is extracted by the caller inside its
    // concrete `catch (SdkException<{Operation}Error>)` — because TryGetError(out Error) is
    // declared on each concrete {Operation}Error, NOT on the ApiError base. These generic
    // helpers therefore take the already-extracted Error and use only the base
    // ApiError.TryGetRawError (SdkException carries no status of its own).
    private PaymentGatewayException TranslateTyped<TError>(SdkException<TError> ex, Error? typed) where TError : ApiError
    {
        var (message, detail) = Describe(ex, typed);
        var kind = Classify(ex, out var status);
        _logger.LogWarning($"PayPal API error ({status?.ToString() ?? "?"}): {detail}");
        return new PaymentGatewayException(message, kind, status, operatorDetail: detail, inner: ex);
    }

    private (string message, string? detail) Describe<TError>(SdkException<TError> ex, Error? typed) where TError : ApiError
    {
        if (typed is not null)
        {
            var detail = BuildDetail(typed);
            var message = string.IsNullOrWhiteSpace(typed.Message)
                ? "The payment provider rejected the request." : typed.Message!;
            return (message, detail);
        }
        if (ex.Error.TryGetRawError(out RawError raw))
            return ("The payment provider reported an error.", SafeBody(raw));
        return ("The payment provider reported an error.", null);
    }

    private static PaymentFailureKind Classify<TError>(SdkException<TError> ex, out int? status) where TError : ApiError
    {
        status = null;
        if (ex.Error.TryGetRawError(out RawError raw))
        {
            status = (int)raw.StatusCode;
            if (status >= 500) return PaymentFailureKind.Provider;
            if (status == 409) return PaymentFailureKind.Conflict;
            return PaymentFailureKind.Rejected;
        }
        // A typed body populated => a 4xx business rejection.
        return PaymentFailureKind.Rejected;
    }

    // Vault operations use the Error1 accessor (TryGetError1), extracted by the caller.
    private PaymentGatewayException TranslateTyped1<TError>(SdkException<TError> ex, Error1? typed) where TError : ApiError
    {
        string message = "The payment provider rejected the request.";
        string? detail = null;
        int? status = null;

        if (typed is not null)
        {
            detail = BuildDetail1(typed);
            if (!string.IsNullOrWhiteSpace(typed.Message)) message = typed.Message!;
        }
        else if (ex.Error.TryGetRawError(out RawError raw))
        {
            status = (int)raw.StatusCode;
            detail = SafeBody(raw);
        }
        var kind = status is >= 500 ? PaymentFailureKind.Provider : PaymentFailureKind.Rejected;
        _logger.LogWarning($"PayPal vault API error: {detail}");
        return new PaymentGatewayException(message, kind, status, operatorDetail: detail, inner: ex);
    }

    private static string? BuildDetail(Error err)
    {
        var issues = new List<string>();
        if (err.Details is not null)
            foreach (var d in err.Details)
                if (!string.IsNullOrEmpty(d?.Issue)) issues.Add(d!.Issue!);
        var joined = issues.Count > 0 ? string.Join("; ", issues) : err.Message;
        return err.DebugId is null ? joined : $"{joined} (debug_id {err.DebugId})";
    }

    private static string? BuildDetail1(Error1 err)
    {
        var issues = new List<string>();
        if (err.Details is not null)
            foreach (var d in err.Details)
                if (!string.IsNullOrEmpty(d?.Issue)) issues.Add(d!.Issue!);
        var joined = issues.Count > 0 ? string.Join("; ", issues) : err.Message;
        return err.DebugId is null ? joined : $"{joined} (debug_id {err.DebugId})";
    }

    private static string? SafeBody(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return null; }
    }

    private static string FormatAmount(decimal amount, string currency)
    {
        var decimals = MinorUnits(currency);
        return Math.Round(amount, decimals, MidpointRounding.AwayFromZero)
            .ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static decimal? ParseAmount(string? value) =>
        string.IsNullOrEmpty(value) ? null
        : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        string.IsNullOrEmpty(value) ? null
        : DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    private static string LastFour(string number)
    {
        var digits = number.Replace(" ", string.Empty).Replace("-", string.Empty);
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static int MinorUnits(string currency) => currency.ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "HUF" or "TWD" => 0,
        "BHD" or "KWD" or "OMR" or "TND" => 3,
        _ => 2
    };
}
