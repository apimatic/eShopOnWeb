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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Services;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkModels = PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The one boundary to PayPal, over the APIMatic-generated SDK. Every SDK failure — a typed API
/// error, a raw error, a transport failure, or an unprocessable body — is translated into a single
/// <see cref="PayPalException"/> with a classified status and, when present, PayPal's issue token.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private const int MaxWindowDays = 31; // PayPal transaction-search range limit.

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currencyCode, CardDetails card,
        string requestId, CancellationToken ct = default)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest> { PurchaseUnit(amount, currencyCode) },
            PaymentSource = new PaymentSource { Card = BuildCard(card) }
        };

        var order = await InvokeAsync<SdkModels.Order, CreateOrderError>("createOrder",
            c => _client.Orders.CreateOrder(null, requestId, null, null, null, body, "return=representation", null, c),
            e => e.TryGetError(out var er) ? FromError(er) : null, ct);

        return await ResolveAuthorizationAsync(order, requestId, InstrumentFromCard(card), ct);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currencyCode,
        string vaultTokenId, string requestId, CancellationToken ct = default)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest> { PurchaseUnit(amount, currencyCode) },
            PaymentSource = new PaymentSource { Card = new CardRequest { VaultId = vaultTokenId } }
        };

        var order = await InvokeAsync<SdkModels.Order, CreateOrderError>("createOrder",
            c => _client.Orders.CreateOrder(null, requestId, null, null, null, body, "return=representation", null, c),
            e => e.TryGetError(out var er) ? FromError(er) : null, ct);

        return await ResolveAuthorizationAsync(order, requestId, null, ct);
    }

    /// <summary>
    /// The SDK map cannot settle whether create-with-card already authorizes in one call or still
    /// needs an explicit AuthorizeOrder, so decide off the live response: use the authorization if the
    /// create returned one, otherwise call AuthorizeOrder. A browser-approval challenge is reported.
    /// </summary>
    private async Task<AuthorizationResult> ResolveAuthorizationAsync(SdkModels.Order order,
        string requestId, string? instrument, CancellationToken ct)
    {
        if (string.Equals(order.Status?.Value, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayPalException(402,
                "PayPal requires the shopper to approve this card payment in a browser (PAYER_ACTION_REQUIRED). " +
                "The direct-card flow cannot proceed for this card.", "PAYER_ACTION_REQUIRED");
        }

        var auth = FindAuthorization(order.PurchaseUnits);
        if (auth is null)
        {
            var authorized = await InvokeAsync<SdkModels.OrderAuthorizeResponse, AuthorizeOrderError>("authorizeOrder",
                c => _client.Orders.AuthorizeOrder(order.Id!, null, requestId, null, null, null, "return=representation", null, c),
                e => e.TryGetError(out var er) ? FromError(er) : null, ct);

            auth = FindAuthorization(authorized.PurchaseUnits);
        }

        if (auth?.Id is null)
        {
            throw new PayPalException(502, "PayPal did not return an authorization for the order.");
        }

        return new AuthorizationResult(
            order.Id ?? string.Empty,
            auth.Id,
            auth.Status?.Value ?? "UNKNOWN",
            ToDateTimeOffset(auth.ExpirationTime),
            instrument);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        var capture = await InvokeAsync<SdkModels.CapturedPayment, CaptureAuthorizedPaymentError>("captureAuthorizedPayment",
            c => _client.Payments.CaptureAuthorizedPayment(authorizationId, null, requestId, null, null, "return=representation", null, c),
            e => e.TryGetError(out var er) ? FromError(er) : null, ct);

        var breakdown = capture.SellerReceivableBreakdown;
        var gross = MoneyFormatter.TryParse(breakdown?.GrossAmount?.Value)
                    ?? MoneyFormatter.TryParse(capture.Amount?.Value) ?? 0m;
        var fee = MoneyFormatter.TryParse(breakdown?.PaypalFee?.Value);
        var net = MoneyFormatter.TryParse(breakdown?.NetAmount?.Value);
        var currency = capture.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? string.Empty;

        return new CaptureResult(capture.Id ?? string.Empty, capture.Status?.Value ?? "UNKNOWN", gross, fee, net, currency);
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode,
        string requestId, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest { Amount = MoneyOf(amount, currencyCode) };

        var reauth = await InvokeAsync<SdkModels.PaymentAuthorization, ReauthorizePaymentError>("reauthorizePayment",
            c => _client.Payments.ReauthorizePayment(authorizationId, requestId, null, body, "return=representation", null, c),
            e => e.TryGetError(out var er) ? FromError(er) : null, ct);

        return new ReauthorizationResult(reauth.Id ?? authorizationId, reauth.Status?.Value ?? "UNKNOWN",
            ToDateTimeOffset(reauth.ExpirationTime));
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        // A successful void returns HTTP 204 with no body, which the SDK cannot deserialize into
        // PaymentAuthorization and surfaces as JsonException. A genuine failure instead arrives as
        // SdkException<VoidPaymentError> (handled first), so an empty-body JsonException here means the
        // void succeeded — treat it as success rather than an unprocessable response.
        try
        {
            await _client.Payments.VoidPayment(authorizationId, null, null, requestId, "return=minimal", null, ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("voidPayment", ex.Error, e => e.TryGetError(out var er) ? FromError(er) : null, ex);
        }
        catch (JsonException)
        {
            // 204 No Content success body — nothing to parse.
        }
        catch (AuthSchemeException ex)
        {
            throw new PayPalException(500, "PayPal authentication could not be applied for voidPayment.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException(503, "PayPal is unreachable (voidPayment).", null, ex);
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string requestId,
        CancellationToken ct = default)
    {
        var body = amount is null ? null : new RefundRequest { Amount = MoneyOf(amount.Value, currencyCode) };

        var refund = await InvokeAsync<SdkModels.Refund, RefundCapturedPaymentError>("refundCapturedPayment",
            c => _client.Payments.RefundCapturedPayment(captureId, null, requestId, null, body, "return=representation", null, c),
            e => e.TryGetError(out var er) ? FromError(er) : null, ct);

        var refundedAmount = MoneyFormatter.TryParse(refund.Amount?.Value) ?? amount ?? 0m;
        var currency = refund.Amount?.CurrencyCode ?? currencyCode;
        return new RefundResult(refund.Id ?? string.Empty, refund.Status?.Value ?? "UNKNOWN", refundedAmount, currency);
    }

    public async Task<VaultCardResult> VaultCardAsync(CardDetails card, string requestId, CancellationToken ct = default)
    {
        // Two-step vault: setup token from the raw card, then a permanent payment token from the setup token.
        var setupBody = new SetupTokenRequest
        {
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = NullIfEmpty(card.SecurityCode),
                    Name = NullIfEmpty(card.Name),
                    BillingAddress = BuildAddress(card)
                }
            }
        };

        var setup = await InvokeAsync<SdkModels.SetupTokenResponse, CreateSetupTokenError>("createSetupToken",
            c => _client.Vault.CreateSetupToken(requestId, setupBody, null, c),
            e => e.TryGetError1(out var er) ? FromError1(er) : null, ct);

        var tokenBody = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest { Id = setup.Id!, Type = VaultTokenRequestType.SetupToken }
            }
        };

        var token = await InvokeAsync<SdkModels.PaymentTokenResponse, CreatePaymentTokenError>("createPaymentToken",
            c => _client.Vault.CreatePaymentToken(requestId + "-pt", tokenBody, null, c),
            e => e.TryGetError1(out var er) ? FromError1(er) : null, ct);

        var entity = token.PaymentSource?.Card;
        var brand = entity?.Brand?.Value ?? "Card";
        var last4 = entity?.LastDigits ?? Last4(card.Number);
        var expiry = entity?.Expiry ?? card.Expiry;
        var name = entity?.Name ?? card.Name;

        return new VaultCardResult(token.Id ?? string.Empty, brand, last4, expiry, name);
    }

    public async Task DeleteVaultedCardAsync(string vaultTokenId, CancellationToken ct = default)
    {
        await InvokeVoidAsync<DeletePaymentTokenError>("deletePaymentToken",
            c => _client.Vault.DeletePaymentToken(vaultTokenId, null, c),
            e => e.TryGetError1(out var er) ? FromError1(er) : null, ct);
    }

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var records = new List<TransactionRecord>();

        // PayPal only serves transactions from the last ~3 years and up to now. Clamp the requested
        // range into that window (PayPal holds no data outside it anyway) so an older/future bound does
        // not make the whole report fail with a range error.
        var now = DateTimeOffset.UtcNow;
        var earliest = now.AddYears(-3).AddDays(1);
        var windowStart = from < earliest ? earliest : from;
        var rangeEnd = to > now ? now : to;

        // Cover the whole range: split into <=31-day windows (PayPal's limit) and, within each window,
        // follow pagination to the last page rather than stopping at the first.
        while (windowStart < rangeEnd)
        {
            var windowEnd = windowStart.AddDays(MaxWindowDays);
            if (windowEnd > rangeEnd) windowEnd = rangeEnd;

            var page = 1;
            int totalPages;
            do
            {
                var response = await SearchPageAsync(windowStart, windowEnd, page, ct);
                if (response.TransactionDetails is not null)
                {
                    records.AddRange(response.TransactionDetails.Select(MapTransaction));
                }
                totalPages = response.TotalPages ?? 1;
                page++;
            }
            while (page <= totalPages);

            windowStart = windowEnd;
        }

        return records;
    }

    private async Task<SdkModels.SearchResponse> SearchPageAsync(DateTimeOffset from, DateTimeOffset to,
        int page, CancellationToken ct)
    {
        try
        {
            return await _client.TransactionSearch.SearchTransactions(
                FormatSearchDate(from), FormatSearchDate(to),
                null, null, null, null, null, null, null, null,
                "transaction_info", "Y", 100, page, null, ct);
        }
        catch (SdkException<RawError> ex) // TransactionSearch is the SDK's only Case B operation.
        {
            throw TranslateRaw("searchTransactions", ex.Error, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PayPalException(500, "PayPal authentication could not be applied for searchTransactions.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException(502, "PayPal returned a transaction-search response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException(503, "PayPal is unreachable (searchTransactions).", null, ex);
        }
    }

    private static TransactionRecord MapTransaction(SdkModels.TransactionDetails detail)
    {
        var info = detail.TransactionInfo;
        return new TransactionRecord(
            info?.TransactionId ?? string.Empty,
            info?.TransactionStatus,
            MoneyFormatter.TryParse(info?.TransactionAmount?.Value),
            info?.TransactionAmount?.CurrencyCode,
            ToDateTimeOffset(info?.TransactionInitiationDate));
    }

    // ---- request builders ----

    private static PurchaseUnitRequest PurchaseUnit(decimal amount, string currencyCode) => new()
    {
        Amount = new AmountWithBreakdown
        {
            CurrencyCode = currencyCode,
            Value = MoneyFormatter.Format(amount, currencyCode)
        }
    };

    private static Money MoneyOf(decimal amount, string currencyCode) => new()
    {
        CurrencyCode = currencyCode,
        Value = MoneyFormatter.Format(amount, currencyCode)
    };

    private static CardRequest BuildCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = NullIfEmpty(card.SecurityCode),
        Name = NullIfEmpty(card.Name),
        BillingAddress = BuildAddress(card)
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static SdkModels.Address? BuildAddress(CardDetails card)
    {
        var hasAddress = !(string.IsNullOrWhiteSpace(card.AddressLine1)
            && string.IsNullOrWhiteSpace(card.City)
            && string.IsNullOrWhiteSpace(card.State)
            && string.IsNullOrWhiteSpace(card.PostalCode));
        if (!hasAddress)
        {
            return null; // Don't send a half-empty billing address.
        }

        return new SdkModels.Address
        {
            AddressLine1 = card.AddressLine1,
            AddressLine2 = card.AddressLine2,
            AdminArea2 = card.City,
            AdminArea1 = card.State,
            PostalCode = card.PostalCode,
            CountryCode = card.CountryCode
        };
    }

    private static string InstrumentFromCard(CardDetails card) => $"Card ****{Last4(card.Number)}";

    private static string Last4(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length >= 4 ? digits[^4..] : digits;
    }

    private static SdkModels.AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? units) =>
        units?
            .SelectMany(pu => pu.Payments?.Authorizations ?? new List<SdkModels.AuthorizationWithAdditionalData>())
            .FirstOrDefault();

    private static DateTimeOffset? ToDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto) ? dto : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    // ---- error boundary ----

    private async Task<T> InvokeAsync<T, TErr>(string op, Func<CancellationToken, Task<T>> call,
        Func<TErr, PayPalFault?> readTyped, CancellationToken ct) where TErr : ApiError
    {
        try
        {
            return await call(ct);
        }
        catch (SdkException<TErr> ex)
        {
            throw Translate(op, ex.Error, readTyped, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PayPalException(500, $"PayPal authentication could not be applied for {op}.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException(502, $"PayPal returned a response for {op} that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException(503, $"PayPal is unreachable ({op}).", null, ex);
        }
    }

    private async Task InvokeVoidAsync<TErr>(string op, Func<CancellationToken, Task> call,
        Func<TErr, PayPalFault?> readTyped, CancellationToken ct) where TErr : ApiError
    {
        try
        {
            await call(ct);
        }
        catch (SdkException<TErr> ex)
        {
            throw Translate(op, ex.Error, readTyped, ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PayPalException(500, $"PayPal authentication could not be applied for {op}.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException(502, $"PayPal returned a response for {op} that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException(503, $"PayPal is unreachable ({op}).", null, ex);
        }
    }

    private PayPalException Translate<TErr>(string op, TErr error, Func<TErr, PayPalFault?> readTyped, Exception inner)
        where TErr : ApiError
    {
        var fault = readTyped(error);
        if (fault is null && error.TryGetRawError(out RawError raw))
        {
            fault = new PayPalFault(ClassifyStatus((int)raw.StatusCode), SafeBody(raw, op), null);
        }
        fault ??= new PayPalFault(502, $"PayPal {op} failed.", null);

        _logger.LogWarning("PayPal {0} failed: status={1} issue={2} message={3} diagnostic={4}",
            op, fault.Status, fault.Issue ?? "-", fault.Message, fault.Diagnostic ?? "-");
        return new PayPalException(fault.Status, fault.Message, fault.Issue, inner);
    }

    private PayPalException TranslateRaw(string op, RawError raw, Exception inner)
    {
        var status = ClassifyStatus((int)raw.StatusCode);
        var message = SafeBody(raw, op);
        _logger.LogWarning("PayPal {0} failed: status={1} message={2}", op, status, message);
        return new PayPalException(status, message, null, inner);
    }

    private static int ClassifyStatus(int providerStatus) => providerStatus switch
    {
        404 => 404,
        409 => 409,
        >= 400 and < 500 and not (401 or 403) => 422, // caller-actionable validation/declines
        _ => 502                                       // auth (401/403), 5xx, unknown → upstream failure
    };

    private static string SafeBody(RawError raw, string op)
    {
        string body;
        try { body = raw.ReadAsString(); }
        catch { body = string.Empty; }

        if (string.IsNullOrWhiteSpace(body))
        {
            return $"PayPal {op} failed with HTTP {(int)raw.StatusCode}.";
        }
        if (body.Length > 600) body = body[..600];
        return $"PayPal {op} failed (HTTP {(int)raw.StatusCode}): {body}";
    }

    private static PayPalFault FromError(Error error) =>
        new(422, ComposeMessage(error.Message, error.Details?.FirstOrDefault()?.Issue,
            error.Details?.FirstOrDefault()?.Description), error.Details?.FirstOrDefault()?.Issue,
            $"name={error.Name} debugId={error.DebugId} details=[{DescribeDetails(error.Details?.Select(d => $"{d.Issue}:{d.Description}:{d.Field}"))}]");

    private static PayPalFault FromError1(Error1 error) =>
        new(422, ComposeMessage(error.Message, error.Details?.FirstOrDefault()?.Issue,
            error.Details?.FirstOrDefault()?.Description), error.Details?.FirstOrDefault()?.Issue,
            $"name={error.Name} debugId={error.DebugId} details=[{DescribeDetails(error.Details?.Select(d => $"{d.Issue}:{d.Description}:{d.Field}"))}]");

    private static string DescribeDetails(IEnumerable<string>? details) =>
        details is null ? string.Empty : string.Join(" | ", details);

    private static string ComposeMessage(string? message, string? issue, string? description)
    {
        var text = message ?? "PayPal rejected the request.";
        var detail = description ?? issue;
        return detail is null ? text : $"{text} ({detail})";
    }

    private sealed record PayPalFault(int Status, string Message, string? Issue, string? Diagnostic = null);
}
