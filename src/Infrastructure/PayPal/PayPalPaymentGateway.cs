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
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of <see cref="IPaymentGateway"/>. All PayPal interaction goes through the
/// PayPalServerSdk here; every SDK/transport failure is translated to <see cref="PaymentGatewayException"/>.
/// Card secrets are read from the request and passed straight to the SDK — never persisted, never logged.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private const string ReturnRepresentation = "return=representation";
    private const int MaxReconciliationPages = 1000; // provider-independent safety bound on the page loop

    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;
    private readonly string _currency;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options,
        ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
        _currency = (options.Value.Currency ?? "USD").Trim().ToUpperInvariant();
    }

    public string CurrencyCode => _currency;

    // ---------------------------------------------------------------- authorize (hold)

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, CancellationToken ct = default)
    {
        if (request.Card is null && string.IsNullOrWhiteSpace(request.VaultId))
            throw new PaymentGatewayException("A card or a saved-card id is required to pay.", PaymentGatewayErrorKind.InvalidRequest);

        var card = request.VaultId is { Length: > 0 } vault
            ? new CardRequest { VaultId = vault }
            : BuildCard(request.Card!);

        var invoiceId = $"{request.OrderReference}-{Guid.NewGuid():N}".Substring(0, Math.Min(127, request.OrderReference.Length + 33));

        var orderBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.CurrencyCode,
                        Value = FormatAmount(request.Amount)
                    },
                    CustomId = request.OrderReference,
                    InvoiceId = invoiceId,
                    Description = request.Description
                }
            },
            PaymentSource = new PaymentSource { Card = card }
        };

        var order = await CallAsync<CreateOrderError, Order>(
            innerCt => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{request.IdempotencyKey}:create",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderBody,
                prefer: ReturnRepresentation,
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null, e.TryGetRawError(out var r) ? r : null),
            "create order", ct);

        if (string.IsNullOrEmpty(order.Id))
            throw new PaymentGatewayException("PayPal did not return an order id.", PaymentGatewayErrorKind.ProviderError);

        StopIfChallenge(order.Status?.Value, "create order");

        // A card + AUTHORIZE order is authorized during creation, so the hold is already on the create
        // response. Only when it is absent (e.g. the order is merely APPROVED) do we authorize explicitly.
        var authorization = ExtractAuthorization(order.PurchaseUnits);

        if (authorization?.Id is not { Length: > 0 })
        {
            var authResponse = await CallAsync<AuthorizeOrderError, OrderAuthorizeResponse>(
                innerCt => _client.Orders.AuthorizeOrder(
                    id: order.Id,
                    payPalMockResponse: null,
                    payPalRequestId: $"{request.IdempotencyKey}:authorize",
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: ReturnRepresentation,
                    ct: innerCt),
                e => (e.TryGetError(out var t) ? t : null, e.TryGetRawError(out var r) ? r : null),
                "authorize order", ct);

            StopIfChallenge(authResponse.Status?.Value, "authorize order");
            authorization = ExtractAuthorization(authResponse.PurchaseUnits);
        }

        if (authorization?.Id is not { Length: > 0 })
            throw new PaymentGatewayException(
                "PayPal accepted the order but returned no authorization to hold the funds.",
                PaymentGatewayErrorKind.ProviderError);

        return new AuthorizationResult
        {
            PayPalOrderId = order.Id!,
            AuthorizationId = authorization.Id!,
            Status = authorization.Status?.Value,
            ExpiresAt = ParseTime(authorization.ExpirationTime)
        };
    }

    public async Task<AuthorizationView> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        var auth = await CallAsync<GetAuthorizedPaymentError, PaymentAuthorization>(
            innerCt => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null,
                  e.TryGetNoContent(out var n) ? n : (e.TryGetRawError(out var r) ? r : null)),
            "get authorization", ct);

        return new AuthorizationView { Status = auth.Status?.Value, ExpiresAt = ParseTime(auth.ExpirationTime) };
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, CancellationToken ct = default)
    {
        var auth = await CallAsync<ReauthorizePaymentError, PaymentAuthorization>(
            innerCt => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth:{authorizationId}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) } },
                prefer: ReturnRepresentation,
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null,
                  e.TryGetNoContent(out var n) ? n : (e.TryGetRawError(out var r) ? r : null)),
            "reauthorize", ct);

        if (auth.Id is not { Length: > 0 })
            throw new PaymentGatewayException("PayPal returned no authorization id on reauthorization.", PaymentGatewayErrorKind.ProviderError);

        return new ReauthorizationResult
        {
            AuthorizationId = auth.Id!,
            Status = auth.Status?.Value,
            ExpiresAt = ParseTime(auth.ExpirationTime)
        };
    }

    // ---------------------------------------------------------------- capture (take money)

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        var capture = await CallAsync<CaptureAuthorizedPaymentError, CapturedPayment>(
            innerCt => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount) },
                    FinalCapture = true
                },
                prefer: ReturnRepresentation,
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null,
                  e.TryGetNoContent(out var n) ? n : (e.TryGetRawError(out var r) ? r : null)),
            "capture", ct);

        if (capture.Id is not { Length: > 0 })
            throw new PaymentGatewayException("PayPal returned no capture id.", PaymentGatewayErrorKind.ProviderError);

        var breakdown = capture.SellerReceivableBreakdown;
        var captured = ParseMoney(breakdown?.GrossAmount) ?? ParseMoney(capture.Amount) ?? amount;

        return new CaptureResult
        {
            CaptureId = capture.Id!,
            Status = capture.Status?.Value,
            CapturedAmount = captured,
            PayPalFee = ParseMoney(breakdown?.PaypalFee),
            NetAmount = ParseMoney(breakdown?.NetAmount)
        };
    }

    // ---------------------------------------------------------------- void (release hold)

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        // A successful void returns HTTP 204 with no body, which the SDK then fails to deserialize into
        // PaymentAuthorization — surfacing as a JsonException that here means SUCCESS, not a bad body.
        // Only an SdkException<VoidPaymentError> (a real 4xx/5xx error body) is an actual failure.
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
            var typed = ex.Error.TryGetError(out var t) ? t : null;
            var raw = ex.Error.TryGetNoContent(out var n) ? n : (ex.Error.TryGetRawError(out var r) ? r : null);
            throw Translate("void authorization", typed, raw, ex);
        }
        catch (JsonException)
        {
            // 204 No Content — the void succeeded and there is no body to read.
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new PaymentGatewayException("PayPal could not be reached for 'void authorization'.",
                PaymentGatewayErrorKind.ProviderUnavailable, innerException: ex);
        }
    }

    // ---------------------------------------------------------------- refund

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken ct = default)
    {
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currencyCode, Value = FormatAmount(amount.Value) } }
            : null;

        var refund = await CallAsync<RefundCapturedPaymentError, Refund>(
            innerCt => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: ReturnRepresentation,
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null,
                  e.TryGetNoContent(out var n) ? n : (e.TryGetRawError(out var r) ? r : null)),
            "refund", ct);

        if (refund.Id is not { Length: > 0 })
            throw new PaymentGatewayException("PayPal returned no refund id.", PaymentGatewayErrorKind.ProviderError);

        return new RefundResult
        {
            RefundId = refund.Id!,
            Status = refund.Status?.Value,
            Amount = ParseMoney(refund.Amount) ?? amount ?? 0m
        };
    }

    // ---------------------------------------------------------------- vault (save card)

    public async Task<SavedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct = default)
    {
        var setup = await CallAsync<CreateSetupTokenError, SetupTokenResponse>(
            innerCt => _client.Vault.CreateSetupToken(
                payPalRequestId: $"setup:{Guid.NewGuid():N}",
                body: new SetupTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = SanitizeCustomerId(request.BuyerReference) },
                    PaymentSource = new SetupTokenRequestPaymentSource { Card = BuildSetupCard(request.Card) }
                },
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null, e.TryGetRawError(out var r) ? r : null),
            "create setup token", ct);

        if (setup.Id is not { Length: > 0 })
            throw new PaymentGatewayException("PayPal returned no setup token id.", PaymentGatewayErrorKind.ProviderError);

        StopIfChallenge(setup.Status?.Value, "vault card");

        var token = await CallAsync<CreatePaymentTokenError, PaymentTokenResponse>(
            innerCt => _client.Vault.CreatePaymentToken(
                payPalRequestId: $"token:{setup.Id}",
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setup.Id!, Type = VaultTokenRequestType.SetupToken }
                    }
                },
                ct: innerCt),
            e => (e.TryGetError(out var t) ? t : null, e.TryGetRawError(out var r) ? r : null),
            "create payment token", ct);

        if (token.Id is not { Length: > 0 })
            throw new PaymentGatewayException("PayPal returned no vault (payment token) id.", PaymentGatewayErrorKind.ProviderError);

        var vaultedCard = token.PaymentSource?.Card;
        return new SavedCardResult
        {
            VaultId = token.Id!,
            CustomerId = token.Customer?.Id,
            Brand = vaultedCard?.Brand?.Value,
            LastDigits = vaultedCard?.LastDigits,
            Expiry = vaultedCard?.Expiry,
            CardholderName = vaultedCard?.Name
        };
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        await CallAsync<DeletePaymentTokenError, bool>(
            async innerCt =>
            {
                await _client.Vault.DeletePaymentToken(id: vaultId, ct: innerCt);
                return true;
            },
            e => (e.TryGetError(out var t) ? t : null, e.TryGetRawError(out var r) ? r : null),
            "delete payment token", ct);
    }

    // ---------------------------------------------------------------- reconciliation (transaction search)

    public async Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var start = FormatSearchDate(from);
        var end = FormatSearchDate(to);
        var results = new List<ReconciliationTransaction>();

        int page = 1;
        int totalPages = 1;
        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: start,
                    endDate: end,
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
                // Case B: RawError carries the status directly.
                var status = (int)ex.Error.StatusCode;
                _logger.LogWarning("PayPal transaction search failed. status={Status} page={Page}", status, page);
                throw new PaymentGatewayException(
                    $"PayPal transaction search failed (HTTP {status}).",
                    ClassifyKind(status, null, null), status, innerException: ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException(
                    "PayPal returned a transaction-search response that could not be processed.",
                    PaymentGatewayErrorKind.ProviderError, innerException: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new PaymentGatewayException("PayPal could not be reached for transaction search.",
                    PaymentGatewayErrorKind.ProviderUnavailable, innerException: ex);
            }

            foreach (var detail in response.TransactionDetails ?? new List<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                results.Add(new ReconciliationTransaction
                {
                    TransactionId = info.TransactionId,
                    Status = info.TransactionStatus,
                    Amount = ParseMoney(info.TransactionAmount),
                    CurrencyCode = info.TransactionAmount?.CurrencyCode,
                    FeeAmount = ParseMoney(info.FeeAmount),
                    Date = ParseTime(info.TransactionInitiationDate),
                    OrderReference = !string.IsNullOrEmpty(info.CustomField) ? info.CustomField : info.InvoiceId
                });
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        }
        while (page <= totalPages && page <= MaxReconciliationPages);

        return results;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<T> CallAsync<TError, T>(
        Func<CancellationToken, Task<T>> op,
        Func<TError, (Error? Typed, RawError? Raw)> read,
        string action,
        CancellationToken ct) where TError : ApiError
    {
        try
        {
            return await op(ct);
        }
        catch (SdkException<TError> ex)
        {
            var (typed, raw) = read(ex.Error);
            throw Translate(action, typed, raw, ex);
        }
        catch (JsonException ex)
        {
            // A drifted 2xx body, or a non-2xx body that didn't match the generated error shape
            // (which replaces the SdkException and destroys the status). Either way: provider fault.
            throw new PaymentGatewayException(
                $"PayPal returned a response for '{action}' that could not be processed.",
                PaymentGatewayErrorKind.ProviderError, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Transport failure or timeout: the outcome of a write is unknown, not known-failed.
            throw new PaymentGatewayException(
                $"PayPal could not be reached for '{action}'.",
                PaymentGatewayErrorKind.ProviderUnavailable, innerException: ex);
        }
    }

    private PaymentGatewayException Translate(string action, Error? typed, RawError? raw, Exception ex)
    {
        int? status = raw is not null ? (int)raw.StatusCode : null;
        string? debugId = typed?.DebugId;
        string? issue = typed?.Details is { Count: > 0 } ? typed.Details[0].Issue : null;
        string? detailText = typed?.Details is { Count: > 0 }
            ? string.Join("; ", typed.Details.Select(d => $"{d.Issue}{(d.Field is not null ? $" @{d.Field}" : string.Empty)}{(d.Description is not null ? $": {d.Description}" : string.Empty)}"))
            : null;
        string providerMsg = typed?.Message ?? typed?.Name ?? (raw is not null ? SafeReadRaw(raw) : ex.Message);
        var kind = ClassifyKind(status, typed?.Name, issue);

        _logger.LogWarning("PayPal {Action} failed. kind={Kind} status={Status} name={Name} details=[{Details}] debug_id={DebugId}",
            action, kind, status, typed?.Name, detailText, debugId);

        var message = $"PayPal '{action}' failed: {providerMsg}"
            + (detailText is not null ? $" [{detailText}]" : string.Empty)
            + (debugId is not null ? $" (debug_id={debugId})" : string.Empty);
        return new PaymentGatewayException(message, kind, status, debugId, ex, issue);
    }

    private static PaymentGatewayErrorKind ClassifyKind(int? status, string? name, string? issue)
    {
        if ((issue is not null && issue.Contains("PAYER_ACTION")) || (name is not null && name.Contains("PAYER_ACTION")))
            return PaymentGatewayErrorKind.ChallengeRequired;

        switch (status)
        {
            case 401:
            case 403:
            case 429:
                return PaymentGatewayErrorKind.ProviderError; // our credentials / our quota
            case 404:
                return PaymentGatewayErrorKind.NotFound;
            case 409:
                return PaymentGatewayErrorKind.Conflict;
            case 400:
            case 422:
                return PaymentGatewayErrorKind.InvalidRequest;
        }

        if (name is not null)
        {
            if (name.Contains("AUTHENTICATION") || name.Contains("NOT_AUTHORIZED") || name.Contains("PERMISSION"))
                return PaymentGatewayErrorKind.ProviderError;
            if (name.Contains("RESOURCE_NOT_FOUND") || name.Contains("INVALID_RESOURCE_ID"))
                return PaymentGatewayErrorKind.NotFound;
        }

        return status >= 500 ? PaymentGatewayErrorKind.ProviderError : PaymentGatewayErrorKind.InvalidRequest;
    }

    private static string SafeReadRaw(RawError raw)
    {
        try { return raw.ReadAsString(); }
        catch { return $"HTTP {(int)raw.StatusCode}"; }
    }

    private static AuthorizationWithAdditionalData? ExtractAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private void StopIfChallenge(string? status, string action)
    {
        if (string.Equals(status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            throw new PaymentGatewayException(
                $"PayPal requires shopper browser approval to complete '{action}' (PAYER_ACTION_REQUIRED). " +
                "A browser approval round-trip is out of scope for this integration.",
                PaymentGatewayErrorKind.ChallengeRequired);
    }

    private static CardRequest BuildCard(CardDetails c) => new()
    {
        Number = c.Number,
        Expiry = c.Expiry,
        SecurityCode = c.SecurityCode,
        Name = c.CardholderName,
        BillingAddress = BuildAddress(c)
    };

    private static SetupTokenRequestCard BuildSetupCard(CardDetails c) => new()
    {
        Number = c.Number,
        Expiry = c.Expiry,
        SecurityCode = c.SecurityCode,
        Name = c.CardholderName,
        BillingAddress = BuildAddress(c)
    };

    private static Address? BuildAddress(CardDetails c)
    {
        if (string.IsNullOrWhiteSpace(c.BillingCountryCode))
            return null;

        return new Address
        {
            CountryCode = c.BillingCountryCode!.Trim().ToUpperInvariant(),
            AddressLine1 = NullIfEmpty(c.BillingLine1),
            AdminArea2 = NullIfEmpty(c.BillingCity),
            AdminArea1 = NullIfEmpty(c.BillingState),
            PostalCode = NullIfEmpty(c.BillingPostalCode)
        };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>PayPal customer merchant_customer_id must match ^[0-9a-zA-Z-_.^*$@#]+$ and be ≤64 chars.</summary>
    private static string SanitizeCustomerId(string reference)
    {
        var chars = reference.Select(ch =>
            char.IsLetterOrDigit(ch) || "-_.^*$@#".IndexOf(ch) >= 0 ? ch : '_').ToArray();
        var sanitized = new string(chars);
        return sanitized.Length <= 64 ? sanitized : sanitized.Substring(0, 64);
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money) =>
        money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        !string.IsNullOrEmpty(value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
