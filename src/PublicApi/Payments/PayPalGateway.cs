using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PayPalGateway : IPayPalGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;
    private readonly PayPalResponseStatusContext _statusContext;

    public PayPalGateway(
        PayPalServerSdkClient client,
        PayPalOptions options,
        PayPalResponseStatusContext statusContext)
    {
        _client = client;
        _options = options;
        _statusContext = statusContext;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(
        int localOrderId,
        decimal amount,
        CardInput? card,
        string? vaultId,
        string createRequestId,
        string authorizeRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
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
                : ToDirectCard(card ?? throw new InvalidOperationException("Card details are required."));

            var order = await Bounded(
                ct => _client.Orders.CreateOrder(
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
                                    CurrencyCode = _options.Currency,
                                    Value = _options.Format(amount)
                                },
                                InvoiceId = CreateInvoiceId(localOrderId, createRequestId),
                                CustomId = localOrderId.ToString(CultureInfo.InvariantCulture),
                                ReferenceId = $"eshop-order-{localOrderId}"
                            }
                        ],
                        PaymentSource = new PaymentSource { Card = paymentCard }
                    },
                    prefer: "return=representation",
                    ct: ct), cancellationToken);

            if (string.IsNullOrWhiteSpace(order.Id))
                throw InvalidSuccess("PayPal did not return an order identifier.");

            if (order.Status == OrderStatus.PayerActionRequired)
                throw ChallengeRequired();

            var authorization = order.PurchaseUnits?
                .SelectMany(static unit => unit.Payments?.Authorizations ?? [])
                .SingleOrDefault();
            if (authorization is null || string.IsNullOrWhiteSpace(authorization.Id) || authorization.Amount is null)
                throw InvalidSuccess("PayPal did not return the payment authorization.");

            var providerAmount = ParseMoney(authorization.Amount.Value, "authorized amount");
            var providerCurrency = authorization.Amount.CurrencyCode;
            if (providerAmount != amount || !string.Equals(providerCurrency, _options.Currency, StringComparison.Ordinal))
            {
                await VoidAsync(authorization.Id, Guid.NewGuid().ToString("N"), cancellationToken);
                throw new PaymentApiException(
                    "PayPal authorized an amount or currency different from the order total; the hold was released.",
                    HttpStatusCode.BadGateway);
            }

            return new AuthorizationResult(
                order.Id,
                order.Status?.Value ?? string.Empty,
                authorization.Id,
                authorization.Status?.Value ?? string.Empty,
                providerAmount,
                providerCurrency,
                ParseOptionalTimestamp(authorization.CreateTime, "authorization creation time"),
                ParseOptionalTimestamp(authorization.ExpirationTime, "authorization expiration time"));
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate(ex.Error, "PayPal rejected the order authorization request.");
        }
    }

    public async Task<(string Id, string Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt)> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await Bounded(
                ct => _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct), cancellationToken);

            return (
                authorization.Id ?? authorizationId,
                authorization.Status?.Value ?? string.Empty,
                ParseOptionalTimestamp(authorization.CreateTime, "authorization creation time"),
                ParseOptionalTimestamp(authorization.ExpirationTime, "authorization expiration time"));
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate(ex.Error, "PayPal could not retrieve the authorization.");
        }
    }

    public async Task<(string Id, string Status, DateTimeOffset? CreatedAt, DateTimeOffset? ExpiresAt)> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await Bounded(
                ct => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new Money { CurrencyCode = _options.Currency, Value = _options.Format(amount) }
                    },
                    prefer: "return=representation",
                    ct: ct), cancellationToken);

            if (authorization.Amount is null ||
                ParseMoney(authorization.Amount.Value, "reauthorized amount") != amount ||
                authorization.Amount.CurrencyCode != _options.Currency)
                throw InvalidSuccess("PayPal returned a reauthorization for the wrong amount or currency.");

            return (
                authorization.Id ?? authorizationId,
                authorization.Status?.Value ?? string.Empty,
                ParseOptionalTimestamp(authorization.CreateTime, "reauthorization creation time"),
                ParseOptionalTimestamp(authorization.ExpirationTime, "reauthorization expiration time"));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw Translate(ex.Error, "The authorization can no longer be renewed; the shopper must re-pay.");
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        decimal amount,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var capture = await Bounded(
                ct => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest
                    {
                        Amount = new Money { CurrencyCode = _options.Currency, Value = _options.Format(amount) },
                        FinalCapture = true
                    },
                    prefer: "return=representation",
                    ct: ct), cancellationToken);
            return ToCaptureResult(capture);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate(ex.Error, "PayPal could not capture the authorization.");
        }
    }

    public async Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        try
        {
            var capture = await Bounded(
                ct => _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    ct: ct), cancellationToken);
            return ToCaptureResult(capture);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw Translate(ex.Error, "PayPal could not retrieve the capture.");
        }
    }

    public async Task<string> VoidAsync(string authorizationId, string requestId, CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await Bounded(
                ct => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: requestId,
                    prefer: "return=representation",
                    ct: ct), cancellationToken);
            return authorization.Status?.Value ?? string.Empty;
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate(ex.Error, "PayPal could not release the authorization.");
        }
    }

    public async Task<RefundProviderResult> RefundAsync(
        string captureId,
        decimal amount,
        bool fullRemainder,
        string requestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var refund = await Bounded(
                ct => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: requestId,
                    payPalAuthAssertion: null,
                    body: fullRemainder
                        ? new RefundRequest()
                        : new RefundRequest
                        {
                            Amount = new Money { CurrencyCode = _options.Currency, Value = _options.Format(amount) }
                        },
                    prefer: "return=representation",
                    ct: ct), cancellationToken);
            return ToRefundResult(refund, amount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate(ex.Error, "PayPal rejected the refund.");
        }
    }

    public async Task<RefundProviderResult> GetRefundAsync(
        string refundId,
        decimal expectedAmount,
        CancellationToken cancellationToken)
    {
        try
        {
            var refund = await Bounded(
                ct => _client.Payments.GetRefund(
                    refundId: refundId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    ct: ct), cancellationToken);
            return ToRefundResult(refund, expectedAmount);
        }
        catch (SdkException<GetRefundError> ex)
        {
            throw Translate(ex.Error, "PayPal could not retrieve the refund.");
        }
    }

    public async Task<SavedCardProviderResult> SaveCardAsync(
        string buyerId,
        CardInput card,
        string setupRequestId,
        string tokenRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            var setup = await Bounded(
                ct => _client.Vault.CreateSetupToken(
                    payPalRequestId: setupRequestId,
                    body: new SetupTokenRequest
                    {
                        Customer = new Customer { MerchantCustomerId = buyerId },
                        PaymentSource = new SetupTokenRequestPaymentSource
                        {
                            Card = new SetupTokenRequestCard
                            {
                                Name = card.Name,
                                Number = card.Number,
                                Expiry = card.Expiry,
                                SecurityCode = card.SecurityCode,
                                BillingAddress = ToAddress(card.BillingAddress)
                            }
                        }
                    },
                    ct: ct), cancellationToken);

            if (setup.Status == PaymentTokenStatus.PayerActionRequired)
                throw ChallengeRequired();
            if (string.IsNullOrWhiteSpace(setup.Id))
                throw InvalidSuccess("PayPal did not return a setup-token identifier.");

            var token = await Bounded(
                ct => _client.Vault.CreatePaymentToken(
                    payPalRequestId: tokenRequestId,
                    body: new PaymentTokenRequest
                    {
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Token = new VaultTokenRequest
                            {
                                Id = setup.Id,
                                Type = VaultTokenRequestType.SetupToken
                            }
                        }
                    },
                    ct: ct), cancellationToken);

            if (string.IsNullOrWhiteSpace(token.Id) || token.PaymentSource?.Card is null)
                throw InvalidSuccess("PayPal did not return a vaulted card token.");
            var safeCard = token.PaymentSource.Card;
            if (string.IsNullOrWhiteSpace(safeCard.LastDigits))
                throw InvalidSuccess("PayPal did not return safe card recognition details.");

            var customerId = token.Customer?.Id ?? setup.Customer?.Id;
            if (string.IsNullOrWhiteSpace(customerId))
                throw InvalidSuccess("PayPal did not return a vault customer identifier.");

            return new SavedCardProviderResult(
                token.Id,
                customerId,
                safeCard.Brand?.Value,
                safeCard.Type?.Value,
                safeCard.LastDigits,
                safeCard.Expiry);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            throw TranslateVault(ex.Error, "PayPal rejected the card setup request.");
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVault(ex.Error, "PayPal could not create the saved-card token.");
        }
    }

    public async Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(
                async ct =>
                {
                    await _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: ct);
                    return true;
                }, cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateVault(ex.Error, "PayPal could not remove the saved card.");
        }
    }

    public async Task<TransactionSearchResult> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var transactions = new List<ProviderTransaction>();
        DateTimeOffset? lastRefreshed = null;
        var requestedFrom = TruncateToUtcSecond(from);
        var requestedTo = TruncateToUtcSecond(to);
        var currentUtc = TruncateToUtcSecond(DateTimeOffset.UtcNow);
        var effectiveTo = requestedTo > currentUtc ? currentUtc : requestedTo;

        if (requestedFrom > effectiveTo)
            throw new PaymentApiException(
                "The reconciliation start time must not be later than the current UTC time.",
                HttpStatusCode.BadRequest);
        if (requestedFrom == effectiveTo)
            return new TransactionSearchResult(transactions, lastRefreshed);

        try
        {
            var windowStart = requestedFrom;
            while (windowStart <= effectiveTo)
            {
                var maximumWindowEnd = windowStart.AddDays(31).AddSeconds(-1);
                var windowEnd = maximumWindowEnd < effectiveTo ? maximumWindowEnd : effectiveTo;
                var page = 1;
                var totalPages = 1;

                do
                {
                    var response = await Bounded(
                        ct => _client.TransactionSearch.SearchTransactions(
                            startDate: FormatReportingTimestamp(windowStart),
                            endDate: FormatReportingTimestamp(windowEnd),
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
                            pageSize: 100d,
                            page: page,
                            ct: ct), cancellationToken);

                    lastRefreshed = ParseOptionalTimestamp(
                        response.LastRefreshedDatetime,
                        "transaction report refresh time");
                    foreach (var detail in response.TransactionDetails ?? [])
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;
                        transactions.Add(new ProviderTransaction(
                            info.TransactionId,
                            info.PaypalReferenceId,
                            info.TransactionEventCode,
                            ParseOptionalTimestamp(
                                info.TransactionInitiationDate,
                                "transaction initiation time"),
                            info.TransactionStatus,
                            ParseOptionalMoney(info.TransactionAmount?.Value),
                            info.TransactionAmount?.CurrencyCode,
                            ParseOptionalMoney(info.FeeAmount?.Value),
                            info.InvoiceId,
                            info.CustomField));
                    }

                    totalPages = ParsePageCount(response.TotalPages, page);
                    page++;
                } while (page <= totalPages);

                if (windowEnd == effectiveTo)
                    break;
                windowStart = windowEnd.AddSeconds(1);
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw TranslateTransactionSearchRaw(ex.Error, "PayPal transaction reporting failed.");
        }

        return new TransactionSearchResult(transactions, lastRefreshed);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        _statusContext.LastStatus = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (JsonException ex)
        {
            var status = _statusContext.LastStatus;
            if (status is >= HttpStatusCode.BadRequest)
                throw new PaymentApiException("PayPal rejected the request but returned an unreadable error response.", status.Value, innerException: ex);
            throw InvalidSuccess("PayPal returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentApiException("PayPal is currently unreachable or timed out.", HttpStatusCode.BadGateway, innerException: ex);
        }
    }

    private CaptureResult ToCaptureResult(CapturedPayment capture)
    {
        if (string.IsNullOrWhiteSpace(capture.Id) || capture.Amount is null)
            throw InvalidSuccess("PayPal did not return a complete capture response.");

        var gross = capture.SellerReceivableBreakdown?.GrossAmount is { } breakdownGross
            ? ParseMoney(breakdownGross.Value, "capture gross amount")
            : ParseMoney(capture.Amount.Value, "capture amount");
        var fee = ParseOptionalMoney(capture.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = ParseOptionalMoney(capture.SellerReceivableBreakdown?.NetAmount?.Value);
        return new CaptureResult(
            capture.Id,
            capture.Status?.Value ?? string.Empty,
            gross,
            capture.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode ?? capture.Amount.CurrencyCode,
            fee,
            net,
            ParseOptionalTimestamp(capture.CreateTime, "capture creation time"));
    }

    private RefundProviderResult ToRefundResult(Refund refund, decimal fallbackAmount)
    {
        if (string.IsNullOrWhiteSpace(refund.Id))
            throw InvalidSuccess("PayPal did not return a refund identifier.");
        var amount = refund.Amount is null ? fallbackAmount : ParseMoney(refund.Amount.Value, "refund amount");
        var currency = refund.Amount?.CurrencyCode ?? _options.Currency;
        return new RefundProviderResult(
            refund.Id,
            refund.Status?.Value ?? string.Empty,
            amount,
            currency,
            ParseOptionalTimestamp(refund.UpdateTime ?? refund.CreateTime, "refund update time"));
    }

    private static CardRequest ToDirectCard(CardInput card) => new()
    {
        Name = card.Name.Trim(),
        Number = new string(card.Number.Where(static character => !char.IsWhiteSpace(character) && character != '-').ToArray()),
        Expiry = card.Expiry.Trim(),
        SecurityCode = card.SecurityCode.Trim(),
        BillingAddress = ToAddress(card.BillingAddress)
    };

    private static Address ToAddress(BillingAddressInput address) => new()
    {
        AddressLine1 = NullIfWhiteSpace(address.AddressLine1),
        AddressLine2 = NullIfWhiteSpace(address.AddressLine2),
        AdminArea2 = NullIfWhiteSpace(address.City),
        AdminArea1 = NullIfWhiteSpace(address.State),
        PostalCode = NullIfWhiteSpace(address.PostalCode),
        CountryCode = address.CountryCode.Trim().ToUpperInvariant()
    };

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateInvoiceId(int localOrderId, string createRequestId)
    {
        if (string.IsNullOrWhiteSpace(createRequestId))
            throw new InvalidOperationException("A persisted PayPal create request identifier is required.");

        var requestHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(createRequestId.Trim())));
        return $"eshop-{localOrderId.ToString(CultureInfo.InvariantCulture)}-{requestHash}";
    }

    private static decimal ParseMoney(string value, string field)
    {
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            throw InvalidSuccess($"PayPal returned an invalid {field}.");
        return amount;
    }

    private static decimal? ParseOptionalMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static DateTimeOffset? ParseOptionalTimestamp(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return timestamp;
        throw InvalidSuccess($"PayPal returned an invalid {field}.");
    }

    private static int ParsePageCount(double? value, int fallback)
    {
        if (value is null)
            return fallback;
        if (value < 0 || value > int.MaxValue || value != Math.Truncate(value.Value))
            throw InvalidSuccess("PayPal returned an invalid transaction report page count.");
        return (int)value.Value;
    }

    private static DateTimeOffset TruncateToUtcSecond(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            utc.Minute,
            utc.Second,
            TimeSpan.Zero);
    }

    private static string FormatReportingTimestamp(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private PaymentApiException Translate(CreateOrderError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(GetAuthorizedPaymentError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(ReauthorizePaymentError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(CaptureAuthorizedPaymentError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(GetCapturedPaymentError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(VoidPaymentError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(RefundCapturedPaymentError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException Translate(GetRefundError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromProviderError(providerError, fallback);
        if (error.TryGetNoContent(out var noContent))
            return TranslateRaw(noContent, fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException TranslateVault(CreateSetupTokenError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromVaultError(
                providerError.Name,
                providerError.Details?.Select(static detail => (detail.Issue, detail.Description)),
                providerError.DebugId,
                fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException TranslateVault(CreatePaymentTokenError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromVaultError(
                providerError.Name,
                providerError.Details?.Select(static detail => (detail.Issue, detail.Description)),
                providerError.DebugId,
                fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException TranslateVault(DeletePaymentTokenError error, string fallback)
    {
        if (error.TryGetError(out var providerError))
            return FromVaultError(
                providerError.Name,
                providerError.Details?.Select(static detail => (detail.Issue, detail.Description)),
                providerError.DebugId,
                fallback);
        if (error.TryGetRawError(out var raw))
            return TranslateRaw(raw, fallback);
        return UnknownProviderError(fallback);
    }

    private PaymentApiException FromProviderError(PayPalServerSdk.Models.Error error, string fallback)
    {
        var challenge = string.Equals(error.Name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            error.Details?.Any(static d => string.Equals(d.Issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)) == true;
        var message = BuildProviderMessage(
            challenge ? "PayPal requires browser approval for this card payment; this headless flow has stopped." : fallback,
            error.Name,
            error.Details?.Select(static detail => (detail.Issue, detail.Description)));
        return new PaymentApiException(message, StatusOr(HttpStatusCode.UnprocessableEntity), error.DebugId, challenge);
    }

    private PaymentApiException FromVaultError(
        string name,
        IEnumerable<(string Issue, string? Description)>? details,
        string debugId,
        string fallback)
    {
        var challenge = string.Equals(name, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
            details?.Any(static detail => string.Equals(detail.Issue, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase)) == true;
        var message = BuildProviderMessage(
            challenge ? "PayPal requires browser approval to save this card; this headless flow has stopped." : fallback,
            name,
            details);
        return new PaymentApiException(message, StatusOr(HttpStatusCode.UnprocessableEntity), debugId, challenge);
    }

    private static string BuildProviderMessage(
        string fallback,
        string name,
        IEnumerable<(string Issue, string? Description)>? details)
    {
        var safeName = SafeProviderText(name);
        var safeDetails = details?
            .Select(static detail =>
            {
                var issue = SafeProviderText(detail.Issue);
                var description = SafeProviderText(detail.Description);
                return string.IsNullOrWhiteSpace(description) ? issue : $"{issue}: {description}";
            })
            .Where(static detail => !string.IsNullOrWhiteSpace(detail))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray() ?? [];

        var diagnostic = safeDetails.Length == 0
            ? safeName
            : $"{safeName}; {string.Join("; ", safeDetails)}";
        return string.IsNullOrWhiteSpace(diagnostic)
            ? fallback
            : $"{fallback} PayPal error: {diagnostic}.";
    }

    private static string SafeProviderText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var sanitized = new string(value.Where(static character => !char.IsControl(character)).ToArray()).Trim();
        return sanitized.Length <= 512 ? sanitized : sanitized[..512];
    }

    private PaymentApiException TranslateRaw(RawError error, string fallback) =>
        new(fallback, error.StatusCode);

    private PaymentApiException TranslateTransactionSearchRaw(RawError error, string fallback)
    {
        var responseBody = error.ReadAsString();
        var diagnosticParts = new List<string>();
        string? providerDebugId = null;

        if (!string.IsNullOrWhiteSpace(responseBody) && responseBody.Length <= 64 * 1024)
        {
            try
            {
                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    AddSafeDiagnostic(diagnosticParts, "name", GetJsonString(root, "name"));
                    AddSafeDiagnostic(diagnosticParts, "message", GetJsonString(root, "message"));
                    providerDebugId = SafeProviderText(GetJsonString(root, "debug_id"));

                    if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var detail in details.EnumerateArray().Take(5))
                        {
                            if (detail.ValueKind != JsonValueKind.Object)
                                continue;
                            AddSafeDiagnostic(diagnosticParts, "issue", GetJsonString(detail, "issue"));
                            AddSafeDiagnostic(diagnosticParts, "description", GetJsonString(detail, "description"));
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // RawError is intentionally untyped. Fall back to a bounded, sanitized body below.
            }
        }

        if (diagnosticParts.Count == 0)
            AddSafeDiagnostic(diagnosticParts, "response", SafeProviderText(responseBody));

        var diagnostic = string.Join("; ", diagnosticParts.Distinct(StringComparer.Ordinal));
        var message = string.IsNullOrWhiteSpace(diagnostic)
            ? fallback
            : $"{fallback} PayPal error: {diagnostic}.";
        if (message.Length > 2048)
            message = message[..2048];

        return new PaymentApiException(
            message,
            error.StatusCode,
            string.IsNullOrWhiteSpace(providerDebugId) ? null : providerDebugId);
    }

    private static string? GetJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static void AddSafeDiagnostic(ICollection<string> diagnostics, string label, string? value)
    {
        var safeValue = SafeProviderText(value);
        if (!string.IsNullOrWhiteSpace(safeValue))
            diagnostics.Add($"{label}={safeValue}");
    }

    private PaymentApiException UnknownProviderError(string fallback) =>
        new(fallback, StatusOr(HttpStatusCode.BadGateway));

    private HttpStatusCode StatusOr(HttpStatusCode fallback) => _statusContext.LastStatus ?? fallback;

    private static PaymentApiException InvalidSuccess(string message, Exception? inner = null) =>
        new(message, HttpStatusCode.BadGateway, innerException: inner);

    private static PaymentApiException ChallengeRequired() =>
        new(
            "PayPal requires browser approval for this card operation; this headless flow has stopped.",
            HttpStatusCode.Conflict,
            payerActionRequired: true);
}
