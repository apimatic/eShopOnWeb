using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPal;
using PayPal.Core.ErrorResponse;
using PayPal.Core.Exceptions;
using PayPal.Errors;
using PayPal.Models;
using PayPal.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private const string PreferRepresentation = "return=representation";
    private const int MaxSearchPages = 50;
    private static readonly TimeSpan SearchWindow = TimeSpan.FromDays(30);

    private readonly PayPalClient _client;
    private readonly ILogger<PayPalPaymentGateway> _logger;

    public PayPalPaymentGateway(PayPalClient client, ILogger<PayPalPaymentGateway> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        CardPaymentDetails card,
        string requestId,
        CancellationToken ct)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                Number = NormalizePan(card.Number),
                Expiry = NormalizeExpiry(card.Expiry),
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = ToPayPalAddress(card.BillingAddress)
            }
        };
        return await AuthorizeAsync(orderId, amount, currency, paymentSource, requestId, ct);
    }

    public async Task<AuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string vaultId,
        string requestId,
        CancellationToken ct)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                VaultId = vaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Customer,
                    PaymentType = StoredPaymentSourcePaymentType.OneTime,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            }
        };
        return await AuthorizeAsync(orderId, amount, currency, paymentSource, requestId, ct);
    }

    public async Task<AuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
            return ToSnapshot(auth);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapGetAuthorizedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal get-authorization failed.", ex);
        }
    }

    public async Task<AuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            var auth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = FormatMoney(amount)
                    }
                },
                prefer: PreferRepresentation,
                ct: ct);
            _logger.LogInformation("PayPal reauthorized {AuthorizationId} -> {NewId}", authorizationId, auth.Id);
            return ToSnapshot(auth);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapReauthorizePayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal reauthorize failed. The hold may no longer be renewable.", ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string requestId,
        string? invoiceId,
        CancellationToken ct)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    FinalCapture = true,
                    InvoiceId = invoiceId
                },
                prefer: PreferRepresentation,
                ct: ct);

            if (capture.SellerReceivableBreakdown is null && !string.IsNullOrEmpty(capture.Id))
            {
                capture = await _client.Payments.GetCapturedPayment(
                    captureId: capture.Id,
                    payPalMockResponse: null,
                    ct: ct);
            }

            _logger.LogInformation("PayPal captured {CaptureId} status {Status}", capture.Id, capture.Status?.Value);
            return ToCaptureResult(capture);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapCaptureAuthorizedPayment(ex);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw MapGetCapturedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal capture failed. Capture outcome is unknown until reconciled.", ex);
        }
    }

    public async Task VoidAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            var result = await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: PreferRepresentation,
                ct: ct);
            _logger.LogInformation("PayPal voided {AuthorizationId} status {Status}", authorizationId, result.Status?.Value);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw MapVoidPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal void failed. Void outcome is unknown until reconciled.", ex);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            RefundRequest? body = null;
            if (amount.HasValue)
            {
                body = new RefundRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = FormatMoney(amount.Value)
                    }
                };
            }

            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: body,
                prefer: PreferRepresentation,
                ct: ct);

            if (string.IsNullOrEmpty(refund.Id))
                throw new PaymentException("PayPal refund succeeded but returned no refund id.", HttpStatusCode.BadGateway);

            _logger.LogInformation("PayPal refund {RefundId} status {Status}", refund.Id, refund.Status?.Value);
            return new RefundResult(
                refund.Id,
                refund.Status?.Value ?? string.Empty,
                ParseMoney(refund.Amount?.Value) is decimal parsed && parsed > 0 ? parsed : amount ?? 0m);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapRefundCapturedPayment(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal refund failed. Refund outcome is unknown until reconciled.", ex);
        }
    }

    public async Task<VaultedCard> VaultCardAsync(
        string merchantCustomerId,
        CardPaymentDetails card,
        string? requestId,
        CancellationToken ct)
    {
        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: requestId ?? Guid.NewGuid().ToString("N"),
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = SanitizeCustomerId(merchantCustomerId) },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = NormalizePan(card.Number),
                            Expiry = NormalizeExpiry(card.Expiry),
                            SecurityCode = card.SecurityCode,
                            Name = card.Name,
                            BillingAddress = ToPayPalAddress(card.BillingAddress)
                        }
                    }
                },
                ct: ct);

            if (string.IsNullOrEmpty(response.Id))
                throw new PaymentException("PayPal vaulted the card but returned no payment token id.", HttpStatusCode.BadGateway);

            var cardEntity = response.PaymentSource?.Card;
            _logger.LogInformation("PayPal vaulted payment token {VaultId}", response.Id);
            return new VaultedCard(
                response.Id,
                response.Customer?.Id,
                cardEntity?.LastDigits ?? string.Empty,
                cardEntity?.Brand?.Value,
                cardEntity?.Expiry,
                cardEntity?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw MapCreatePaymentToken(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal vault failed.", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
            _logger.LogInformation("PayPal deleted payment token {VaultId}", vaultId);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw MapDeletePaymentToken(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal delete payment token failed.", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var results = new List<ProviderTransaction>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + SearchWindow;
            if (windowEnd > to)
                windowEnd = to;

            await SearchWindowAsync(windowStart, windowEnd, results, ct);
            windowStart = windowEnd;
        }

        return results;
    }

    private async Task<AuthorizationResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        string currency,
        PaymentSource paymentSource,
        string requestId,
        CancellationToken ct)
    {
        PayPal.Models.Order created;
        try
        {
            created = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
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
                            InvoiceId = $"eShop-{orderId}-{Guid.NewGuid():N}",
                            CustomId = orderId.ToString(),
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = currency,
                                Value = FormatMoney(amount)
                            }
                        }
                    ],
                    PaymentSource = paymentSource
                },
                prefer: PreferRepresentation,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrder(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal create-order failed. Authorization outcome is unknown until reconciled.", ex);
        }

        EnsureNoPayerAction(created.Status, created.Links);
        var auth = FirstAuthorization(created);
        if (auth is null && !string.IsNullOrEmpty(created.Id))
        {
            auth = await AuthorizeExistingOrder(created.Id, requestId + ":authorize", ct);
        }

        if (auth is null)
        {
            throw new PaymentException(
                "PayPal did not return an authorization for the order. The hold was not placed.",
                HttpStatusCode.BadGateway);
        }

        _logger.LogInformation(
            "PayPal authorized order {PayPalOrderId} authorization {AuthorizationId} status {Status}",
            created.Id, auth.Id, auth.Status?.Value);

        return new AuthorizationResult(
            created.Id ?? string.Empty,
            created.Status?.Value ?? string.Empty,
            auth.Id ?? string.Empty,
            auth.Status?.Value ?? string.Empty,
            ParseTime(auth.ExpirationTime),
            ParseTime(auth.CreateTime),
            ParseMoney(auth.Amount?.Value) is decimal a && a > 0 ? a : amount);
    }

    private async Task<AuthorizationWithAdditionalData?> AuthorizeExistingOrder(
        string payPalOrderId,
        string requestId,
        CancellationToken ct)
    {
        try
        {
            var authorized = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: PreferRepresentation,
                ct: ct);
            EnsureNoPayerAction(authorized.Status, authorized.Links);
            return FirstAuthorization(authorized);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapAuthorizeOrder(ex);
        }
        catch (Exception ex) when (IsBoundary(ex))
        {
            throw WrapBoundary("PayPal authorize-order failed. Authorization outcome is unknown until reconciled.", ex);
        }
    }

    private async Task SearchWindowAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<ProviderTransaction> sink,
        CancellationToken ct)
    {
        var start = FormatSearchTime(from);
        var end = FormatSearchTime(to);
        int page = 1;
        int pages = 0;
        int? totalPages = null;

        do
        {
            SearchResponse pageResponse;
            try
            {
                pageResponse = await _client.TransactionSearch.SearchTransactions(
                    startDate: start,
                    endDate: end,
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
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw MapRaw("PayPal transaction search failed.", ex.Error);
            }
            catch (Exception ex) when (IsBoundary(ex))
            {
                throw WrapBoundary("PayPal transaction search failed.", ex);
            }

            pages++;
            totalPages = pageResponse.TotalPages;
            var details = pageResponse.TransactionDetails;
            if (details is not null)
            {
                foreach (var detail in details)
                {
                    var info = detail.TransactionInfo;
                    if (info is null) continue;
                    sink.Add(new ProviderTransaction(
                        info.TransactionId ?? string.Empty,
                        info.PaypalReferenceId,
                        info.InvoiceId,
                        info.CustomField,
                        info.TransactionAmount?.Value,
                        info.FeeAmount?.Value,
                        info.TransactionAmount?.CurrencyCode,
                        info.TransactionStatus,
                        info.TransactionInitiationDate));
                }
            }

            var count = details?.Count ?? 0;
            if (totalPages.HasValue && totalPages.Value > 0)
            {
                page++;
                if (page > totalPages.Value) break;
            }
            else if (count < 100)
            {
                break;
            }
            else
            {
                page++;
            }
        } while (pages < MaxSearchPages);

        if (pages >= MaxSearchPages)
        {
            _logger.LogWarning("PayPal transaction search stopped after {MaxPages} pages for window {From}–{To}", MaxSearchPages, start, end);
        }
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(PayPal.Models.Order order) =>
        order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static AuthorizationWithAdditionalData? FirstAuthorization(OrderAuthorizeResponse order) =>
        order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();

    private static void EnsureNoPayerAction(OrderStatus? status, IReadOnlyList<LinkDescription>? links)
    {
        if (status == OrderStatus.PayerActionRequired)
            throw new PayerActionRequiredException();
        if (links is not null && links.Any(l => string.Equals(l.Rel, "payer-action", StringComparison.OrdinalIgnoreCase)))
            throw new PayerActionRequiredException();
    }

    private static AuthorizationSnapshot ToSnapshot(PaymentAuthorization auth) =>
        new(auth.Id ?? string.Empty,
            auth.Status?.Value ?? string.Empty,
            ParseTime(auth.ExpirationTime),
            ParseTime(auth.CreateTime));

    private static CaptureResult ToCaptureResult(CapturedPayment capture)
    {
        if (string.IsNullOrEmpty(capture.Id))
            throw new PaymentException("PayPal capture succeeded but returned no capture id.", HttpStatusCode.BadGateway);

        var breakdown = capture.SellerReceivableBreakdown;
        return new CaptureResult(
            capture.Id,
            capture.Status?.Value ?? string.Empty,
            ParseMoney(capture.Amount?.Value) is decimal captured ? captured : 0m,
            ParseMoney(breakdown?.PaypalFee?.Value),
            ParseMoney(breakdown?.NetAmount?.Value));
    }

    private static Address? ToPayPalAddress(CardBillingAddress? address)
    {
        if (address is null)
            return new Address { CountryCode = "US" };
        return new Address
        {
            CountryCode = string.IsNullOrWhiteSpace(address.CountryCode) ? "US" : address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode
        };
    }

    private static string NormalizePan(string number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = (expiry ?? string.Empty).Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
            return trimmed;
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        if (digits.Length == 4)
            return $"20{digits[2]}{digits[3]}-{digits[0]}{digits[1]}";
        if (digits.Length == 6)
            return $"{digits[2]}{digits[3]}{digits[4]}{digits[5]}-{digits[0]}{digits[1]}";
        throw new PaymentException("Card expiry must be YYYY-MM or MM/YY.", HttpStatusCode.BadRequest);
    }

    private static string SanitizeCustomerId(string buyerId)
    {
        var cleaned = new string((buyerId ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || "-_.^*$@#".Contains(c)).ToArray());
        if (cleaned.Length > 64)
            cleaned = cleaned[..64];
        return string.IsNullOrEmpty(cleaned) ? "buyer" : cleaned;
    }

    private static string FormatMoney(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return parsed;
        return null;
    }

    private static string FormatSearchTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static bool IsBoundary(Exception ex) =>
        ex is JsonException or HttpRequestException or TaskCanceledException;

    private PaymentException WrapBoundary(string message, Exception ex)
    {
        _logger.LogError(ex, "{Message}", message);
        return new PaymentException(message, ex, HttpStatusCode.BadGateway);
    }

    private PaymentException MapCreateOrder(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal create-order failed.", raw);
        return new PaymentException("PayPal create-order failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapAuthorizeOrder(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal authorize-order failed.", raw);
        return new PaymentException("PayPal authorize-order failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapGetAuthorizedPayment(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetNoContent(out var noContent)) return MapRaw("PayPal get-authorization failed.", noContent);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal get-authorization failed.", raw);
        return new PaymentException("PayPal get-authorization failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapReauthorizePayment(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetNoContent(out var noContent)) return MapRaw("PayPal reauthorize failed.", noContent);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal reauthorize failed.", raw);
        return new PaymentException("PayPal reauthorize failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapCaptureAuthorizedPayment(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetNoContent(out var noContent)) return MapRaw("PayPal capture failed.", noContent);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal capture failed.", raw);
        return new PaymentException("PayPal capture failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapGetCapturedPayment(SdkException<GetCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetNoContent(out var noContent)) return MapRaw("PayPal get-capture failed.", noContent);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal get-capture failed.", raw);
        return new PaymentException("PayPal get-capture failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapVoidPayment(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetNoContent(out var noContent)) return MapRaw("PayPal void failed.", noContent);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal void failed.", raw);
        return new PaymentException("PayPal void failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapRefundCapturedPayment(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetNoContent(out var noContent)) return MapRaw("PayPal refund failed.", noContent);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal refund failed.", raw);
        return new PaymentException("PayPal refund failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapCreatePaymentToken(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal vault failed.", raw);
        return new PaymentException("PayPal vault failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapDeletePaymentToken(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError(out var error)) return MapError(error);
        if (ex.Error.TryGetRawError(out var raw)) return MapRaw("PayPal delete payment token failed.", raw);
        return new PaymentException("PayPal delete payment token failed.", HttpStatusCode.BadGateway);
    }

    private PaymentException MapError(Error error)
    {
        _logger.LogWarning("PayPal error {Name}: {Message} debug {DebugId}", error.Name, error.Message, error.DebugId);
        var detail = error.Details is { Count: > 0 }
            ? string.Join("; ", error.Details.Select(d => d.Description ?? d.Issue))
            : error.Message;
        var status = error.Name switch
        {
            "AUTHENTICATION_FAILURE" or "NOT_AUTHORIZED" => HttpStatusCode.BadGateway,
            "RESOURCE_NOT_FOUND" => HttpStatusCode.NotFound,
            "UNPROCESSABLE_ENTITY" or "INVALID_REQUEST" => HttpStatusCode.BadRequest,
            "RESOURCE_CONFLICT" => HttpStatusCode.Conflict,
            _ => HttpStatusCode.BadRequest
        };
        return new PaymentException(detail, status, error.DebugId);
    }

    private PaymentException MapRaw(string message, RawError raw)
    {
        var body = raw.ReadAsString();
        _logger.LogWarning("PayPal HTTP {Status}: {Body}", (int)raw.StatusCode, Truncate(body));
        var status = (int)raw.StatusCode is 401 or 403 or >= 500
            ? HttpStatusCode.BadGateway
            : raw.StatusCode == HttpStatusCode.NotFound
                ? HttpStatusCode.NotFound
                : HttpStatusCode.BadRequest;
        return new PaymentException($"{message} HTTP {(int)raw.StatusCode}.", status);
    }

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= 500 ? value : value[..500];
}
