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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using SdkAddress = PayPalServerSdk.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalPaymentGateway : IPayPalPaymentGateway
{
    private const string BadResponseMessage = "PayPal returned a response that could not be processed.";
    private const string UnreachableMessage = "PayPal was unreachable.";

    private readonly PayPalServerSdkClient _client;
    private readonly PayPalOptions _options;

    public PayPalPaymentGateway(PayPalServerSdkClient client, IOptions<PayPalOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public Task<PayPalAuthorizationResult> AuthorizeWithCardAsync(PayPalCardDetails card, decimal amount, string requestId, CancellationToken ct = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                SecurityCode = card.SecurityCode,
                BillingAddress = MapAddress(card.BillingAddress)
            }
        };
        return CreateOrderInternalAsync(paymentSource, amount, requestId, ct);
    }

    public Task<PayPalAuthorizationResult> AuthorizeWithVaultedCardAsync(string vaultId, decimal amount, string requestId, CancellationToken ct = default)
    {
        var paymentSource = new PaymentSource
        {
            Card = new CardRequest { VaultId = vaultId }
        };
        return CreateOrderInternalAsync(paymentSource, amount, requestId, ct);
    }

    public async Task<PayPalAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.GetAuthorizedPayment(authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, "Get authorization");
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, "Get authorization");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Get authorization");
            throw new PayPalGatewayException("Get authorization failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

        return MapAuthorization(auth);
    }

    public async Task<PayPalAuthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string requestId, CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = _options.Currency, Value = FormatAmount(amount) }
        };

        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.ReauthorizePayment(authorizationId, payPalRequestId: requestId, payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, "Reauthorize payment");
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, "Reauthorize payment");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Reauthorize payment");
            throw new PayPalGatewayException("Reauthorize payment failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

        return MapAuthorization(auth);
    }

    public async Task<PayPalCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(authorizationId, payPalMockResponse: null, payPalRequestId: requestId, payPalAuthAssertion: null, body: null, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, "Capture authorized payment");
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, "Capture authorized payment");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Capture authorized payment");
            throw new PayPalGatewayException("Capture authorized payment failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

        var breakdown = captured.SellerReceivableBreakdown;
        var gross = breakdown is not null ? ParseMoney(breakdown.GrossAmount) : ParseMoney(captured.Amount);
        var fee = ParseMoney(breakdown?.PaypalFee);
        var net = breakdown is not null ? ParseMoney(breakdown.NetAmount) : gross;
        var currency = captured.Amount?.CurrencyCode ?? breakdown?.GrossAmount?.CurrencyCode ?? _options.Currency;

        return new PayPalCaptureResult(captured.Id ?? string.Empty, captured.Status?.Value ?? "UNKNOWN", gross, fee, net, currency);
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(authorizationId, payPalMockResponse: null, payPalAuthAssertion: null, payPalRequestId: requestId, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, "Void authorization");
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, "Void authorization");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Void authorization");
            throw new PayPalGatewayException("Void authorization failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }
    }

    public async Task<PayPalRefundResult> RefundCaptureAsync(string captureId, decimal? amount, string requestId, CancellationToken ct = default)
    {
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = _options.Currency, Value = FormatAmount(amount.Value) } }
            : null;

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(captureId, payPalMockResponse: null, payPalRequestId: requestId, payPalAuthAssertion: null, body: body, prefer: "return=representation", ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, "Refund captured payment");
            if (ex.Error.TryGetNoContent(out var noContent)) throw FromRaw(noContent, "Refund captured payment");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Refund captured payment");
            throw new PayPalGatewayException("Refund captured payment failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

        return new PayPalRefundResult(refund.Id ?? string.Empty, refund.Status?.Value ?? "UNKNOWN", ParseMoney(refund.Amount), refund.Amount?.CurrencyCode ?? _options.Currency);
    }

    public async Task<PayPalVaultedCard> SaveCardAsync(PayPalCardDetails card, string customerId, string requestId, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = FormatExpiry(card.ExpiryYear, card.ExpiryMonth),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = MapAddress(card.BillingAddress)
                }
            }
        };

        PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(payPalRequestId: requestId, body: body, ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw FromError1(error, "Save payment method");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Save payment method");
            throw new PayPalGatewayException("Save payment method failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

        return MapVaultedCard(token);
    }

    public async Task<IReadOnlyList<PayPalVaultedCard>> ListSavedCardsAsync(string customerId, CancellationToken ct = default)
    {
        var results = new List<PayPalVaultedCard>();
        var page = 1;
        var totalPages = 1;

        do
        {
            CustomerVaultPaymentTokensResponse response;
            try
            {
                response = await _client.Vault.ListCustomerPaymentTokens(customerId, pageSize: 20, page: page, totalRequired: true, ct: ct);
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                if (ex.Error.TryGetError1(out var error)) throw FromError1(error, "List saved payment methods");
                if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "List saved payment methods");
                throw new PayPalGatewayException("List saved payment methods failed with an unrecognized error.", 502);
            }
            catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

            if (response.PaymentTokens is not null)
            {
                results.AddRange(response.PaymentTokens.Select(MapVaultedCard));
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        } while (page <= totalPages);

        return results;
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error)) throw FromError1(error, "Delete saved payment method");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Delete saved payment method");
            throw new PayPalGatewayException("Delete saved payment method failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<PayPalTransaction>();
        var page = 1;
        var totalPages = 1;
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
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
            }
            catch (SdkException<RawError> ex)
            {
                throw FromRaw(ex.Error, "Search transactions");
            }
            catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    if (info?.TransactionId is null)
                    {
                        continue;
                    }

                    results.Add(new PayPalTransaction(
                        info.TransactionId,
                        info.TransactionStatus,
                        ParseMoney(info.TransactionAmount),
                        info.TransactionAmount?.CurrencyCode ?? _options.Currency,
                        ParseDate(info.TransactionInitiationDate),
                        ParseDate(info.TransactionUpdatedDate)));
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        } while (page <= totalPages);

        return results;
    }

    private async Task<PayPalAuthorizationResult> CreateOrderInternalAsync(PaymentSource paymentSource, decimal amount, string requestId, CancellationToken ct)
    {
        var body = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = _options.Currency,
                        Value = FormatAmount(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };

        Order order;
        try
        {
            order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out var error)) throw FromError(error, "Authorize payment");
            if (ex.Error.TryGetRawError(out var raw)) throw FromRaw(raw, "Authorize payment");
            throw new PayPalGatewayException("Authorize payment failed with an unrecognized error.", 502);
        }
        catch (JsonException ex) { throw new PayPalGatewayException(BadResponseMessage, 502, inner: ex); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { throw new PayPalGatewayException(UnreachableMessage, 502, inner: ex); }

        return MapAuthorizationFromOrder(order);
    }

    private static PayPalAuthorizationResult MapAuthorizationFromOrder(Order order)
    {
        if (order.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentActionRequiredException(
                $"PayPal requires the shopper to approve this payment in a browser (order {order.Id}, status PAYER_ACTION_REQUIRED). This integration only supports direct server-side card payments and cannot proceed.");
        }

        var payerActionLink = order.Links?.FirstOrDefault(l => l.Rel is not null && l.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase));
        if (payerActionLink is not null)
        {
            throw new PaymentActionRequiredException(
                $"PayPal returned a payer-action link for order {order.Id}, indicating the shopper must approve this payment in a browser. This integration only supports direct server-side card payments and cannot proceed.");
        }

        var authorization = order.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization is null)
        {
            throw new PayPalGatewayException($"PayPal did not return an authorization for order {order.Id} (status {order.Status?.Value}).", 502);
        }

        return new PayPalAuthorizationResult(
            order.Id,
            authorization.Id ?? string.Empty,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseMoney(authorization.Amount),
            authorization.Amount?.CurrencyCode ?? string.Empty,
            ParseDate(authorization.ExpirationTime));
    }

    private static PayPalAuthorizationResult MapAuthorization(PaymentAuthorization auth) =>
        new(null, auth.Id ?? string.Empty, auth.Status?.Value ?? "UNKNOWN", ParseMoney(auth.Amount), auth.Amount?.CurrencyCode ?? string.Empty, ParseDate(auth.ExpirationTime));

    private static PayPalVaultedCard MapVaultedCard(PaymentTokenResponse token)
    {
        var card = token.PaymentSource?.Card;
        return new PayPalVaultedCard(token.Id ?? string.Empty, card?.Brand?.Value, card?.LastDigits, card?.Expiry, card?.Type?.Value);
    }

    private static decimal ParseMoney(Money? money) =>
        money is null ? 0m : decimal.Parse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseDate(string? value) =>
        !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;

    private static string FormatAmount(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private static string FormatExpiry(int year, int month) => $"{year:D4}-{month:D2}";

    private static SdkAddress? MapAddress(PayPalBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new SdkAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea1 = address.AdminArea1,
            AdminArea2 = address.AdminArea2,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static string FormatDetails(IReadOnlyList<ErrorDetails>? details) =>
        details is null || details.Count == 0 ? string.Empty : string.Join("; ", details.Select(d => FormatDetail(d.Field, d.Issue, d.Description)));

    private static string FormatDetails(IReadOnlyList<ErrorDetails1>? details) =>
        details is null || details.Count == 0 ? string.Empty : string.Join("; ", details.Select(d => FormatDetail(d.Field, d.Issue, d.Description)));

    private static string FormatDetail(string? field, string issue, string? description)
    {
        var text = field is null ? issue : $"{field}: {issue}";
        return description is null ? text : $"{text} ({description})";
    }

    private static PayPalGatewayException FromError(Error error, string action)
    {
        var details = FormatDetails(error.Details);
        var message = string.IsNullOrEmpty(details)
            ? $"{action} failed: {error.Name} - {error.Message}"
            : $"{action} failed: {error.Name} - {error.Message} [{details}]";
        return new PayPalGatewayException(message, 422, error.Name);
    }

    private static PayPalGatewayException FromError1(Error1 error, string action)
    {
        var details = FormatDetails(error.Details);
        var message = string.IsNullOrEmpty(details)
            ? $"{action} failed: {error.Name} - {error.Message}"
            : $"{action} failed: {error.Name} - {error.Message} [{details}]";
        return new PayPalGatewayException(message, 422, error.Name);
    }

    private static PayPalGatewayException FromRaw(RawError raw, string action)
    {
        var status = (int)raw.StatusCode;
        var body = SafeReadRawError(raw);
        return new PayPalGatewayException($"{action} failed: HTTP {status} - {body}", status);
    }

    private static string SafeReadRawError(RawError raw)
    {
        try
        {
            return raw.ReadAsString();
        }
        catch
        {
            return "(unreadable error body)";
        }
    }
}
