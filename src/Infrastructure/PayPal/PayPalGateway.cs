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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using Address = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal implementation of the payment gateway, over the PayPalServerSdk client.
/// Every write passes a caller idempotency key as PayPal-Request-Id; every call is bounded by
/// a total budget; every failure is translated to <see cref="PaymentGatewayException"/> with a
/// caller-safe message (typed SDK errors carry no HTTP status, so the status is approximated
/// from PayPal's error name).
/// </summary>
public class PayPalGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    // PayPal enforces invoice-id uniqueness per merchant account; the in-memory store reuses
    // order ids across runs, so invoice ids carry a per-process suffix to stay unique.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly PayPalServerSdkClient _client;
    private readonly IAppLogger<PayPalGateway> _logger;

    public PayPalGateway(PayPalServerSdkClient client, IAppLogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizePaymentAsync(AuthorizationRequest request, CancellationToken ct = default)
    {
        var invoiceId = $"order-{request.LocalOrderId}-{RunId}";
        PayPalServerSdk.Models.Order order;
        try
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
                            CurrencyCode = request.Currency,
                            Value = FormatAmount(request.Amount)
                        },
                        ReferenceId = request.LocalOrderId.ToString(CultureInfo.InvariantCulture),
                        CustomId = request.LocalOrderId.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = invoiceId,
                        Description = $"eShopOnWeb order #{request.LocalOrderId}"
                    }
                }
            };

            order = await Call(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey + "-order",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: c), ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            _logger.LogWarning("CreateOrder failed for local order {LocalOrderId} (invoice {InvoiceId}).",
                request.LocalOrderId, invoiceId);
            throw Map(ex.Error);
        }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        try
        {
            if (order.Id is null)
            {
                throw UnprocessableResponse(null);
            }

            var authorizeRequest = new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource
                {
                    Card = BuildCardRequest(request)
                }
            };

            var authorization = await Call(c => _client.Orders.AuthorizeOrder(
                id: order.Id,
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeRequest,
                prefer: "return=representation",
                ct: c), ct);

            if (authorization.Status == OrderStatus.PayerActionRequired)
            {
                // A browser approval challenge is outside this integration's headless model.
                _logger.LogWarning("PayPal order {PayPalOrderId} requires payer action (3DS/SCA challenge).", order.Id);
                throw new PaymentGatewayException(
                    "PayPal requires the shopper to approve this payment in a browser before it can be authorized. " +
                    "This card cannot be used for a direct payment.",
                    422, "PAYER_ACTION_REQUIRED");
            }

            var auth = authorization.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (auth?.Id is null)
            {
                throw UnprocessableResponse(null);
            }
            if (auth.Status != AuthorizationStatus.Created)
            {
                throw new PaymentGatewayException(
                    $"PayPal did not authorize the payment (status {auth.Status?.Value ?? "unknown"}).",
                    422, auth.Status?.Value);
            }

            return new AuthorizationResult(order.Id, auth.Id, auth.Status!.Value, ParseTime(auth.ExpirationTime));
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            _logger.LogWarning("AuthorizeOrder failed for PayPal order {PayPalOrderId} (invoice {InvoiceId}).",
                order.Id, invoiceId);
            throw Map(ex.Error);
        }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, string? invoiceId, CancellationToken ct = default)
    {
        try
        {
            var capture = await Call(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
                    InvoiceId = invoiceId,
                    FinalCapture = true
                },
                prefer: "return=representation",
                ct: c), ct);

            if (capture.Id is null)
            {
                throw UnprocessableResponse(null);
            }

            var breakdown = capture.SellerReceivableBreakdown;
            return new CaptureResult(
                capture.Id,
                capture.Status?.Value ?? "UNKNOWN",
                ParseMoney(breakdown?.GrossAmount),
                ParseMoney(breakdown?.PaypalFee),
                ParseMoney(breakdown?.NetAmount),
                breakdown?.GrossAmount?.CurrencyCode ?? currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<AuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var auth = await Call(c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: c), ct);

            return new AuthorizationInfo(
                auth.Id ?? authorizationId,
                auth.Status?.Value ?? "UNKNOWN",
                ParseTime(auth.ExpirationTime));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<AuthorizationInfo> ReauthorizePaymentAsync(string authorizationId, decimal amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var renewed = await Call(c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
                },
                prefer: "return=representation",
                ct: c), ct);

            if (renewed.Id is null)
            {
                throw UnprocessableResponse(null);
            }

            // PayPal may return a new authorization id; the caller persists and uses whatever came back.
            return new AuthorizationInfo(renewed.Id, renewed.Status?.Value ?? "UNKNOWN", ParseTime(renewed.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<string> VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var voided = await Call(c => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: c), ct);

            return voided.Status?.Value ?? "VOIDED";
        }
        catch (SdkException<VoidPaymentError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<RefundResult> RefundCaptureAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            // A full refund is an empty payload; a partial refund sets only the amount.
            var body = amount.HasValue
                ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount.Value) } }
                : new RefundRequest();

            var refund = await Call(c => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: c), ct);

            if (refund.Id is null)
            {
                throw UnprocessableResponse(null);
            }

            var status = refund.Status?.Value ?? "UNKNOWN";
            if (status is not ("COMPLETED" or "PENDING"))
            {
                throw new PaymentGatewayException(
                    $"PayPal did not complete the refund (status {status}).", 422, status);
            }

            return new RefundResult(
                refund.Id,
                status,
                ParseMoney(refund.Amount) ?? amount ?? 0m,
                refund.Amount?.CurrencyCode ?? currency,
                ParseMoney(refund.SellerPayableBreakdown?.TotalRefundedAmount));
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<VaultedCardResult> VaultCardAsync(CardDetails card, string? payPalCustomerId,
        string merchantCustomerId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var response = await Call(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
                {
                    Customer = new Customer
                    {
                        Id = payPalCustomerId,
                        MerchantCustomerId = merchantCustomerId
                    },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name,
                            BillingAddress = ToSdkAddress(card.Address)
                        }
                    }
                },
                ct: c), ct);

            if (response.Id is null || response.Customer?.Id is null)
            {
                throw UnprocessableResponse(null);
            }

            var cardEntity = response.PaymentSource?.Card;
            if (cardEntity?.VerificationStatus == CardVerificationStatus.Failed)
            {
                throw new PaymentGatewayException("PayPal could not verify the card.", 422, "CARD_VERIFICATION_FAILED");
            }

            return new VaultedCardResult(
                response.Id,
                response.Customer.Id,
                cardEntity?.Brand?.Value,
                cardEntity?.LastDigits,
                cardEntity?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await Call(c => _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: c), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Map(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();
        try
        {
            // PayPal caps a search window at 31 days; chunk longer ranges.
            var windowStart = from;
            while (windowStart < to)
            {
                var windowEnd = windowStart.AddDays(31) < to ? windowStart.AddDays(31) : to;

                var page = 1;
                var totalPages = 1;
                while (page <= totalPages)
                {
                    var response = await Call(c => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatInstant(windowStart),
                        endDate: FormatInstant(windowEnd),
                        transactionId: null,
                        transactionType: null,
                        transactionStatus: null,
                        transactionAmount: null,
                        transactionCurrency: null,
                        paymentInstrumentType: null,
                        storeId: null,
                        terminalId: null,
                        fields: "transaction_info",
                        balanceAffectingRecordsOnly: "N",
                        pageSize: 100,
                        page: page,
                        ct: c), ct);

                    totalPages = response.TotalPages ?? page;
                    if (response.TransactionDetails is not null)
                    {
                        results.AddRange(response.TransactionDetails.Select(MapTransaction));
                    }
                    page++;
                }

                windowStart = windowEnd;
            }
        }
        catch (SdkException<RawError> ex) { throw MapRaw(ex.Error); }
        catch (JsonException ex) { throw UnprocessableResponse(ex); }
        catch (TaskCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw Unreachable(ex); }

        return results
            .GroupBy(t => t.TransactionId)
            .Select(g => g.First())
            .ToList();
    }

    private static CardRequest BuildCardRequest(AuthorizationRequest request)
    {
        if (request.VaultedCardTokenId is not null)
        {
            return new CardRequest
            {
                VaultId = request.VaultedCardTokenId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            };
        }

        var card = request.Card!;
        return new CardRequest
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = ToSdkAddress(card.Address)
        };
    }

    private static Address? ToSdkAddress(BillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static GatewayTransaction MapTransaction(TransactionDetails details)
    {
        var info = details.TransactionInfo;
        return new GatewayTransaction(
            info?.TransactionId,
            info?.PaypalReferenceId,
            info?.PaypalReferenceIdType?.Value,
            info?.InvoiceId,
            info?.CustomField,
            ParseMoney(info?.TransactionAmount),
            info?.TransactionAmount?.CurrencyCode,
            ParseMoney(info?.FeeAmount),
            info?.TransactionStatus,
            info?.TransactionEventCode,
            ParseTime(info?.TransactionInitiationDate));
    }

    private static async Task<T> Call<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static async Task Call(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(Money? money)
    {
        return decimal.TryParse(money?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var time)
            ? time
            : null;
    }

    private static string FormatInstant(DateTimeOffset instant)
    {
        // Transaction Search requires RFC 3339 with seconds.
        return instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    // ---- Error mapping (per-operation accessor ladders; TryGetRawError always last) ----

    private PaymentGatewayException Map(CreateOrderError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetNoContent(out var noContent)) return FromRawError(noContent);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(GetAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetNoContent(out var noContent)) return FromRawError(noContent);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(VoidPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetNoContent(out var noContent)) return FromRawError(noContent);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var e)) return FromError(e);
        if (error.TryGetNoContent(out var noContent)) return FromRawError(noContent);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(CreatePaymentTokenError error)
    {
        if (error.TryGetError1(out var e)) return FromError(e);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException Map(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var e)) return FromError(e);
        if (error.TryGetRawError(out var raw)) return FromRawError(raw);
        return UnknownProviderError();
    }

    private PaymentGatewayException MapRaw(RawError raw) => FromRawError(raw);

    private PaymentGatewayException FromError(Error error)
    {
        var issues = error.Details?
            .Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}")
            .ToList() ?? new List<string>();
        _logger.LogWarning("PayPal error {ErrorName} (debug {DebugId}): {Message}", error.Name, error.DebugId, error.Message);
        return new PaymentGatewayException(
            $"PayPal rejected the request ({error.Name}: {error.Message}).",
            StatusFromName(error.Name), error.Name, issues);
    }

    private PaymentGatewayException FromError(Error1 error)
    {
        var issues = error.Details?
            .Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}")
            .ToList() ?? new List<string>();
        _logger.LogWarning("PayPal error {ErrorName} (debug {DebugId}): {Message}", error.Name, error.DebugId, error.Message);
        return new PaymentGatewayException(
            $"PayPal rejected the request ({error.Name}: {error.Message}).",
            StatusFromName(error.Name), error.Name, issues);
    }

    private PaymentGatewayException FromRawError(RawError raw)
    {
        var status = (int)raw.StatusCode;
        string? name = null;
        string? message = null;
        List<string>? issues = null;
        try
        {
            var parsed = raw.ReadAsJson<DefaultError>();
            if (parsed is not null)
            {
                name = parsed.Name;
                message = parsed.Message;
                issues = parsed.Details?
                    .Select(d => d.Description is null ? d.Issue : $"{d.Issue}: {d.Description}")
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Body is not the modeled error shape; the generic message below is used instead.
        }

        _logger.LogWarning("PayPal raw error HTTP {Status}: {Body}", status, raw.ReadAsString());
        var safeMessage = message is not null
            ? $"PayPal rejected the request ({name}: {message})."
            : $"PayPal returned HTTP {status}.";
        return new PaymentGatewayException(safeMessage, status, name, issues);
    }

    private static int StatusFromName(string? name)
    {
        // Typed SDK errors discard the HTTP status; approximate it from PayPal's error name.
        return name switch
        {
            "INTERNAL_SERVER_ERROR" or "SERVICE_UNAVAILABLE" => 502,
            "RESOURCE_NOT_FOUND" => 404,
            "NOT_AUTHORIZED" or "UNAUTHORIZED_ACCESS" => 403,
            _ => 422
        };
    }

    private static PaymentGatewayException UnknownProviderError()
    {
        return new PaymentGatewayException("PayPal returned an error that could not be read.", 502);
    }

    private static PaymentGatewayException UnprocessableResponse(Exception? inner)
    {
        return new PaymentGatewayException(
            "The payment provider returned a response that could not be processed.", 502, null, null, inner);
    }

    private static PaymentGatewayException Unreachable(Exception inner)
    {
        return new PaymentGatewayException(
            "The payment provider could not be reached or did not respond in time.", 502, null, null, inner);
    }
}
