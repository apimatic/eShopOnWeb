using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdk.PayPalServerSdkClient _client;
    private readonly ILogger<PayPalGateway> _logger;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    public PayPalGateway(PayPalServerSdk.PayPalServerSdkClient client, ILogger<PayPalGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<string> CreateOrderAsync(int orderId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WriteAsync(ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new[]
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = Amount(amount, currency),
                            InvoiceId = requestId,
                            CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                        }
                    }
                },
                prefer: "return=representation",
                ct: ct), cancellationToken);
            return response.Id ?? throw InvalidResponse("create order");
        }
        catch (SdkException<CreateOrderError> ex) { throw Rejected("create the PayPal order", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("create the PayPal order", ex); }
    }

    public async Task<ProviderAuthorization> AuthorizeOrderAsync(string payPalOrderId, decimal amount,
        CardRequest? card, string? vaultId, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            var cardSource = card is not null ? DirectCard(card) : new PayPalServerSdk.Models.CardRequest
            {
                VaultId = vaultId
            };
            var response = await WriteAsync(ct => _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardSource }
                },
                prefer: "return=representation",
                ct: ct), cancellationToken);
            if (response.Status == OrderStatus.PayerActionRequired)
                throw new PaymentApiException(409,
                    "PayPal requires browser approval for this card; the headless payment flow has stopped.");

            var authorization = response.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault() ?? throw InvalidResponse("authorize order");
            return new ProviderAuthorization(
                response.Id ?? payPalOrderId,
                authorization.Id ?? throw InvalidResponse("authorize order"),
                authorization.Status?.Value ?? "UNKNOWN",
                Decimal(authorization.Amount?.Value, "authorized amount"),
                Date(authorization.ExpirationTime));
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            LogAuthorizeRejection(ex.Error);
            throw Rejected("authorize the PayPal order", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("authorize the PayPal order", ex); }
    }

    public async Task<ProviderAuthorization?> GetOrderAuthorizationAsync(string payPalOrderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(ct => _client.Orders.GetOrder(payPalOrderId, null, null, null, ct: ct),
                cancellationToken);
            var authorization = response.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault();
            return authorization is null ? null : new ProviderAuthorization(
                response.Id ?? payPalOrderId,
                authorization.Id ?? throw InvalidResponse("read order authorization"),
                authorization.Status?.Value ?? "UNKNOWN",
                Decimal(authorization.Amount?.Value, "authorized amount"),
                Date(authorization.ExpirationTime));
        }
        catch (SdkException<GetOrderError> ex) { throw Rejected("read the PayPal order", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("read the PayPal order", ex); }
    }

    public async Task<ProviderAuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(ct => _client.Payments.GetAuthorizedPayment(
                authorizationId, null, null, ct: ct), cancellationToken);
            return Authorization(response);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Rejected("read the PayPal authorization", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("read the PayPal authorization", ex); }
    }

    public async Task<ProviderAuthorizationState> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WriteAsync(ct => _client.Payments.ReauthorizePayment(
                authorizationId, requestId, null,
                new ReauthorizeRequest { Amount = Money(amount, currency) },
                prefer: "return=representation", ct: ct), cancellationToken);
            return Authorization(response);
        }
        catch (SdkException<ReauthorizePaymentError> ex) { throw Rejected("renew the PayPal authorization", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("renew the PayPal authorization", ex); }
    }

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await WriteAsync(ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId, null, requestId, null,
                new CaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
                prefer: "return=representation", ct: ct), cancellationToken);
            return Capture(response);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Rejected("capture the PayPal authorization", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("capture the PayPal authorization", ex); }
    }

    public async Task<ProviderCapture> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(ct => _client.Payments.GetCapturedPayment(captureId, null, ct: ct),
                cancellationToken);
            return Capture(response);
        }
        catch (SdkException<GetCapturedPaymentError> ex) { throw Rejected("read the PayPal capture", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("read the PayPal capture", ex); }
    }

    public async Task<ProviderAuthorizationState> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await WriteAsync(ct => _client.Payments.VoidPayment(
                authorizationId, null, null, requestId,
                prefer: "return=representation", ct: ct), cancellationToken);
            return Authorization(response);
        }
        catch (SdkException<VoidPaymentError> ex) { throw Rejected("void the PayPal authorization", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("void the PayPal authorization", ex); }
    }

    public async Task<ProviderRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var body = new PayPalServerSdk.Models.RefundRequest
            {
                Amount = amount is null ? null : Money(amount.Value, currency)
            };
            var response = await WriteAsync(ct => _client.Payments.RefundCapturedPayment(
                captureId, null, idempotencyKey, null, body,
                prefer: "return=representation", ct: ct), cancellationToken);
            return Refund(response);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Rejected("refund the PayPal capture", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("refund the PayPal capture", ex); }
    }

    public async Task<ProviderRefund> GetRefundAsync(string refundId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await ReadAsync(ct => _client.Payments.GetRefund(refundId, null, null, ct: ct),
                cancellationToken);
            return Refund(response);
        }
        catch (SdkException<GetRefundError> ex) { throw Rejected("read the PayPal refund", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("read the PayPal refund", ex); }
    }

    public async Task<ProviderVaultedCard> SaveCardAsync(CardRequest card, string setupRequestId,
        string tokenRequestId, CancellationToken cancellationToken)
    {
        try
        {
            var setup = await WriteAsync(ct => _client.Vault.CreateSetupToken(
                setupRequestId,
                new SetupTokenRequest
                {
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Name = card.Name,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = Address(card.BillingAddress)
                        }
                    }
                }, ct: ct), cancellationToken);
            if (setup.Status == PaymentTokenStatus.PayerActionRequired)
                throw new PaymentApiException(409,
                    "PayPal requires browser approval to save this card; the headless flow has stopped.");
            var setupId = setup.Id ?? throw InvalidResponse("create card setup token");

            var token = await WriteAsync(ct => _client.Vault.CreatePaymentToken(
                tokenRequestId,
                new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setupId, Type = VaultTokenRequestType.SetupToken }
                    }
                }, ct: ct), cancellationToken);
            var safeCard = token.PaymentSource?.Card ?? throw InvalidResponse("create payment token");
            return new ProviderVaultedCard(
                setupId,
                token.Id ?? throw InvalidResponse("create payment token"),
                token.Customer?.Id,
                "ACTIVE",
                safeCard.Brand?.Value,
                safeCard.LastDigits,
                safeCard.Expiry,
                safeCard.Name);
        }
        catch (SdkException<CreateSetupTokenError> ex) { throw Rejected("create the PayPal setup token", ex); }
        catch (SdkException<CreatePaymentTokenError> ex) { throw Rejected("create the PayPal payment token", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("save the card with PayPal", ex); }
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            await WriteAsync(async ct =>
            {
                await _client.Vault.DeletePaymentToken(tokenId, ct: ct);
                return true;
            }, cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex) { throw Rejected("delete the PayPal payment token", ex); }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("delete the PayPal payment token", ex); }
    }

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, ProviderTransaction>(StringComparer.Ordinal);
        var cursor = new DateTimeOffset(from.UtcDateTime.AddTicks(-(from.UtcDateTime.Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);
        var end = new DateTimeOffset(to.UtcDateTime.AddTicks(TimeSpan.TicksPerSecond - 1 - (to.UtcDateTime.Ticks % TimeSpan.TicksPerSecond)), TimeSpan.Zero);
        while (cursor <= end)
        {
            var windowEnd = cursor.AddDays(31).AddSeconds(-1);
            if (windowEnd > end) windowEnd = end;
            var page = 1;
            while (true)
            {
                try
                {
                    var response = await ReadAsync(ct => _client.TransactionSearch.SearchTransactions(
                        startDate: cursor.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                        endDate: windowEnd.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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
                        ct: ct), cancellationToken);
                    foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                    {
                        var info = detail.TransactionInfo;
                        if (string.IsNullOrWhiteSpace(info?.TransactionId)) continue;
                        result[info.TransactionId] = new ProviderTransaction(
                            info.TransactionId,
                            info.PaypalReferenceId,
                            info.PaypalReferenceIdType?.Value,
                            info.InvoiceId,
                            info.CustomField,
                            info.TransactionStatus,
                            info.TransactionEventCode,
                            Date(info.TransactionInitiationDate),
                            DecimalOrNull(info.TransactionAmount?.Value),
                            DecimalOrNull(info.FeeAmount?.Value),
                            info.TransactionAmount?.CurrencyCode);
                    }
                    var totalPages = response.TotalPages ?? page;
                    if (page >= totalPages || (response.TransactionDetails?.Count ?? 0) == 0) break;
                    page++;
                }
                catch (SdkException<RawError> ex)
                {
                    if (IsReportingDataUnavailable(ex.Error))
                        break;

                    _logger.LogWarning(
                        "PayPal transaction search failed. HTTP status: {PayPalStatusCode}; response body: {PayPalResponseBody}",
                        (int)ex.Error.StatusCode,
                        ex.Error.ReadAsString());
                    throw Rejected("search PayPal transactions", ex);
                }
                catch (Exception ex) when (IsBoundaryFailure(ex)) { throw Unavailable("search PayPal transactions", ex); }
            }
            cursor = windowEnd.AddSeconds(1);
        }
        return result.Values.OrderBy(x => x.InitiatedAt).ToList();
    }

    private async Task<T> ReadAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task<T> WriteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var scope = PayPalWriteGuardHandler.BeginScope();
        return await ReadAsync(call, cancellationToken);
    }

    private static AmountWithBreakdown Amount(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static Money Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static PayPalServerSdk.Models.CardRequest DirectCard(CardRequest card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = Address(card.BillingAddress)
    };

    private static Address Address(BillingAddressRequest address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode.ToUpperInvariant()
    };

    private static ProviderAuthorizationState Authorization(PaymentAuthorization response) => new(
        response.Id ?? throw InvalidResponse("read authorization"),
        response.Status?.Value ?? "UNKNOWN",
        Decimal(response.Amount?.Value, "authorized amount"),
        Date(response.ExpirationTime));

    private static ProviderCapture Capture(CapturedPayment response) => new(
        response.Id ?? throw InvalidResponse("read capture"),
        response.Status?.Value ?? "UNKNOWN",
        Decimal(response.Amount?.Value, "captured amount"),
        DecimalOrNull(response.SellerReceivableBreakdown?.PaypalFee?.Value),
        DecimalOrNull(response.SellerReceivableBreakdown?.NetAmount?.Value));

    private static ProviderRefund Refund(PayPalServerSdk.Models.Refund response) => new(
        response.Id ?? throw InvalidResponse("read refund"),
        response.Status?.Value ?? "UNKNOWN",
        DecimalOrNull(response.Amount?.Value));

    private static decimal Decimal(string? value, string field) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw InvalidResponse(field);

    private static decimal? DecimalOrNull(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed : null;

    private static PaymentApiException InvalidResponse(string operation) =>
        new(502, $"PayPal returned an incomplete response while attempting to {operation}.");

    private static bool IsBoundaryFailure(Exception ex) => ex is JsonException or HttpRequestException or
        TaskCanceledException or DuplicateProviderSendBlockedException;

    private static bool IsReportingDataUnavailable(RawError error)
    {
        if ((int)error.StatusCode != 404)
            return false;

        try
        {
            using var body = JsonDocument.Parse(error.ReadAsString());
            var root = body.RootElement;
            return root.TryGetProperty("name", out var name) &&
                   string.Equals(name.GetString(), "INVALID_REQUEST", StringComparison.Ordinal) &&
                   root.TryGetProperty("message", out var message) &&
                   string.Equals(message.GetString(), "Data for the given start date is not available.",
                       StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void LogAuthorizeRejection(AuthorizeOrderError error)
    {
        if (error.TryGetError(out var providerError))
        {
            _logger.LogWarning(
                "PayPal rejected order authorization. Error name: {PayPalErrorName}; message: {PayPalErrorMessage}; debug ID: {PayPalDebugId}",
                providerError.Name,
                providerError.Message,
                providerError.DebugId);

            foreach (var detail in providerError.Details ?? Array.Empty<ErrorDetails>())
            {
                _logger.LogWarning(
                    "PayPal order authorization error detail. Issue: {PayPalErrorIssue}; field: {PayPalErrorField}; description: {PayPalErrorDescription}; debug ID: {PayPalDebugId}",
                    detail.Issue,
                    detail.Field,
                    detail.Description,
                    providerError.DebugId);
            }
            return;
        }

        _logger.LogWarning("PayPal rejected order authorization with an unrecognized error response shape.");
    }

    private static PayPalProviderException Rejected(string operation, Exception inner) =>
        new($"PayPal rejected the request to {operation}.", inner);

    private static PayPalProviderException Unavailable(string operation, Exception inner) =>
        new($"PayPal could not complete the request to {operation}; its outcome may need reconciliation.", inner);
}
