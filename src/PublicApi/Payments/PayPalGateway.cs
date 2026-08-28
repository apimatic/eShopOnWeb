using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan TransactionReportingLag = TimeSpan.FromHours(3);
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client) => _client = client;

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        string createRequestId, string authorizeRequestId, CardInput? card, string? vaultId,
        string? existingPayPalOrderId, CancellationToken ct)
    {
        try
        {
            var orderIdAtPayPal = existingPayPalOrderId;
            if (string.IsNullOrWhiteSpace(orderIdAtPayPal))
            {
                var created = await Bounded(token => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: createRequestId,
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
                                Amount = Money(amount, currency),
                                InvoiceId = Invoice(createRequestId),
                                CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                            }
                        }
                    },
                    prefer: "return=representation", ct: token), ct);
                orderIdAtPayPal = Required(created.Id, "PayPal did not return an order identifier.");
            }

            var paymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = card is not null ? DirectCard(card) : new CardRequest { VaultId = Required(vaultId, "A payment method is required.") }
            };
            var authorized = await Bounded(token => _client.Orders.AuthorizeOrder(
                id: orderIdAtPayPal,
                payPalMockResponse: null,
                payPalRequestId: authorizeRequestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest { PaymentSource = paymentSource },
                prefer: "return=representation", ct: token), ct);

            if (authorized.Status == OrderStatus.PayerActionRequired)
                throw new PayPalChallengeRequiredException();

            var auth = authorized.PurchaseUnits?
                .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
                .FirstOrDefault() ?? throw Malformed("PayPal authorized the order without returning an authorization.");
            var parsedAmount = ParseMoney(auth.Amount, "authorization");
            return new PayPalAuthorizationResult(
                Required(authorized.Id, "PayPal did not return the authorized order identifier."),
                authorized.Status?.Value,
                Required(auth.Id, "PayPal did not return an authorization identifier."),
                auth.Status?.Value,
                parsedAmount.Amount,
                parsedAmount.Currency,
                ParseDate(auth.ExpirationTime),
                ParseDate(auth.CreateTime));
        }
        catch (SdkException<CreateOrderError> ex) { throw Typed(ex.Error, ex); }
        catch (SdkException<AuthorizeOrderError> ex) { throw Typed(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalAuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(token => _client.Payments.GetAuthorizedPayment(
                authorizationId, null, null, ct: token), ct);
            return Authorization(auth);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex) { throw Typed(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalAuthorizationSnapshot> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(token => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = MoneyValue(amount, currency) },
                prefer: "return=representation", ct: token), ct);
            return Authorization(auth);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw new PaymentApiException(HttpStatusCode.Conflict,
                $"The authorization expired and PayPal could not renew it. The shopper must pay again. Provider detail: {SafeDetail(ex.Error)}");
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, int orderId, decimal amount,
        string currency, string requestId, CancellationToken ct)
    {
        try
        {
            var capture = await Bounded(token => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest { Amount = MoneyValue(amount, currency), FinalCapture = true, InvoiceId = Invoice(requestId) },
                prefer: "return=representation", ct: token), ct);
            return Capture(capture);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex) { throw Typed(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken ct)
    {
        try
        {
            var capture = await Bounded(token => _client.Payments.GetCapturedPayment(captureId, null, ct: token), ct);
            return Capture(capture);
        }
        catch (SdkException<GetCapturedPaymentError> ex) { throw Typed(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            var auth = await Bounded(token => _client.Payments.VoidPayment(
                authorizationId, null, null, requestId, prefer: "return=representation", ct: token), ct);
            return auth.Status?.Value;
        }
        catch (SdkException<VoidPaymentError> ex) { throw Typed(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        bool refundRemainingBalance, string idempotencyKey, int orderId, CancellationToken ct)
    {
        try
        {
            var body = new RefundRequest
            {
                Amount = refundRemainingBalance ? null : MoneyValue(amount, currency),
                InvoiceId = RefundInvoice(captureId, idempotencyKey),
                CustomId = orderId.ToString(CultureInfo.InvariantCulture)
            };
            var refund = await Bounded(token => _client.Payments.RefundCapturedPayment(
                captureId, null, idempotencyKey, null, body, prefer: "return=representation", ct: token), ct);
            var money = ParseMoney(refund.Amount, "refund");
            return new PayPalRefundResult(Required(refund.Id, "PayPal did not return a refund identifier."),
                refund.Status?.Value, money.Amount, money.Currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex) { throw Typed(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalSavedCardResult> SaveCardAsync(string ownerCorrelation, string? customerId,
        CardInput card, string setupRequestId, string tokenRequestId, CancellationToken ct)
    {
        try
        {
            var setup = await Bounded(token => _client.Vault.CreateSetupToken(
                payPalRequestId: setupRequestId,
                body: new SetupTokenRequest
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
                    },
                    Customer = new Customer { Id = customerId, MerchantCustomerId = ownerCorrelation }
                }, ct: token), ct);
            if (setup.Status == PaymentTokenStatus.PayerActionRequired)
                throw new PayPalChallengeRequiredException();

            var setupId = Required(setup.Id, "PayPal did not return a setup-token identifier.");
            var tokenResponse = await Bounded(token => _client.Vault.CreatePaymentToken(
                payPalRequestId: tokenRequestId,
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest { Id = setupId, Type = VaultTokenRequestType.SetupToken }
                    },
                    Customer = new Customer { Id = customerId ?? setup.Customer?.Id, MerchantCustomerId = ownerCorrelation }
                }, ct: token), ct);
            var safeCard = tokenResponse.PaymentSource?.Card;
            return new PayPalSavedCardResult(
                Required(tokenResponse.Id, "PayPal did not return a reusable payment-token identifier."),
                Required(tokenResponse.Customer?.Id ?? customerId ?? setup.Customer?.Id,
                    "PayPal did not return a customer identifier."),
                safeCard?.Brand?.Value,
                safeCard?.LastDigits,
                safeCard?.Expiry,
                safeCard?.Name);
        }
        catch (SdkException<CreateSetupTokenError> ex) { throw TypedVault(ex.Error, ex); }
        catch (SdkException<CreatePaymentTokenError> ex) { throw TypedVault(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<IReadOnlySet<string>> ListPaymentTokenIdsAsync(string customerId, CancellationToken ct)
    {
        try
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var page = 1;
            var totalPages = 1;
            do
            {
                var response = await Bounded(token => _client.Vault.ListCustomerPaymentTokens(
                    customerId, pageSize: 100, page: page, totalRequired: true, ct: token), ct);
                foreach (var token in response.PaymentTokens ?? Array.Empty<PaymentTokenResponse>())
                    if (!string.IsNullOrWhiteSpace(token.Id)) result.Add(token.Id);
                totalPages = Math.Max(1, response.TotalPages ?? 1);
                page++;
            } while (page <= totalPages);
            return result;
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex) { throw TypedVault(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task DeletePaymentTokenAsync(string tokenId, CancellationToken ct)
    {
        try { await Bounded(token => _client.Vault.DeletePaymentToken(tokenId, ct: token), ct); }
        catch (SdkException<DeletePaymentTokenError> ex) { throw TypedVault(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    public async Task<PayPalTransactionReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct)
    {
        var availableThrough = DateTimeOffset.UtcNow.Subtract(TransactionReportingLag);
        var effectiveTo = to < availableThrough ? to : availableThrough;
        if (effectiveTo <= from)
            return new PayPalTransactionReport(Array.Empty<PayPalTransactionRecord>(), null, 0);

        try
        {
            var records = new List<PayPalTransactionRecord>();
            DateTimeOffset? refreshedAt = null;
            var page = 1;
            var totalPages = 1;
            do
            {
                var response = await Bounded(token => _client.TransactionSearch.SearchTransactions(
                    startDate: ReportingDate(from),
                    endDate: ReportingDate(effectiveTo),
                    transactionId: null, transactionType: null, transactionStatus: null,
                    transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                    storeId: null, terminalId: null, fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page, ct: token), ct);
                foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    records.Add(new PayPalTransactionRecord(
                        info.TransactionId, info.PaypalReferenceId, info.PaypalReferenceIdType?.Value,
                        info.TransactionEventCode, ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate), ParseNullableMoney(info.TransactionAmount).Amount,
                        ParseNullableMoney(info.TransactionAmount).Currency, ParseNullableMoney(info.FeeAmount).Amount,
                        info.TransactionStatus, info.InvoiceId, info.CustomField));
                }
                refreshedAt = ParseDate(response.LastRefreshedDatetime) ?? refreshedAt;
                totalPages = Math.Max(1, response.TotalPages ?? 1);
                page++;
            } while (page <= totalPages);
            return new PayPalTransactionReport(records, refreshedAt, page - 1);
        }
        catch (SdkException<RawError> ex) { throw TransactionSearchError(ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary(ex); }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static AmountWithBreakdown Money(decimal amount, string currency) =>
        new() { CurrencyCode = currency, Value = Format(amount) };
    private static Money MoneyValue(decimal amount, string currency) =>
        new() { CurrencyCode = currency, Value = Format(amount) };
    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string ReportingDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    private static string Invoice(string operationCorrelation) => $"ESHOP-{operationCorrelation}";

    private static string RefundInvoice(string captureId, string idempotencyKey)
    {
        var correlationHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{captureId}\n{idempotencyKey}"))).ToLowerInvariant();
        return $"ESHOP-REFUND-{correlationHash}";
    }

    private static CardRequest DirectCard(CardInput card) => new()
    {
        Name = card.Name, Number = card.Number, Expiry = card.Expiry,
        SecurityCode = card.SecurityCode, BillingAddress = Address(card.BillingAddress)
    };

    private static Address Address(CardBillingAddress address) => new()
    {
        AddressLine1 = address.AddressLine1, AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City, AdminArea1 = address.State, PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static PayPalAuthorizationSnapshot Authorization(PaymentAuthorization auth)
    {
        var money = ParseMoney(auth.Amount, "authorization");
        return new PayPalAuthorizationSnapshot(Required(auth.Id, "PayPal returned an authorization without an identifier."),
            auth.Status?.Value, money.Amount, money.Currency, ParseDate(auth.ExpirationTime), ParseDate(auth.CreateTime));
    }

    private static PayPalCaptureResult Capture(CapturedPayment capture)
    {
        var amount = ParseMoney(capture.Amount, "capture");
        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult(Required(capture.Id, "PayPal returned a capture without an identifier."),
            capture.Status?.Value, amount.Amount, amount.Currency,
            ParseNullableMoney(breakdown?.GrossAmount).Amount,
            ParseNullableMoney(breakdown?.PaypalFee).Amount,
            ParseNullableMoney(breakdown?.NetAmount).Amount);
    }

    private static (decimal Amount, string Currency) ParseMoney(Money? money, string label)
    {
        if (money is null || string.IsNullOrWhiteSpace(money.CurrencyCode) ||
            !decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw Malformed($"PayPal returned an invalid {label} amount.");
        return (value, money.CurrencyCode);
    }

    private static (decimal? Amount, string? Currency) ParseNullableMoney(Money? money)
    {
        if (money is null || !decimal.TryParse(money.Value, NumberStyles.Number,
                CultureInfo.InvariantCulture, out var value)) return (null, money?.CurrencyCode);
        return (value, money.CurrencyCode);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date : null;

    private static string Required(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw Malformed(message) : value;
    private static PayPalProviderException Malformed(string message) => new(message, new JsonException(message));
    private static bool IsBoundaryException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException;
    private static PayPalProviderException Boundary(Exception ex) =>
        new(ex is TaskCanceledException ? "PayPal did not respond before the payment deadline. The outcome may require reconciliation."
            : "PayPal returned a response that could not be processed. The outcome may require reconciliation.", ex);

    private static PayPalProviderException Typed(CreateOrderError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException Typed(AuthorizeOrderError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException Typed(GetAuthorizedPaymentError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetNoContent(out var noContent)) return Raw(noContent, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException Typed(CaptureAuthorizedPaymentError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetNoContent(out var noContent)) return Raw(noContent, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException Typed(GetCapturedPaymentError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetNoContent(out var noContent)) return Raw(noContent, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException Typed(VoidPaymentError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetNoContent(out var noContent)) return Raw(noContent, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException Typed(RefundCapturedPaymentError error, Exception ex)
    {
        if (error.TryGetError(out var typed)) return TypedError(typed, ex);
        if (error.TryGetNoContent(out var noContent)) return Raw(noContent, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownPaymentError(ex);
    }

    private static PayPalProviderException TypedVault(CreateSetupTokenError error, Exception ex)
    {
        if (error.TryGetError1(out var typed)) return TypedVaultError(typed, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownVaultError(ex);
    }

    private static PayPalProviderException TypedVault(CreatePaymentTokenError error, Exception ex)
    {
        if (error.TryGetError1(out var typed)) return TypedVaultError(typed, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownVaultError(ex);
    }

    private static PayPalProviderException TypedVault(ListCustomerPaymentTokensError error, Exception ex)
    {
        if (error.TryGetError1(out var typed)) return TypedVaultError(typed, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownVaultError(ex);
    }

    private static PayPalProviderException TypedVault(DeletePaymentTokenError error, Exception ex)
    {
        if (error.TryGetError1(out var typed)) return TypedVaultError(typed, ex);
        if (error.TryGetRawError(out var raw)) return Raw(raw, ex);
        return UnknownVaultError(ex);
    }

    private static PayPalProviderException TypedError(PayPalServerSdk.Models.Error error, Exception ex) =>
        new(SafeDetail(error), ex, debugId: error.DebugId);
    private static PayPalProviderException TypedVaultError(Error1 error, Exception ex) =>
        new(SafeDetail(error), ex, debugId: error.DebugId);
    private static PayPalProviderException UnknownPaymentError(Exception ex) =>
        new("PayPal rejected the payment request.", ex);
    private static PayPalProviderException UnknownVaultError(Exception ex) =>
        new("PayPal rejected the saved-card request.", ex);

    private static PayPalProviderException Raw(RawError raw, Exception ex) =>
        new("PayPal rejected the request.", ex, raw.StatusCode);

    private static PayPalProviderException TransactionSearchError(RawError raw, Exception ex)
    {
        try
        {
            var error = raw.ReadAsJson<DefaultError>();
            if (error is not null)
            {
                var details = new[] { $"name={error.Name}" }.Concat(error.Details?.Select(detail =>
                    SafeTransactionSearchDetail(detail.Issue, detail.Field, detail.Location, detail.Description))
                    ?? Array.Empty<string>());
                var message = SafeDetail(error.Message, "PayPal rejected the transaction report request.", details);
                return new PayPalProviderException(message, ex, raw.StatusCode, error.DebugId);
            }
        }
        catch (JsonException)
        {
            // Raw transaction-search errors are not guaranteed to match DefaultError.
        }

        return new PayPalProviderException("PayPal rejected the transaction report request.", ex, raw.StatusCode);
    }
    private static string SafeDetail(PayPalServerSdk.Models.Error error) =>
        SafeDetail(
            error.Message,
            "PayPal rejected the payment request.",
            error.Details?.Select(detail => SafeProviderDetail(detail.Issue, detail.Field, detail.Description)));

    private static string SafeDetail(Error1 error) =>
        SafeDetail(
            error.Message,
            "PayPal rejected the saved-card request.",
            error.Details?.Select(detail => SafeProviderDetail(detail.Issue, detail.Field, detail.Description)));

    private static string SafeDetail(string? message, string fallback, IEnumerable<string>? details)
    {
        var safeMessage = string.IsNullOrWhiteSpace(message) ? fallback : message;
        var safeDetails = details?.Where(detail => !string.IsNullOrWhiteSpace(detail)).Take(10).ToArray();
        return safeDetails is { Length: > 0 }
            ? $"{safeMessage} Details: {string.Join("; ", safeDetails)}"
            : safeMessage;
    }

    private static string SafeProviderDetail(string issue, string? field, string? description)
    {
        var parts = new List<string> { $"issue={issue}" };
        if (!string.IsNullOrWhiteSpace(field)) parts.Add($"field={field}");
        if (!string.IsNullOrWhiteSpace(description)) parts.Add($"description={description}");
        return string.Join(", ", parts);
    }

    private static string SafeTransactionSearchDetail(string issue, string? field, string? location,
        string? description)
    {
        var parts = new List<string> { $"issue={issue}" };
        if (!string.IsNullOrWhiteSpace(field)) parts.Add($"field={field}");
        if (!string.IsNullOrWhiteSpace(location)) parts.Add($"location={location}");
        if (!string.IsNullOrWhiteSpace(description)) parts.Add($"description={description}");
        return string.Join(", ", parts);
    }
    private static string SafeDetail(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out PayPalServerSdk.Models.Error typed)) return SafeDetail(typed);
        return "the provider rejected the renewal";
    }
}
