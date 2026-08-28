using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public interface IPayPalGateway
{
    Task<AuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        ProviderCard card, string createRequestId, string authorizeRequestId, CancellationToken ct);
    Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct);
    Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct);
    Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken ct);
    Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken ct);
    Task<ProviderRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken ct);
    Task<ProviderSavedMethod> SaveMethodAsync(string ownerId, ProviderCard card, string requestId,
        CancellationToken ct);
    Task<IReadOnlyList<ProviderSavedMethod>> ListMethodsAsync(string customerId, CancellationToken ct);
    Task DeleteMethodAsync(string tokenId, CancellationToken ct);
    Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct);
}

public sealed class PayPalGateway : IPayPalGateway
{
    private readonly PayPalServerSdkClient _client;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(30);

    public PayPalGateway(PayPalServerSdkClient client) => _client = client;

    public async Task<AuthorizationResult> AuthorizeAsync(int orderId, decimal amount, string currency,
        ProviderCard card, string createRequestId, string authorizeRequestId, CancellationToken ct)
    {
        var value = Format(amount);
        var invoiceId = BuildInvoiceId(createRequestId);
        var created = await CallAsync(token => _client.Orders.CreateOrder(
            payPalMockResponse: null,
            payPalRequestId: createRequestId,
            payPalPartnerAttributionId: null,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        ReferenceId = orderId.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = invoiceId,
                        CustomId = $"{orderId.ToString(CultureInfo.InvariantCulture)}:{invoiceId}",
                        Amount = new AmountWithBreakdown { CurrencyCode = currency, Value = value }
                    }
                }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), ct);

        if (string.IsNullOrWhiteSpace(created.Id))
            throw new PaymentProviderException("PayPal did not return an order identifier.");

        var cardRequest = new CardRequest
        {
            Name = card.Name,
            Number = card.VaultId is null ? card.Number : null,
            Expiry = card.VaultId is null ? card.Expiry : null,
            SecurityCode = card.VaultId is null ? card.SecurityCode : null,
            VaultId = card.VaultId,
            BillingAddress = card.VaultId is null ? ToAddress(card.BillingAddress) : null,
            StoredCredential = card.VaultId is null ? null : new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.OneTime,
                Usage = StoredPaymentSourceUsageType.Subsequent
            }
        };

        var authorized = await CallAsync(token => _client.Orders.AuthorizeOrder(
            id: created.Id,
            payPalMockResponse: null,
            payPalRequestId: authorizeRequestId,
            payPalClientMetadataId: null,
            payPalAuthAssertion: null,
            body: new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), ct);

        var challenge = authorized.Status == OrderStatus.PayerActionRequired;
        var providerAuthorization = authorized.PurchaseUnits?
            .SelectMany(x => x.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (!challenge && (providerAuthorization is null || string.IsNullOrWhiteSpace(providerAuthorization.Id)))
            throw new PaymentProviderException("PayPal did not return an authorization.");

        var returnedAmount = Parse(providerAuthorization?.Amount?.Value);
        if (!challenge && (returnedAmount != amount ||
            !string.Equals(providerAuthorization?.Amount?.CurrencyCode, currency, StringComparison.OrdinalIgnoreCase)))
            throw new PaymentProviderException("PayPal returned an authorization amount that does not match the order.");

        return new AuthorizationResult(
            created.Id,
            authorized.Status?.Value,
            challenge,
            providerAuthorization?.Id,
            providerAuthorization?.Status?.Value,
            returnedAmount,
            ParseDate(providerAuthorization?.CreateTime),
            ParseDate(providerAuthorization?.ExpirationTime));
    }

    public async Task<ProviderAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var result = await CallAsync(token => _client.Payments.GetAuthorizedPayment(
            authorizationId, null, null, null, token), ct);
        return Authorization(result);
    }

    public async Task<ProviderAuthorization> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct)
    {
        var result = await CallAsync(token => _client.Payments.ReauthorizePayment(
            authorizationId: authorizationId,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: new ReauthorizeRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = Format(amount) }
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), ct);
        return Authorization(result);
    }

    public async Task<ProviderCapture> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken ct)
    {
        var result = await CallAsync(token => _client.Payments.CaptureAuthorizedPayment(
            authorizationId: authorizationId,
            payPalMockResponse: null,
            payPalRequestId: requestId,
            payPalAuthAssertion: null,
            body: new CaptureRequest
            {
                Amount = new Money { CurrencyCode = currency, Value = Format(amount) },
                FinalCapture = true
            },
            prefer: "return=representation",
            requestOptions: null,
            ct: token), ct);
        var captured = Parse(result.Amount?.Value)
            ?? throw new PaymentProviderException("PayPal did not return the captured amount.");
        return new ProviderCapture(
            result.Id ?? throw new PaymentProviderException("PayPal did not return a capture identifier."),
            result.Status?.Value,
            captured,
            Parse(result.SellerReceivableBreakdown?.PaypalFee?.Value),
            Parse(result.SellerReceivableBreakdown?.NetAmount?.Value),
            ParseDate(result.CreateTime));
    }

    public async Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        var result = await CallAsync(token => _client.Payments.VoidPayment(
            authorizationId, null, null, requestId, "return=representation", null, token), ct);
        return result.Status?.Value;
    }

    public async Task<ProviderRefund> RefundAsync(string captureId, decimal? amount, string currency,
        string requestId, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = amount is null ? null : new Money
            {
                CurrencyCode = currency,
                Value = Format(amount.Value)
            }
        };
        var result = await CallAsync(token => _client.Payments.RefundCapturedPayment(
            captureId, null, requestId, null, body, "return=representation", null, token), ct);
        return new ProviderRefund(
            result.Id ?? throw new PaymentProviderException("PayPal did not return a refund identifier."),
            result.Status?.Value,
            Parse(result.Amount?.Value) ?? amount
                ?? throw new PaymentProviderException("PayPal did not return the refunded amount."),
            ParseDate(result.CreateTime));
    }

    public async Task<ProviderSavedMethod> SaveMethodAsync(string ownerId, ProviderCard card,
        string requestId, CancellationToken ct)
    {
        var result = await CallAsync(token => _client.Vault.CreatePaymentToken(
            payPalRequestId: requestId,
            body: new PaymentTokenRequest
            {
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = ToAddress(card.BillingAddress)
                    }
                },
                Customer = new Customer { MerchantCustomerId = ownerId }
            },
            requestOptions: null,
            ct: token), ct);
        var safeCard = result.PaymentSource?.Card;
        return new ProviderSavedMethod(
            result.Id ?? throw new PaymentProviderException("PayPal did not return a payment-token identifier."),
            result.Customer?.Id,
            safeCard?.Brand?.Value,
            safeCard?.LastDigits,
            safeCard?.Expiry,
            safeCard?.Type?.Value,
            safeCard?.VerificationStatus?.Value);
    }

    public async Task<IReadOnlyList<ProviderSavedMethod>> ListMethodsAsync(string customerId,
        CancellationToken ct)
    {
        var methods = new List<ProviderSavedMethod>();
        var page = 1;
        while (true)
        {
            var response = await CallAsync(token => _client.Vault.ListCustomerPaymentTokens(
                customerId: customerId, pageSize: 100, page: page, totalRequired: true,
                requestOptions: null, ct: token), ct);
            foreach (var token in response.PaymentTokens ?? Array.Empty<PaymentTokenResponse>())
            {
                var card = token.PaymentSource?.Card;
                if (token.Id is not null)
                    methods.Add(new ProviderSavedMethod(token.Id, response.Customer?.Id,
                        card?.Brand?.Value, card?.LastDigits, card?.Expiry, card?.Type?.Value,
                        card?.VerificationStatus?.Value));
            }
            if ((response.TotalPages is not null && page >= response.TotalPages) ||
                (response.PaymentTokens?.Count ?? 0) < 100) break;
            page++;
        }
        return methods;
    }

    public Task DeleteMethodAsync(string tokenId, CancellationToken ct) =>
        CallAsync(token => _client.Vault.DeletePaymentToken(tokenId, null, token), ct);

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        var transactions = new Dictionary<string, ProviderTransaction>(StringComparer.Ordinal);
        var page = 1;
        while (true)
        {
            var response = await CallAsync(token => _client.TransactionSearch.SearchTransactions(
                startDate: FormatReportingInstant(from),
                endDate: FormatReportingInstant(to),
                transactionId: null, transactionType: null, transactionStatus: null,
                transactionAmount: null, transactionCurrency: null, paymentInstrumentType: null,
                storeId: null, terminalId: null, fields: "transaction_info",
                balanceAffectingRecordsOnly: "Y", pageSize: 100, page: page,
                requestOptions: null, ct: token), ct);
            var details = response.TransactionDetails ?? Array.Empty<TransactionDetails>();
            foreach (var detail in details)
            {
                var info = detail.TransactionInfo;
                if (info?.TransactionId is null) continue;
                transactions[info.TransactionId] = new ProviderTransaction(
                    info.TransactionId, info.PaypalReferenceId, info.TransactionStatus,
                    Parse(info.TransactionAmount?.Value), Parse(info.FeeAmount?.Value),
                    info.TransactionAmount?.CurrencyCode, ParseDate(info.TransactionInitiationDate),
                    info.InvoiceId, info.CustomField);
            }
            if ((response.TotalPages is not null && page >= response.TotalPages) || details.Count < 100) break;
            page++;
        }
        return transactions.Values.ToList();
    }

    private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_callBudget);
        try
        {
            return await call(linked.Token);
        }
        catch (PaymentProviderException) { throw; }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("create order", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Rejected("create order", raw, ex);
            throw Rejected("create order", ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("authorize order", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Rejected("authorize order", raw, ex);
            throw Rejected("authorize order", ex);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("get authorization", error, ex);
            if (ex.Error.TryGetNoContent(out var raw)) throw Rejected("get authorization", raw, ex);
            if (ex.Error.TryGetRawError(out raw)) throw Rejected("get authorization", raw, ex);
            throw Rejected("get authorization", ex);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("reauthorize payment", error, ex);
            if (ex.Error.TryGetNoContent(out var raw)) throw Rejected("reauthorize payment", raw, ex);
            if (ex.Error.TryGetRawError(out raw)) throw Rejected("reauthorize payment", raw, ex);
            throw Rejected("reauthorize payment", ex);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("capture authorization", error, ex);
            if (ex.Error.TryGetNoContent(out var raw)) throw Rejected("capture authorization", raw, ex);
            if (ex.Error.TryGetRawError(out raw)) throw Rejected("capture authorization", raw, ex);
            throw Rejected("capture authorization", ex);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("void authorization", error, ex);
            if (ex.Error.TryGetNoContent(out var raw)) throw Rejected("void authorization", raw, ex);
            if (ex.Error.TryGetRawError(out raw)) throw Rejected("void authorization", raw, ex);
            throw Rejected("void authorization", ex);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Rejected("refund capture", error, ex);
            if (ex.Error.TryGetNoContent(out var raw)) throw Rejected("refund capture", raw, ex);
            if (ex.Error.TryGetRawError(out raw)) throw Rejected("refund capture", raw, ex);
            throw Rejected("refund capture", ex);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw Rejected("save payment method", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Rejected("save payment method", raw, ex);
            throw Rejected("save payment method", ex);
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw Rejected("list payment methods", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Rejected("list payment methods", raw, ex);
            throw Rejected("list payment methods", ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Rejected("search transactions", ex.Error, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new PaymentProviderException("PayPal could not return a processable response.", 502, ex);
        }
        catch (Exception ex)
        {
            throw new PaymentProviderException("PayPal rejected the payment operation.", 422, ex);
        }
    }

    private async Task CallAsync(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(_callBudget);
        try { await call(linked.Token); }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw Rejected("delete payment method", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Rejected("delete payment method", raw, ex);
            throw Rejected("delete payment method", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        { throw new PaymentProviderException("PayPal could not return a processable response.", 502, ex); }
        catch (Exception ex)
        { throw new PaymentProviderException("PayPal rejected the payment operation.", 422, ex); }
    }

    private static ProviderAuthorization Authorization(PaymentAuthorization result) => new(
        result.Id ?? throw new PaymentProviderException("PayPal did not return an authorization identifier."),
        result.Status?.Value,
        Parse(result.Amount?.Value) ?? throw new PaymentProviderException("PayPal did not return the authorization amount."),
        ParseDate(result.CreateTime),
        ParseDate(result.ExpirationTime));

    private static Address ToAddress(BillingAddressInput address) => new()
    {
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.State,
        PostalCode = address.PostalCode,
        CountryCode = address.CountryCode
    };

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static decimal? Parse(string? value) => decimal.TryParse(value,
        NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) => DateTimeOffset.TryParse(value,
        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

    private static PaymentProviderException Rejected(string operation, Error error, Exception inner) =>
        new(ProviderDiagnostic(operation, error.Name, error.DebugId,
            error.Details?.Select(x => DiagnosticIssue(x.Issue, x.Field))), 422, inner);

    private static PaymentProviderException Rejected(string operation, Error1 error, Exception inner) =>
        new(ProviderDiagnostic(operation, error.Name, error.DebugId,
            error.Details?.Select(x => DiagnosticIssue(x.Issue, x.Field))), 422, inner);

    private static PaymentProviderException Rejected(string operation, RawError error, Exception inner)
    {
        var status = (int)error.StatusCode;
        var publicStatus = status is >= 400 and <= 599 ? status : 502;
        try
        {
            var typed = error.ReadAsJson<Error>();
            if (typed is not null && !string.IsNullOrWhiteSpace(typed.Name))
                return new PaymentProviderException(ProviderDiagnostic(operation, typed.Name, typed.DebugId,
                    typed.Details?.Select(x => DiagnosticIssue(x.Issue, x.Field))), publicStatus, inner);
        }
        catch (JsonException)
        {
            // Raw fallback bodies are not guaranteed to be JSON. Do not expose their contents.
        }
        return new PaymentProviderException(
            $"PayPal {operation} rejected the request with HTTP {status}.", publicStatus, inner);
    }

    private static PaymentProviderException Rejected(string operation, Exception inner) =>
        new($"PayPal {operation} rejected the request without a readable diagnostic.", 422, inner);

    private static string ProviderDiagnostic(string operation, string name, string debugId,
        IEnumerable<string>? issues)
    {
        var safeIssues = issues?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray()
            ?? Array.Empty<string>();
        var issueText = safeIssues.Length == 0 ? string.Empty : $" Issues: {string.Join("; ", safeIssues)}.";
        var debugText = string.IsNullOrWhiteSpace(debugId) ? string.Empty : $" PayPal debug ID: {debugId}.";
        return $"PayPal {operation} rejected the request: {name}.{issueText}{debugText}";
    }

    private static string DiagnosticIssue(string issue, string? field) =>
        string.IsNullOrWhiteSpace(field) ? issue : $"{issue} ({field})";

    private static string FormatReportingInstant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string BuildInvoiceId(string createRequestId)
    {
        const string prefix = "eshop-";
        if (createRequestId.Length <= 127 - prefix.Length)
            return prefix + createRequestId;

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(createRequestId));
        return prefix + Convert.ToHexString(digest).ToLowerInvariant();
    }
}
