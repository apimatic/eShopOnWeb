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
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// The only class that touches the PayPal SDK. Translates the domain-level gateway contract into
/// PayPal Server SDK calls and maps SDK failures onto the app's own <see cref="PaymentGatewayException"/>.
/// </summary>
public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const int MaxReconciliationPages = 100;

    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;
    private readonly IAppLogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalSettings> settings,
        IAppLogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _currency = settings.Value.Currency!; // validated non-empty at startup
        _logger = logger;
    }

    public string CurrencyCode => _currency;

    public async Task<PayPalAuthorizationResult> AuthorizeAsync(AuthorizeCardRequest request, CancellationToken ct)
    {
        var paymentSource = new PaymentSource
        {
            Card = request.VaultId is not null
                ? new CardRequest { VaultId = request.VaultId }
                : BuildCardRequest(request.Card!)
        };

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new[]
            {
                new PurchaseUnitRequest
                {
                    Amount = AmountWithBreakdownOf(request.Amount),
                    // custom_id carries the eShop order reference for reconciliation (no uniqueness
                    // constraint). invoice_id must be globally unique for the merchant account, so we
                    // make it order-ref + GUID — a small integer order id collides on a shared account
                    // and triggers DUPLICATE_INVOICE_ID / duplicate-transaction detection.
                    CustomId = request.OrderReference,
                    InvoiceId = $"eshop-{request.OrderReference}-{Guid.NewGuid():N}",
                    Description = $"eShopOnWeb order {request.OrderReference}"
                }
            },
            PaymentSource = paymentSource
        };

        try
        {
            var created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: request.IdempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);

            ThrowIfPayerActionRequired(created.Status);
            var orderId = created.Id ?? throw new PaymentGatewayException("PayPal did not return an order id.");

            // Some flows already carry the authorization on the created order; otherwise authorize now.
            var authorization = FindAuthorization(created.PurchaseUnits);
            var cardInfo = created.PaymentSource;

            if (authorization is null)
            {
                var authorized = await _client.Orders.AuthorizeOrder(
                    id: orderId,
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);

                ThrowIfPayerActionRequired(authorized.Status);
                authorization = FindAuthorization(authorized.PurchaseUnits);
            }

            if (authorization?.Id is null)
                throw new PaymentGatewayException("PayPal accepted the order but returned no authorization to hold.");

            var last4 = request.Card?.Number is { Length: >= 4 } n ? n[^4..] : null;
            return new PayPalAuthorizationResult(
                PayPalOrderId: orderId,
                AuthorizationId: authorization.Id,
                Status: authorization.Status?.Value,
                ExpiresAt: ParseDate(authorization.ExpirationTime),
                CardBrand: null,
                CardLast4: last4);
        }
        catch (SdkException<CreateOrderError> ex) { throw FromCreateOrder(ex); }
        catch (SdkException<AuthorizeOrderError> ex) { throw FromAuthorizeOrder(ex); }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            FinalCapture = true,
            Amount = amount is decimal a ? MoneyOf(a) : null
        };

        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            var gross = ParseAmount(breakdown?.GrossAmount) ?? ParseAmount(capture.Amount) ?? amount ?? 0m;
            return new PayPalCaptureResult(
                CaptureId: capture.Id ?? throw new PaymentGatewayException("PayPal returned a capture with no id."),
                Status: capture.Status?.Value,
                GrossAmount: gross,
                PayPalFee: ParseAmount(breakdown?.PaypalFee),
                NetAmount: ParseAmount(breakdown?.NetAmount),
                CurrencyCode: breakdown?.GrossAmount?.CurrencyCode ?? capture.Amount?.CurrencyCode ?? _currency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetNoContent(out RawError nc)) throw FromRaw(nc);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized capture error.", ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<PayPalReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = MoneyOf(amount) },
                prefer: "return=representation",
                ct: ct);

            return new PayPalReauthorizeResult(
                AuthorizationId: reauth.Id ?? authorizationId,
                Status: reauth.Status?.Value,
                ExpiresAt: ParseDate(reauth.ExpirationTime));
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetNoContent(out RawError nc)) throw FromRaw(nc);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized re-authorization error.", ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetNoContent(out RawError nc)) throw FromRaw(nc);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized void error.", ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException) when (PayPalResponseContext.LastStatusCode is null or (>= 200 and < 300))
        {
            // A successful void can return 204 No Content; the SDK throws deserializing the empty
            // body. An error would have surfaced as SdkException<VoidPaymentError>, so reaching a
            // JsonException on a 2xx (or unrecorded) response means the void succeeded.
        }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, string? noteToPayer, string? customId, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = amount is decimal a ? MoneyOf(a) : null,
            NoteToPayer = noteToPayer,
            CustomId = customId
        };

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new PayPalRefundResult(
                RefundId: refund.Id ?? throw new PaymentGatewayException("PayPal returned a refund with no id."),
                Status: refund.Status?.Value,
                Amount: ParseAmount(refund.Amount) ?? amount ?? 0m,
                CurrencyCode: refund.Amount?.CurrencyCode ?? _currency);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetNoContent(out RawError nc)) throw FromRaw(nc);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized refund error.", ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<PayPalVaultedCardResult> VaultCardAsync(VaultCardRequest request, CancellationToken ct)
    {
        try
        {
            // Step 1: create a setup token from the raw card.
            var setup = await _client.Vault.CreateSetupToken(
                payPalRequestId: $"{request.IdempotencyKey}-setup",
                body: new SetupTokenRequest
                {
                    Customer = request.PayPalCustomerId is not null ? new Customer { Id = request.PayPalCustomerId } : null,
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Number = request.Card.Number,
                            Expiry = request.Card.ExpiryYearMonth,
                            SecurityCode = request.Card.SecurityCode,
                            Name = request.Card.CardholderName,
                            BillingAddress = ToSdkAddress(request.Card.BillingAddress)
                        }
                    }
                },
                ct: ct);

            var setupId = setup.Id ?? throw new PaymentGatewayException("PayPal returned no setup token id.");

            // Step 2: exchange the setup token for a permanent payment (vault) token.
            var token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"{request.IdempotencyKey}-token",
                body: new PaymentTokenRequest
                {
                    Customer = request.PayPalCustomerId is not null ? new Customer { Id = request.PayPalCustomerId } : null,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setupId,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                ct: ct);

            var card = token.PaymentSource?.Card;
            return new PayPalVaultedCardResult(
                VaultId: token.Id ?? throw new PaymentGatewayException("PayPal returned no vault token id."),
                CustomerId: token.Customer?.Id ?? request.PayPalCustomerId,
                Brand: card?.Brand?.Value,
                Last4: card?.LastDigits ?? (request.Card.Number.Length >= 4 ? request.Card.Number[^4..] : null),
                Expiry: card?.Expiry ?? request.Card.ExpiryYearMonth,
                CardholderName: card?.Name ?? request.Card.CardholderName);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized setup-token error.", ex);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized payment-token error.", ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError(out var err)) throw FromError(err);
            if (ex.Error.TryGetRawError(out RawError raw)) throw FromRaw(raw);
            throw new PaymentGatewayException("PayPal returned an unrecognized delete-token error.", ex);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var startDate = FormatSearchDate(from);
        var endDate = FormatSearchDate(to);
        var records = new List<PayPalTransactionRecord>();

        try
        {
            var page = 1;
            var totalPages = 1;
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
                foreach (var detail in response.TransactionDetails ?? Enumerable.Empty<TransactionDetails>())
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    records.Add(new PayPalTransactionRecord(
                        TransactionId: info.TransactionId,
                        InvoiceId: info.InvoiceId,
                        CustomField: info.CustomField,
                        Amount: ParseAmount(info.TransactionAmount),
                        Fee: ParseAmount(info.FeeAmount),
                        CurrencyCode: info.TransactionAmount?.CurrencyCode,
                        Status: info.TransactionStatus,
                        Date: ParseDate(info.TransactionInitiationDate),
                        EventCode: info.TransactionEventCode));
                }

                page++;
            }
            while (page <= totalPages && page <= MaxReconciliationPages);

            if (totalPages > MaxReconciliationPages)
                _logger.LogWarning($"Reconciliation truncated at {MaxReconciliationPages} pages (PayPal reported {totalPages}).");

            return records;
        }
        catch (SdkException<RawError> ex) // TransactionSearch is Case B
        {
            throw FromRaw(ex.Error);
        }
        catch (Exception ex) when (IsTransport(ex)) { throw Unreachable(ex); }
        catch (JsonException ex) { throw Unprocessable(ex); }
    }

    // --- helpers ---------------------------------------------------------

    private CardRequest BuildCardRequest(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.ExpiryYearMonth,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = ToSdkAddress(card.BillingAddress)
    };

    private static Address? ToSdkAddress(CardBillingAddress? a) => a is null ? null : new Address
    {
        CountryCode = a.CountryCode,
        AddressLine1 = a.AddressLine1,
        AddressLine2 = a.AddressLine2,
        AdminArea1 = a.AdminArea1,
        AdminArea2 = a.AdminArea2,
        PostalCode = a.PostalCode
    };

    private Money MoneyOf(decimal amount) => new()
    {
        CurrencyCode = _currency,
        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private AmountWithBreakdown AmountWithBreakdownOf(decimal amount) => new()
    {
        CurrencyCode = _currency,
        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
    };

    private static AuthorizationWithAdditionalData? FindAuthorization(IReadOnlyList<PurchaseUnit>? purchaseUnits) =>
        purchaseUnits?
            .Select(pu => pu.Payments?.Authorizations)
            .FirstOrDefault(a => a is { Count: > 0 })?[0];

    private static void ThrowIfPayerActionRequired(OrderStatus? status)
    {
        if (status is not null && status == OrderStatus.PayerActionRequired)
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (e.g. 3-D Secure). " +
                "This unbranded card integration does not support an approval round-trip.");
    }

    private static decimal? ParseAmount(Money? money) =>
        money is not null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static bool IsTransport(Exception ex) => ex is HttpRequestException or TaskCanceledException or OperationCanceledException;

    private PaymentGatewayException FromError(Error err)
    {
        var expired = err.Details?.Any(d => d.Issue.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)) == true;
        var issues = err.Details is { Count: > 0 }
            ? " (" + string.Join("; ", err.Details.Select(d =>
                string.IsNullOrEmpty(d.Description) ? d.Issue : $"{d.Issue}: {d.Description}")) + ")"
            : string.Empty;
        var debug = string.IsNullOrEmpty(err.DebugId) ? string.Empty : $" [debug_id={err.DebugId}]";
        _logger.LogWarning($"PayPal error {err.Name}{issues}{debug}");
        return new PaymentGatewayException(
            message: $"{err.Name}: {err.Message}{issues}{debug}",
            statusCode: PayPalResponseContext.LastStatusCode,
            providerName: err.Name,
            debugId: err.DebugId,
            authorizationExpired: expired);
    }

    private static PaymentGatewayException FromRaw(RawError raw) =>
        new($"PayPal returned HTTP {(int)raw.StatusCode}.", statusCode: (int)raw.StatusCode);

    private PaymentGatewayException FromCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var err)) return FromError(err);
        if (ex.Error.TryGetRawError(out RawError raw)) return FromRaw(raw);
        return new PaymentGatewayException("PayPal returned an unrecognized create-order error.", ex);
    }

    private PaymentGatewayException FromAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var err)) return FromError(err);
        if (ex.Error.TryGetRawError(out RawError raw)) return FromRaw(raw);
        return new PaymentGatewayException("PayPal returned an unrecognized authorize-order error.", ex);
    }

    private static PaymentGatewayException Unreachable(Exception ex) =>
        new("PayPal is currently unreachable.", ex);

    private static PaymentGatewayException Unprocessable(Exception ex) =>
        new("PayPal returned a response that could not be processed.", ex);
}
