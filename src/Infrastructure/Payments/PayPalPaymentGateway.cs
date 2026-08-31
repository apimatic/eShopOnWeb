using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<SavedCardResult> SaveCardAsync(string merchantCustomerId,
        SensitiveCardDetails card, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            var response = await Bounded(ct => _client.Vault.CreatePaymentToken(
                payPalRequestId: requestId,
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = CreateVaultCard(card)
                    }
                },
                ct: ct), cancellationToken);

            var tokenCard = response.PaymentSource?.Card;
            if (string.IsNullOrWhiteSpace(response.Id) || tokenCard is null ||
                string.IsNullOrWhiteSpace(tokenCard.LastDigits))
            {
                throw Drift("save-card");
            }

            return new SavedCardResult(response.Id, response.Customer?.Id, tokenCard.Name,
                tokenCard.Brand?.Value, tokenCard.LastDigits, tokenCard.Expiry,
                tokenCard.Type?.Value, tokenCard.VerificationStatus?.Value);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw Typed("save-card", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("save-card", raw, ex);
            throw Unknown("save-card", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("save-card", ex); }
    }

    public async Task<SavedCardResult> GetSavedCardAsync(string tokenId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Vault.GetPaymentToken(tokenId, ct: ct),
                cancellationToken);
            var tokenCard = response.PaymentSource?.Card;
            if (string.IsNullOrWhiteSpace(response.Id) || tokenCard is null)
            {
                throw Drift("get-saved-card");
            }

            return new SavedCardResult(response.Id, response.Customer?.Id, tokenCard.Name,
                tokenCard.Brand?.Value, tokenCard.LastDigits, tokenCard.Expiry,
                tokenCard.Type?.Value, tokenCard.VerificationStatus?.Value);
        }
        catch (SdkException<GetPaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw Typed("get-saved-card", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("get-saved-card", raw, ex);
            throw Unknown("get-saved-card", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("get-saved-card", ex); }
    }

    public async Task DeleteSavedCardAsync(string tokenId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            await Bounded(async ct =>
            {
                await _client.Vault.DeletePaymentToken(tokenId, ct: ct);
                return true;
            }, cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw Typed("delete-saved-card", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("delete-saved-card", raw, ex);
            throw Unknown("delete-saved-card", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("delete-saved-card", ex); }
    }

    public async Task<AuthorizationResult> AuthorizeAsync(int orderId, decimal amount,
        string currency, SensitiveCardDetails? card, string? savedCardTokenId, string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            var createResponse = await Bounded(ct => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"{requestId}-create",
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
                            Amount = MoneyBreakdown(amount, currency),
                            InvoiceId = orderId.ToString(CultureInfo.InvariantCulture),
                            CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                        }
                    ]
                },
                prefer: "return=representation",
                ct: ct), cancellationToken);

            if (string.IsNullOrWhiteSpace(createResponse.Id)) throw Drift("create-order");

            var authorizeBody = new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource
                {
                    Card = savedCardTokenId is not null
                        ? new CardRequest { VaultId = savedCardTokenId }
                        : CreateOrderCard(card ?? throw new InvalidOperationException("Card data is required."))
                }
            };

            var response = await Bounded(ct => _client.Orders.AuthorizeOrder(
                id: createResponse.Id,
                payPalMockResponse: null,
                payPalRequestId: $"{requestId}-authorize",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeBody,
                prefer: "return=representation",
                ct: ct), cancellationToken);

            var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?
                .Authorizations?.FirstOrDefault();
            var payerAction = response.Status == OrderStatus.PayerActionRequired;
            if (payerAction)
            {
                return new AuthorizationResult(response.Id ?? createResponse.Id,
                    response.Status?.Value ?? "PAYER_ACTION_REQUIRED", string.Empty,
                    "PAYER_ACTION_REQUIRED", amount, currency, null, null, true);
            }

            if (string.IsNullOrWhiteSpace(response.Id) || authorization is null ||
                string.IsNullOrWhiteSpace(authorization.Id) || authorization.Status is null ||
                authorization.Amount is null)
            {
                throw Drift("authorize-order");
            }

            return new AuthorizationResult(response.Id, response.Status?.Value ?? "UNKNOWN",
                authorization.Id, authorization.Status.Value,
                ParseMoney(authorization.Amount, currency, "authorize-order"),
                authorization.Amount.CurrencyCode, ParseDate(authorization.CreateTime),
                ParseDate(authorization.ExpirationTime), false);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("create-order", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("create-order", raw, ex);
            throw Unknown("create-order", ex);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("authorize-order", error, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("authorize-order", raw, ex);
            throw Unknown("authorize-order", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("authorize-order", ex); }
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId, payPalMockResponse: null,
                payPalAuthAssertion: null, ct: ct), cancellationToken);
            return ToAuthorization(result, "get-authorization");
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("get-authorization", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("get-authorization", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("get-authorization", raw, ex);
            throw Unknown("get-authorization", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("get-authorization", ex); }
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(string authorizationId,
        decimal amount, string currency, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            var result = await Bounded(ct => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId, payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = Money(amount, currency) },
                prefer: "return=representation", ct: ct), cancellationToken);
            return ToAuthorization(result, "reauthorize");
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("reauthorize", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("reauthorize", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("reauthorize", raw, ex);
            throw Unknown("reauthorize", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("reauthorize", ex); }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount,
        string currency, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            var captured = await Bounded(ct => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId, payPalMockResponse: null,
                payPalRequestId: requestId, payPalAuthAssertion: null,
                body: new CaptureRequest { Amount = Money(amount, currency), FinalCapture = true },
                prefer: "return=representation", ct: ct), cancellationToken);
            if (string.IsNullOrWhiteSpace(captured.Id)) throw Drift("capture");

            var result = await Bounded(ct => _client.Payments.GetCapturedPayment(
                captureId: captured.Id, payPalMockResponse: null, ct: ct), cancellationToken);
            if (result.Amount is null || result.SellerReceivableBreakdown?.GrossAmount is null ||
                string.IsNullOrWhiteSpace(result.Id) || result.Status is null)
            {
                throw Drift("get-capture");
            }

            var breakdown = result.SellerReceivableBreakdown;
            return new CaptureResult(result.Id, result.Status.Value,
                ParseMoney(breakdown.GrossAmount, currency, "get-capture"),
                breakdown.GrossAmount.CurrencyCode,
                ParseOptionalMoney(breakdown.PaypalFee, currency, "get-capture"),
                ParseOptionalMoney(breakdown.NetAmount, currency, "get-capture"));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("capture", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("capture", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("capture", raw, ex);
            throw Unknown("capture", ex);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("get-capture", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("get-capture", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("get-capture", raw, ex);
            throw Unknown("get-capture", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("capture", ex); }
    }

    public async Task<AuthorizationSnapshot> VoidAsync(string authorizationId, string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            var result = await Bounded(ct => _client.Payments.VoidPayment(
                authorizationId: authorizationId, payPalMockResponse: null,
                payPalAuthAssertion: null, payPalRequestId: requestId,
                prefer: "return=representation", ct: ct), cancellationToken);
            return ToAuthorization(result, "void");
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("void", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("void", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("void", raw, ex);
            throw Unknown("void", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("void", ex); }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount,
        string currency, bool fullRemaining, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = ProviderWriteScope.Begin();
            var body = fullRemaining
                ? new RefundRequest()
                : new RefundRequest { Amount = Money(amount, currency) };
            var result = await Bounded(ct => _client.Payments.RefundCapturedPayment(
                captureId: captureId, payPalMockResponse: null, payPalRequestId: requestId,
                payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Id) || result.Status is null || result.Amount is null)
            {
                throw Drift("refund");
            }
            return new RefundResult(result.Id, result.Status.Value,
                ParseMoney(result.Amount, currency, "refund"), result.Amount.CurrencyCode);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("refund", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("refund", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("refund", raw, ex);
            throw Unknown("refund", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("refund", ex); }
    }

    public async Task<RefundResult> GetRefundAsync(string refundId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await Bounded(ct => _client.Payments.GetRefund(refundId,
                payPalMockResponse: null, payPalAuthAssertion: null, ct: ct), cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Id) || result.Status is null || result.Amount is null)
            {
                throw Drift("get-refund");
            }
            return new RefundResult(result.Id, result.Status.Value,
                ParseMoney(result.Amount, result.Amount.CurrencyCode, "get-refund"),
                result.Amount.CurrencyCode);
        }
        catch (SdkException<GetRefundError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw Typed("get-refund", error, ex);
            if (ex.Error.TryGetNoContent(out var noContent)) throw Raw("get-refund", noContent, ex);
            if (ex.Error.TryGetRawError(out var raw)) throw Raw("get-refund", raw, ex);
            throw Unknown("get-refund", ex);
        }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("get-refund", ex); }
    }

    public async Task<ProviderTransactionReport> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        try
        {
            var transactions = new List<ProviderTransaction>();
            DateTimeOffset? refreshedAt = null;
            var page = 1;
            var totalPages = 1;
            do
            {
                var response = await Bounded(ct => _client.TransactionSearch.SearchTransactions(
                    startDate: from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    endDate: to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    transactionId: null, transactionType: null, transactionStatus: null,
                    transactionAmount: null, transactionCurrency: null,
                    paymentInstrumentType: null, storeId: null, terminalId: null,
                    fields: "transaction_info", balanceAffectingRecordsOnly: "Y",
                    pageSize: 100, page: page, ct: ct), cancellationToken);

                refreshedAt ??= ParseDate(response.LastRefreshedDatetime);
                foreach (var detail in response.TransactionDetails ?? [])
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    transactions.Add(new ProviderTransaction(info.TransactionId,
                        info.PaypalReferenceId, info.PaypalReferenceIdType?.Value,
                        info.TransactionEventCode, ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate),
                        ParseOptionalMoney(info.TransactionAmount, null, "reconciliation"),
                        info.TransactionAmount?.CurrencyCode,
                        ParseOptionalMoney(info.FeeAmount, null, "reconciliation"),
                        info.TransactionStatus, info.InvoiceId, info.CustomField));
                }
                totalPages = Math.Max(response.TotalPages ?? 1, 1);
                page++;
            } while (page <= totalPages);

            return new ProviderTransactionReport(transactions, refreshedAt);
        }
        catch (SdkException<RawError> ex) { throw Raw("reconciliation", ex.Error, ex); }
        catch (Exception ex) when (IsBoundaryException(ex)) { throw Boundary("reconciliation", ex); }
    }

    private static PaymentTokenRequestCard CreateVaultCard(SensitiveCardDetails card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = CreateAddress(card)
    };

    private static CardRequest CreateOrderCard(SensitiveCardDetails card) => new()
    {
        Name = card.Name,
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        BillingAddress = CreateAddress(card)
    };

    private static Address CreateAddress(SensitiveCardDetails card) => new()
    {
        AddressLine1 = card.AddressLine1,
        AddressLine2 = card.AddressLine2,
        AdminArea2 = card.City,
        AdminArea1 = card.State,
        PostalCode = card.PostalCode,
        CountryCode = card.CountryCode ?? string.Empty
    };

    private static Money Money(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static AmountWithBreakdown MoneyBreakdown(decimal amount, string currency) => new()
    {
        CurrencyCode = currency,
        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
    };

    private static AuthorizationSnapshot ToAuthorization(PaymentAuthorization result, string operation)
    {
        if (string.IsNullOrWhiteSpace(result.Id) || result.Status is null || result.Amount is null)
            throw Drift(operation);
        return new AuthorizationSnapshot(result.Id, result.Status.Value,
            ParseMoney(result.Amount, result.Amount.CurrencyCode, operation),
            result.Amount.CurrencyCode, ParseDate(result.CreateTime), ParseDate(result.ExpirationTime),
            result.StatusDetails?.Reason?.Value);
    }

    private static decimal ParseMoney(Money money, string? expectedCurrency, string operation)
    {
        if (!string.IsNullOrWhiteSpace(expectedCurrency) &&
            !string.Equals(money.CurrencyCode, expectedCurrency, StringComparison.OrdinalIgnoreCase))
            throw Drift(operation);
        if (!decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            throw Drift(operation);
        return value;
    }

    private static decimal? ParseOptionalMoney(Money? money, string? expectedCurrency, string operation) =>
        money is null ? null : ParseMoney(money, expectedCurrency, operation);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
            out var parsed) ? parsed : null;

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TotalCallBudget);
        return await call(cts.Token);
    }

    private static bool IsBoundaryException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or
            DuplicateProviderWriteBlockedException;

    private static PaymentProviderException Boundary(string operation, Exception ex) => ex switch
    {
        DuplicateProviderWriteBlockedException => new PaymentProviderException(operation,
            "The PayPal request may have succeeded, but its result could not be confirmed. Retry the same application operation to reconcile it.",
            HttpStatusCode.Conflict, ex),
        JsonException => new PaymentProviderException(operation,
            "PayPal returned a response that could not be processed.", HttpStatusCode.BadGateway, ex),
        _ => new PaymentProviderException(operation,
            "PayPal is temporarily unavailable or the request timed out.", HttpStatusCode.ServiceUnavailable, ex)
    };

    private static PaymentProviderException Typed(string operation, Error error, Exception inner) =>
        new(operation, ProviderMessage(error.Name, error.Message, error.DebugId),
            HttpStatusCode.UnprocessableEntity, inner);

    private static PaymentProviderException Typed(string operation, Error1 error, Exception inner) =>
        new(operation, ProviderMessage(error.Name, error.Message, error.DebugId),
            HttpStatusCode.UnprocessableEntity, inner);

    private static string ProviderMessage(string name, string message, string debugId) =>
        $"PayPal rejected the request ({name}). {message} Reference: {debugId}.";

    private static PaymentProviderException Raw(string operation, RawError raw, Exception inner) =>
        new(operation, $"PayPal rejected the {operation} request with HTTP {(int)raw.StatusCode}.",
            raw.StatusCode, inner);

    private static PaymentProviderException Unknown(string operation, Exception inner) =>
        new(operation, $"PayPal rejected the {operation} request.",
            HttpStatusCode.UnprocessableEntity, inner);

    private static PaymentProviderException Drift(string operation) =>
        new(operation, "PayPal returned an incomplete response; the payment outcome needs review.",
            HttpStatusCode.BadGateway);
}
