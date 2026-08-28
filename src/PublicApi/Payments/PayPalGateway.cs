using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway(PayPalServerSdkClient client) : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    public Task<PayPalAuthorizationResult> AuthorizeAsync(string orderReference, decimal amount, string currency,
        string createRequestId, string authorizeRequestId, CardInput? card, string? vaultId,
        CancellationToken cancellationToken) => Bounded(async ct =>
    {
        PayPalServerSdk.Models.Order payPalOrder;
        try
        {
            payPalOrder = await client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: createRequestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits =
                    [
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = currency,
                                Value = MoneyValue(amount)
                            },
                            InvoiceId = orderReference,
                            CustomId = orderReference
                        }
                    ]
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        if (string.IsNullOrWhiteSpace(payPalOrder.Id))
            throw new PayPalProviderException("PayPal did not return an order identifier.");

        var paymentCard = vaultId is not null
            ? new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            }
            : ToCard(card ?? throw new ArgumentException("Card details are required for a one-off payment.", nameof(card)));

        OrderAuthorizeResponse authorization;
        try
        {
            authorization = await client.Orders.AuthorizeOrder(
                id: payPalOrder.Id,
                payPalMockResponse: null,
                payPalRequestId: authorizeRequestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource { Card = paymentCard }
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        var orderStatus = authorization.Status?.Value;
        if (orderStatus == OrderStatus.PayerActionRequired.Value)
        {
            return new PayPalAuthorizationResult(
                authorization.Id ?? payPalOrder.Id, orderStatus, null, null, null, null, null, null, true);
        }

        var providerAuthorization = authorization.PurchaseUnits?
            .SelectMany(x => x.Payments?.Authorizations ?? [])
            .SingleOrDefault();
        if (providerAuthorization?.Id is null)
            throw new PayPalProviderException("PayPal did not return a card authorization.");

        var heldAmount = ParseMoney(providerAuthorization.Amount?.Value, "authorization amount");
        EnsureMoney(amount, currency, heldAmount, providerAuthorization.Amount?.CurrencyCode);

        return new PayPalAuthorizationResult(
            authorization.Id ?? payPalOrder.Id,
            orderStatus,
            providerAuthorization.Id,
            providerAuthorization.Status?.Value,
            heldAmount,
            providerAuthorization.Amount?.CurrencyCode,
            ParseDate(providerAuthorization.CreateTime),
            ParseDate(providerAuthorization.ExpirationTime),
            false);
    }, cancellationToken);

    public Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken) => Bounded(async ct =>
    {
        PaymentAuthorization result;
        try
        {
            result = await client.Payments.GetAuthorizedPayment(authorizationId, null, null, ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        return ToAuthorization(result);
    }, cancellationToken);

    public Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken) => Bounded(async ct =>
    {
        PaymentAuthorization result;
        try
        {
            result = await client.Payments.ReauthorizePayment(
                authorizationId,
                requestId,
                null,
                new ReauthorizeRequest { Amount = Money(amount, currency) },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        var mapped = ToAuthorization(result);
        EnsureMoney(amount, currency, mapped.Amount, mapped.Currency);
        return mapped;
    }, cancellationToken);

    public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency,
        string requestId, CancellationToken cancellationToken) => Bounded(async ct =>
    {
        CapturedPayment result;
        try
        {
            result = await client.Payments.CaptureAuthorizedPayment(
                authorizationId,
                null,
                requestId,
                null,
                new CaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        var mapped = ToCapture(result);
        EnsureMoney(amount, currency, mapped.Amount, mapped.Currency);
        return mapped;
    }, cancellationToken);

    public Task<PayPalCaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            CapturedPayment result;
            try
            {
                result = await client.Payments.GetCapturedPayment(captureId, null, ct: ct);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                throw ProviderFailure(ex.Error, ex);
            }

            return ToCapture(result);
        }, cancellationToken);

    public Task<string?> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            PaymentAuthorization result;
            try
            {
                result = await client.Payments.VoidPayment(
                    authorizationId, null, null, requestId, prefer: "return=representation", ct: ct);
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                throw ProviderFailure(ex.Error, ex);
            }

            return result.Status?.Value;
        }, cancellationToken);

    public Task<PayPalRefundResult> RefundAsync(string captureId, decimal amount, string currency,
        string idempotencyKey, CancellationToken cancellationToken) => Bounded(async ct =>
    {
        Refund result;
        try
        {
            result = await client.Payments.RefundCapturedPayment(
                captureId,
                null,
                idempotencyKey,
                null,
                new RefundRequest { Amount = Money(amount, currency) },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        return ToRefund(result);
    }, cancellationToken);

    public Task<PayPalRefundResult> GetRefundAsync(string refundId, CancellationToken cancellationToken) =>
        Bounded(async ct =>
        {
            Refund result;
            try
            {
                result = await client.Payments.GetRefund(refundId, null, null, ct: ct);
            }
            catch (SdkException<GetRefundError> ex)
            {
                throw ProviderFailure(ex.Error, ex);
            }

            return ToRefund(result);
        }, cancellationToken);

    public Task<PayPalVaultResult> SaveCardAsync(string buyerId, CardInput card, string requestId,
        CancellationToken cancellationToken) => Bounded(async ct =>
    {
        PaymentTokenResponse result;
        try
        {
            result = await client.Vault.CreatePaymentToken(
                requestId,
                new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = buyerId },
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
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }

        if (result.Id is null) throw new PayPalProviderException("PayPal did not return a payment token identifier.");
        var responseCard = result.PaymentSource?.Card;
        return new PayPalVaultResult(result.Id, responseCard?.Name, responseCard?.Brand?.Value,
            responseCard?.LastDigits, responseCard?.Expiry, responseCard?.Type?.Value);
    }, cancellationToken);

    public Task DeleteSavedCardAsync(string vaultId, CancellationToken cancellationToken) => Bounded(async ct =>
    {
        try
        {
            await client.Vault.DeletePaymentToken(vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw ProviderFailure(ex.Error, ex);
        }
    }, cancellationToken);

    public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken) => Bounded(async ct =>
    {
        var transactions = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;
        do
        {
            SearchResponse result;
            try
            {
                result = await client.TransactionSearch.SearchTransactions(
                    startDate: from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    endDate: to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
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
                throw TransactionSearchFailure(ex);
            }

            foreach (var detail in result.TransactionDetails ?? [])
            {
                var info = detail.TransactionInfo;
                if (info is null) continue;
                transactions.Add(new PayPalTransaction(
                    info.TransactionId,
                    info.PaypalReferenceId,
                    info.TransactionEventCode,
                    info.TransactionStatus,
                    ParseDate(info.TransactionInitiationDate),
                    ParseDate(info.TransactionUpdatedDate),
                    ParseOptionalMoney(info.TransactionAmount?.Value),
                    info.TransactionAmount?.CurrencyCode,
                    ParseOptionalMoney(info.FeeAmount?.Value),
                    info.InvoiceId,
                    info.CustomField));
            }

            totalPages = Math.Max(1, checked((int)(result.TotalPages ?? 1)));
            page++;
        } while (page <= totalPages);

        return (IReadOnlyList<PayPalTransaction>)transactions;
    }, cancellationToken);

    private static PayPalProviderException TransactionSearchFailure(SdkException<RawError> exception)
    {
        var status = (int)exception.Error.StatusCode;
        var raw = exception.Error.ReadAsString();
        if (string.IsNullOrWhiteSpace(raw))
            return new PayPalProviderException($"PayPal transaction search failed with HTTP {status}.",
                innerException: exception);

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var name = JsonText(root, "name");
            var debugId = JsonText(root, "debug_id");
            var message = JsonText(root, "message");
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array &&
                details.GetArrayLength() > 0)
            {
                var description = JsonText(details[0], "description");
                if (!string.IsNullOrWhiteSpace(description)) message = description;
            }

            var diagnostic = string.IsNullOrWhiteSpace(message) ? string.Empty : $" {SafeDiagnostic(message)}";
            return new PayPalProviderException(
                $"PayPal transaction search failed with HTTP {status}.{diagnostic}", name, debugId, exception);
        }
        catch (JsonException)
        {
            return new PayPalProviderException(
                $"PayPal transaction search failed with HTTP {status}. {SafeDiagnostic(raw)}",
                innerException: exception);
        }
    }

    private static string? JsonText(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string SafeDiagnostic(string value)
    {
        var singleLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 500 ? singleLine : singleLine[..500];
    }

    private static CardRequest ToCard(CardInput card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static Address? ToAddress(BillingAddressInput? address) => address is null ? null : new Address
    {
        CountryCode = address.CountryCode,
        AddressLine1 = address.AddressLine1,
        AddressLine2 = address.AddressLine2,
        AdminArea2 = address.City,
        AdminArea1 = address.Region,
        PostalCode = address.PostalCode
    };

    private static PayPalServerSdk.Models.Money Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = MoneyValue(amount)
    };

    private static string MoneyValue(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string? value, string field) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : throw new PayPalProviderException($"PayPal returned an invalid {field}.");

    private static decimal? ParseOptionalMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;

    private static void EnsureMoney(decimal expectedAmount, string expectedCurrency, decimal? actualAmount,
        string? actualCurrency)
    {
        if (actualAmount != expectedAmount ||
            !string.Equals(actualCurrency, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            throw new PayPalProviderException("PayPal returned an amount or currency that did not match the order.");
    }

    private static PayPalAuthorizationResult ToAuthorization(PaymentAuthorization result)
    {
        if (result.Id is null) throw new PayPalProviderException("PayPal did not return an authorization identifier.");
        return new PayPalAuthorizationResult(
            string.Empty,
            null,
            result.Id,
            result.Status?.Value,
            ParseOptionalMoney(result.Amount?.Value),
            result.Amount?.CurrencyCode,
            ParseDate(result.CreateTime),
            ParseDate(result.ExpirationTime),
            false);
    }

    private static PayPalCaptureResult ToCapture(CapturedPayment result)
    {
        if (result.Id is null || result.Amount is null)
            throw new PayPalProviderException("PayPal returned an incomplete capture.");
        var breakdown = result.SellerReceivableBreakdown;
        return new PayPalCaptureResult(
            result.Id,
            result.Status?.Value,
            ParseMoney(result.Amount.Value, "capture amount"),
            result.Amount.CurrencyCode,
            ParseOptionalMoney(breakdown?.PaypalFee?.Value),
            ParseOptionalMoney(breakdown?.NetAmount?.Value),
            ParseDate(result.CreateTime));
    }

    private static PayPalRefundResult ToRefund(Refund result)
    {
        if (result.Id is null || result.Amount is null)
            throw new PayPalProviderException("PayPal returned an incomplete refund.");
        return new PayPalRefundResult(result.Id, result.Status?.Value,
            ParseMoney(result.Amount.Value, "refund amount"), result.Amount.CurrencyCode,
            ParseDate(result.UpdateTime));
    }

    private static PayPalProviderException ProviderFailure(ApiError error, Exception source)
    {
        if (error is null) return new PayPalProviderException("PayPal rejected the request.", innerException: source);
        if (error.TryGetRawError(out var raw))
            return new PayPalProviderException($"PayPal rejected the request with HTTP {(int)raw.StatusCode}.", innerException: source);
        return new PayPalProviderException("PayPal rejected the request.", innerException: source);
    }

    private static PayPalProviderException ProviderFailure(CreateOrderError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(AuthorizeOrderError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(GetAuthorizedPaymentError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(ReauthorizePaymentError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(CaptureAuthorizedPaymentError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(GetCapturedPaymentError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(VoidPaymentError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(RefundCapturedPaymentError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);
    private static PayPalProviderException ProviderFailure(GetRefundError error, Exception source) =>
        error.TryGetError(out var detail) ? ProviderFailure(detail, source) : ProviderFailure((ApiError)error, source);

    private static PayPalProviderException ProviderFailure(CreatePaymentTokenError error, Exception source)
        => ProviderFailure((ApiError)error, source);

    private static PayPalProviderException ProviderFailure(DeletePaymentTokenError error, Exception source)
        => ProviderFailure((ApiError)error, source);

    private static PayPalProviderException ProviderFailure(Error detail, Exception source)
    {
        var issue = detail.Details?.FirstOrDefault();
        var message = issue?.Description ?? detail.Message;
        return new PayPalProviderException(message, detail.Name, detail.DebugId, source);
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(CallBudget);
        try
        {
            return await operation(linked.Token);
        }
        catch (PayPalProviderException)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalProviderException("PayPal could not be reached or did not respond in time.", innerException: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalProviderException("PayPal returned a response that could not be processed.", innerException: ex);
        }
    }

    private static async Task Bounded(Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await Bounded(async ct =>
        {
            await operation(ct);
            return true;
        }, cancellationToken);
    }
}
