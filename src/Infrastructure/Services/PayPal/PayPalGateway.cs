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
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models.Enums;
using M = PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// The single implementation of <see cref="IPayPalGateway"/> — owns the paypal-sdk and translates between the
/// application's neutral PayPal contracts and the SDK's models/exceptions. Never persists or logs raw card data.
/// </summary>
public sealed class PayPalGateway : IPayPalGateway
{
    private const string PreferRepresentation = "return=representation";

    // PayPal enforces invoice_id uniqueness across a merchant's captured payments. custom_id (our
    // reconciliation match key) carries the eShop order id and has no such constraint, so we keep it as
    // the order id while making invoice_id globally unique with a per-process nonce. In production the
    // order id is already unique forever; the nonce additionally survives the in-memory DB resetting ids
    // to 1 on restart (which would otherwise replay an invoice_id already captured in a previous run).
    private static readonly string InvoiceNonce = Guid.NewGuid().ToString("N").Substring(0, 12);

    // Issue tokens (substring, case-insensitive) that mean an authorization can no longer be captured/renewed.
    // The SDK models ErrorDetails.Issue as a free string, so we match on keywords rather than a fixed constant.
    private static readonly string[] UnusableIssueTokens =
    {
        "EXPIRED", "VOID", "ALREADY_CAPTURED", "PREVIOUSLY_CAPTURED", "CANNOT_BE_CAPTURED",
        "AUTH_CAPTURE_NOT_ALLOWED", "MAX_CAPTURE", "REAUTHORIZATION", "PAYMENT_ALREADY_DONE",
        "ORDER_ALREADY_CAPTURED", "COMPLETED"
    };

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public Task<AuthorizationResult> AuthorizeWithCardAsync(
        Money amount, CardDetails card, string orderReference, string idempotencyKey, CancellationToken ct)
    {
        var paymentSource = new M.PaymentSource { Card = BuildCardRequest(card) };
        return AuthorizeInternalAsync(amount, paymentSource, orderReference, idempotencyKey, ct);
    }

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(
        Money amount, string vaultId, string orderReference, string idempotencyKey, CancellationToken ct)
    {
        // Saved-card reuse path is payment_source.card.vault_id — NOT the Token variant (that is for billing agreements).
        var paymentSource = new M.PaymentSource { Card = new M.CardRequest { VaultId = vaultId } };
        return AuthorizeInternalAsync(amount, paymentSource, orderReference, idempotencyKey, ct);
    }

    private async Task<AuthorizationResult> AuthorizeInternalAsync(
        Money amount, M.PaymentSource paymentSource, string orderReference, string idempotencyKey, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();

        var request = new M.OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<M.PurchaseUnitRequest>
            {
                new M.PurchaseUnitRequest
                {
                    Amount = ToAmountWithBreakdown(amount),
                    // custom_id carries the eShop order id so reconciliation can line PayPal txns up against
                    // eShop orders; invoice_id must be unique per merchant, so it gets the nonce suffix.
                    CustomId = orderReference,
                    InvoiceId = $"{orderReference}-{InvoiceNonce}"
                }
            },
            PaymentSource = paymentSource
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: request,
                prefer: PreferRepresentation,
                ct: ct);

            var orderId = order.Id ?? string.Empty;

            // §3 path 1: 3DS / browser-approval signal → STOP (we deliberately do not build an approval round-trip).
            if (RequiresBrowserApproval(order.Status, order.Links))
            {
                throw new PayPalChallengeException(
                    $"PayPal requires shopper approval in the browser for order {orderId} (status {order.Status}).");
            }

            // §3 path 2: authorization returned inline on the created order.
            var inline = FirstAuthorization(order.PurchaseUnits);
            if (inline is not null)
            {
                _logger.LogInformation(
                    "PayPal order {OrderId} authorized inline as {AuthorizationId} ({Status}).",
                    orderId, inline.Id ?? "", inline.Status?.ToString() ?? "");
                return ToAuthorizationResult(orderId, inline);
            }

            // §3 path 3: not inline → issue a separate authorize call against the created order.
            var authorized = await _client.Orders.AuthorizeOrder(
                id: orderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: PreferRepresentation,
                ct: ct);

            if (RequiresBrowserApproval(authorized.Status, authorized.Links))
            {
                throw new PayPalChallengeException(
                    $"PayPal requires shopper approval in the browser for order {orderId} (status {authorized.Status}).");
            }

            var auth = FirstAuthorization(authorized.PurchaseUnits);
            if (auth is not null)
            {
                _logger.LogInformation(
                    "PayPal order {OrderId} authorized as {AuthorizationId} ({Status}).",
                    orderId, auth.Id ?? "", auth.Status?.ToString() ?? "");
                return ToAuthorizationResult(orderId, auth);
            }

            // No authorization and no explicit approval status. If the card carried a 3DS authentication result,
            // this is a pending challenge; otherwise it is an unexpected outcome. (Whether the sandbox wire
            // populates these fields for a test card is UNVERIFIED — hence the defensive branch.)
            if (HasAuthenticationResult(order.PaymentSource) || HasAuthenticationResult(authorized.PaymentSource))
            {
                throw new PayPalChallengeException(
                    $"PayPal returned no usable authorization for order {orderId} and a card authentication result is present — shopper approval is required.");
            }

            throw new PayPalException(
                $"PayPal returned no authorization for order {orderId} (status {authorized.Status}).",
                httpStatusCode: scope.StatusCode);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToApiException(ReadError(ex.Error), ex, "create the PayPal order");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw ToApiException(ReadError(ex.Error), ex, "authorize the PayPal order");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "authorize with card");
        }
    }

    public async Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            return new AuthorizationState(
                auth.Id ?? authorizationId,
                auth.Status?.Value ?? string.Empty,
                ParseTimestamp(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw ToApiException(ReadError(ex.Error), ex, "read the PayPal authorization");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "read the authorization");
        }
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, Money amount, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        var body = new M.ReauthorizeRequest { Amount = ToMoney(amount) };
        try
        {
            var auth = await _client.Payments.ReauthorizePayment(
                authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            return new AuthorizationResult(
                string.Empty,
                auth.Id ?? authorizationId,
                auth.Status?.Value ?? string.Empty,
                ParseTimestamp(auth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw ToUnusableOrApiException(ReadError(ex.Error), ex, "reauthorize the PayPal authorization");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "reauthorize");
        }
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        var body = new M.CaptureRequest { FinalCapture = true };
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            var currency = breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? string.Empty;
            var result = new CaptureResult(
                capture.Id ?? string.Empty,
                capture.Status?.Value ?? string.Empty,
                ParseDecimal(breakdown?.GrossAmount?.Value),
                ParseDecimal(breakdown?.PaypalFee?.Value),
                ParseDecimal(breakdown?.NetAmount?.Value),
                currency);

            _logger.LogInformation(
                "Captured PayPal authorization {AuthorizationId} as capture {CaptureId} ({Status}), net {Net} {Currency}.",
                authorizationId, result.CaptureId, result.Status, result.NetAmount, result.CurrencyCode);
            return result;
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw ToUnusableOrApiException(ReadError(ex.Error), ex, "capture the PayPal authorization");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "capture the authorization");
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                ct: ct);

            _logger.LogInformation("Voided PayPal authorization {AuthorizationId}.", authorizationId);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw ToUnusableOrApiException(ReadError(ex.Error), ex, "void the PayPal authorization");
        }
        catch (JsonException ex)
        {
            // A successful void returns 204 No Content. The SDK still tries to deserialize the (empty)
            // body into a PaymentAuthorization and throws — so a 2xx here means the void actually succeeded.
            if (scope.StatusCode is >= 200 and < 300)
            {
                _logger.LogInformation("Voided PayPal authorization {AuthorizationId} (204 No Content).", authorizationId);
                return;
            }
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "void the authorization");
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, Money? amount, string idempotencyKey, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        // Partial refund → send an amount; full refund → send no body.
        var body = amount is null ? null : new M.RefundRequest { Amount = ToMoney(amount) };
        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            var refundedAmount = ParseDecimalOrNull(refund.Amount?.Value) ?? amount?.Value ?? 0m;
            var currency = refund.Amount?.CurrencyCode ?? amount?.CurrencyCode ?? string.Empty;

            _logger.LogInformation(
                "Refunded PayPal capture {CaptureId} as refund {RefundId} ({Status}), {Amount} {Currency}.",
                captureId, refund.Id ?? string.Empty, refund.Status?.Value ?? string.Empty, refundedAmount, currency);

            return new RefundResult(
                refund.Id ?? string.Empty,
                refund.Status?.Value ?? string.Empty,
                refundedAmount,
                currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw ToApiException(ReadError(ex.Error), ex, "refund the PayPal capture");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "refund the capture");
        }
    }

    public async Task<VaultedCard> VaultCardAsync(CardDetails card, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        var body = new M.PaymentTokenRequest
        {
            PaymentSource = new M.PaymentTokenRequestPaymentSource
            {
                Card = new M.PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = ToSdkExpiry(card.ExpiryMonth, card.ExpiryYear),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                ct: ct);

            var vaulted = response.PaymentSource?.Card;
            var (month, year) = SplitExpiry(vaulted?.Expiry);
            var result = new VaultedCard(
                response.Id ?? string.Empty,
                vaulted?.LastDigits ?? string.Empty,
                vaulted?.Brand?.Value,
                month,
                year);

            _logger.LogInformation(
                "Vaulted a card as {VaultId} (**** {Last4}, {Brand}).",
                result.VaultId, result.Last4, result.Brand ?? "unknown");
            return result;
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw ToApiException(ReadError(ex.Error), ex, "vault the card");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "vault the card");
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        try
        {
            await _client.Vault.DeletePaymentToken(vaultId, ct: ct);
            _logger.LogInformation("Deleted vaulted card {VaultId}.", vaultId);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw ToApiException(ReadError(ex.Error), ex, "delete the vaulted card");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "delete the vaulted card");
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> ListTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        using var scope = PayPalCallScope.Begin();
        var records = new List<PayPalTransactionRecord>();
        var startDate = ToIso8601(from);
        var endDate = ToIso8601(to);

        var page = 1;
        var totalPages = 1;
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
                    ct: ct);

                totalPages = response.TotalPages ?? 1;

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null)
                        {
                            continue;
                        }

                        var orderReference = !string.IsNullOrEmpty(info.CustomField) ? info.CustomField : info.InvoiceId;
                        var date = ParseTimestamp(info.TransactionInitiationDate)
                            ?? ParseTimestamp(info.TransactionUpdatedDate)
                            ?? from;

                        records.Add(new PayPalTransactionRecord(
                            info.TransactionId ?? string.Empty,
                            info.TransactionStatus ?? string.Empty,
                            ParseDecimal(info.TransactionAmount?.Value),
                            info.TransactionAmount?.CurrencyCode ?? string.Empty,
                            orderReference,
                            date));
                    }
                }

                page++;
            }
            while (page <= totalPages);

            return records;
        }
        catch (SdkException<RawError> ex) // SearchTransactions is the SDK's only Case-B operation.
        {
            throw ToApiException(ExtractRaw(ex.Error), ex, "list PayPal transactions");
        }
        catch (JsonException ex)
        {
            throw FromJson(ex, scope);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw FromTransport(ex, "list transactions");
        }
    }

    // ---- request builders ----

    private static M.AmountWithBreakdown ToAmountWithBreakdown(Money amount) => new()
    {
        CurrencyCode = amount.CurrencyCode,
        Value = FormatAmount(amount.Value)
    };

    private static M.Money ToMoney(Money amount) => new()
    {
        CurrencyCode = amount.CurrencyCode,
        Value = FormatAmount(amount.Value)
    };

    private static M.CardRequest BuildCardRequest(CardDetails card) => new()
    {
        Name = card.CardholderName,
        Number = card.Number,
        Expiry = ToSdkExpiry(card.ExpiryMonth, card.ExpiryYear),
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static M.Address? ToAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new M.Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    // ---- response mapping ----

    private static AuthorizationResult ToAuthorizationResult(string orderId, M.AuthorizationWithAdditionalData auth) =>
        new(orderId, auth.Id ?? string.Empty, auth.Status?.Value ?? string.Empty, ParseTimestamp(auth.ExpirationTime));

    private static M.AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<M.PurchaseUnit>? purchaseUnits)
    {
        if (purchaseUnits is null)
        {
            return null;
        }

        foreach (var unit in purchaseUnits)
        {
            var authorizations = unit.Payments?.Authorizations;
            if (authorizations is not null && authorizations.Count > 0)
            {
                return authorizations[0];
            }
        }

        return null;
    }

    private static bool RequiresBrowserApproval(OrderStatus? status, IReadOnlyList<M.LinkDescription>? links)
    {
        if (status is { } s && s == OrderStatus.PayerActionRequired)
        {
            return true;
        }

        if (links is not null)
        {
            foreach (var link in links)
            {
                if (string.Equals(link.Rel, "payer-action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasAuthenticationResult(M.PaymentSourceResponse? paymentSource) =>
        paymentSource?.Card?.AuthenticationResult is not null;

    private static bool HasAuthenticationResult(M.OrderAuthorizeResponsePaymentSource? paymentSource) =>
        paymentSource?.Card?.AuthenticationResult is not null;

    // ---- error translation ----

    private readonly record struct PayPalErrorInfo(
        int? Status, string? Name, string? Message, string? DebugId, IReadOnlyList<string> Issues);

    private static PayPalErrorInfo ReadError(CreateOrderError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(GetAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetNoContent(out var noContent)) return ExtractRaw(noContent);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetNoContent(out var noContent)) return ExtractRaw(noContent);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetNoContent(out var noContent)) return ExtractRaw(noContent);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(VoidPaymentError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetNoContent(out var noContent)) return ExtractRaw(noContent);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var typed)) return Extract(typed);
        if (error.TryGetNoContent(out var noContent)) return ExtractRaw(noContent);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var typed)) return Extract(typed);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo ReadError(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var typed)) return Extract(typed);
        if (error.TryGetRawError(out var raw)) return ExtractRaw(raw);
        return EmptyError();
    }

    private static PayPalErrorInfo Extract(M.Error error) => new(
        PayPalCallScope.Current?.StatusCode,
        error.Name,
        error.Message,
        error.DebugId,
        error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i)).ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>());

    private static PayPalErrorInfo Extract(M.Error1 error) => new(
        PayPalCallScope.Current?.StatusCode,
        error.Name,
        error.Message,
        error.DebugId,
        error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrWhiteSpace(i)).ToList()
            ?? (IReadOnlyList<string>)Array.Empty<string>());

    private static PayPalErrorInfo ExtractRaw(RawError raw)
    {
        string? body = null;
        try
        {
            body = Truncate(raw.ReadAsString());
        }
        catch
        {
            // Body was unreadable; the status code alone still classifies the failure.
        }

        return new PayPalErrorInfo((int)raw.StatusCode, null, body, null, Array.Empty<string>());
    }

    private static PayPalErrorInfo EmptyError() =>
        new(PayPalCallScope.Current?.StatusCode, null, null, null, Array.Empty<string>());

    private static PayPalException ToApiException(PayPalErrorInfo info, Exception inner, string action)
    {
        var issueName = info.Issues.FirstOrDefault() ?? info.Name;
        return new PayPalException(BuildMessage(info, action), info.Status, issueName, info.DebugId, inner);
    }

    private PayPalException ToUnusableOrApiException(PayPalErrorInfo info, Exception inner, string action)
    {
        if (IsUnusable(info.Issues))
        {
            // Put PayPal's Name + Message + each issue verbatim so an operator can act on it.
            return new AuthorizationUnusableException(BuildMessage(info, action));
        }

        return ToApiException(info, inner, action);
    }

    private PayPalException FromJson(JsonException inner, PayPalCallScope scope)
    {
        var status = scope.StatusCode;
        var statusText = status?.ToString(CultureInfo.InvariantCulture) ?? "unknown";

        // A NON-2xx body that did not match the operation's generated {Op}Error shape: the JsonException replaced
        // the SdkException and took the status with it — but we captured it out of band. Surface it as the
        // deterministic 4xx rejection it is, NOT as a 5xx outage.
        if (status is >= 400 and < 500)
        {
            _logger.LogWarning("PayPal returned a {Status} whose error body could not be parsed to the expected shape.", statusText);
            return new PayPalException(
                "PayPal rejected the request; its error response could not be parsed to the expected shape.",
                httpStatusCode: status,
                inner: inner);
        }

        // A drifted/malformed 2xx body (or an unknown status): the outcome is genuinely unknown.
        _logger.LogWarning("PayPal returned a response body that could not be processed (status {Status}).", statusText);
        return new PayPalException(
            "The PayPal response could not be processed.",
            httpStatusCode: status,
            inner: inner);
    }

    private PayPalException FromTransport(Exception inner, string action)
    {
        _logger.LogWarning("PayPal was unreachable while attempting to {Action}.", action);
        return new PayPalException($"PayPal was unreachable while attempting to {action}.", inner: inner);
    }

    private static string BuildMessage(PayPalErrorInfo info, string action)
    {
        var name = string.IsNullOrWhiteSpace(info.Name) ? "PayPal error" : info.Name!;
        var message = string.IsNullOrWhiteSpace(info.Message) ? $"Failed to {action}." : info.Message!;
        var issues = info.Issues.Count > 0 ? $" Issues: {string.Join("; ", info.Issues)}." : string.Empty;
        var debug = string.IsNullOrWhiteSpace(info.DebugId) ? string.Empty : $" (debug_id {info.DebugId})";
        return $"Failed to {action}: {name} — {message}.{issues}{debug}";
    }

    private static bool IsUnusable(IReadOnlyList<string> issues) =>
        issues.Any(issue => UnusableIssueTokens.Any(token =>
            issue.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));

    // ---- value helpers ----

    private static string FormatAmount(decimal value) =>
        // Cent-accurate decimal string, invariant culture (USD → 2 dp). PayPal amounts are strings, never numbers.
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string ToSdkExpiry(string month, string year) =>
        // SDK card Expiry is "YYYY-MM"; pad the month to two digits defensively.
        $"{year}-{month.PadLeft(2, '0')}";

    private static (string? Month, string? Year) SplitExpiry(string? expiry)
    {
        if (string.IsNullOrWhiteSpace(expiry))
        {
            return (null, null);
        }

        var parts = expiry.Split('-');
        return parts.Length == 2 ? (parts[1], parts[0]) : (null, null);
    }

    private static string ToIso8601(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static decimal ParseDecimal(string? value) => ParseDecimalOrNull(value) ?? 0m;

    private static decimal? ParseDecimalOrNull(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : (value!.Length <= 500 ? value : value.Substring(0, 500));
}
