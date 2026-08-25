using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalService> _logger;

    public PayPalService(PayPalServerSdkClient client, ILogger<PayPalService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<AuthorizeResult> CreateAndAuthorizeAsync(
        decimal amount,
        string currency,
        string eShopOrderId,
        string idempotencyKey,
        DirectCardDetails card,
        CancellationToken ct = default)
    {
        var orderRequest = BuildOrderRequest(amount, currency, eShopOrderId, BuildDirectCardPaymentSource(card));
        return await CreateAndAuthorizeInternalAsync(orderRequest, idempotencyKey, ct);
    }

    public async Task<AuthorizeResult> CreateAndAuthorizeWithVaultAsync(
        decimal amount,
        string currency,
        string eShopOrderId,
        string idempotencyKey,
        string vaultTokenId,
        CancellationToken ct = default)
    {
        var orderRequest = BuildOrderRequest(amount, currency, eShopOrderId, BuildVaultedCardPaymentSource(vaultTokenId));
        return await CreateAndAuthorizeInternalAsync(orderRequest, idempotencyKey, ct);
    }

    private async Task<AuthorizeResult> CreateAndAuthorizeInternalAsync(
        OrderRequest orderRequest,
        string idempotencyKey,
        CancellationToken ct)
    {
        Order paypalOrder;
        try
        {
            paypalOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during order creation.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }

        if (paypalOrder.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayPalException(
                "This card requires browser approval and cannot be used for direct payment. " +
                "Ask the payer to use a different card or payment method.");
        }

        var paypalOrderId = paypalOrder.Id ?? throw new PayPalException("PayPal did not return an order ID.");

        // Some cards / SCA configs auto-authorize during CreateOrder — check before calling AuthorizeOrder.
        var createOrderAuth = paypalOrder.PurchaseUnits?[0]?.Payments?.Authorizations?[0];
        if (createOrderAuth?.Id is not null)
        {
            DateTimeOffset? createExpiry = null;
            if (createOrderAuth.ExpirationTime is not null && DateTimeOffset.TryParse(createOrderAuth.ExpirationTime, out var createParsed))
                createExpiry = createParsed;
            return new AuthorizeResult(paypalOrderId, createOrderAuth.Id, createExpiry);
        }

        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: paypalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during authorization.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }

        if (authResponse.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayPalException(
                "This card requires browser approval and cannot be used for direct payment. " +
                "Ask the payer to use a different card or payment method.");
        }

        var auth = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]
            ?? throw new PayPalException("PayPal authorization response did not contain authorization details.");

        var authId = auth.Id ?? throw new PayPalException("PayPal did not return an authorization ID.");

        DateTimeOffset? expiry = null;
        if (auth.ExpirationTime is not null && DateTimeOffset.TryParse(auth.ExpirationTime, out var parsed))
            expiry = parsed;

        return new AuthorizeResult(paypalOrderId, authId, expiry);
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during capture.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }

        var captureId = capture.Id ?? throw new PayPalException("PayPal did not return a capture ID.");
        var breakdown = capture.SellerReceivableBreakdown;
        var gross = ParseMoney(breakdown?.GrossAmount);
        var fee = ParseMoney(breakdown?.PaypalFee);
        var net = ParseMoney(breakdown?.NetAmount);

        return new CaptureResult(captureId, gross, fee, net);
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? body) && body?.Message is not null)
            {
                // 409 = already voided or captured - treat as idempotent
                _logger.LogWarning("VoidPayment returned error: {Msg}", body.Message);
                return;
            }
            if (ex.Error.TryGetNoContent(out _))
                return;
            if (ex.Error.TryGetRawError(out RawError? raw))
                throw new PayPalException($"Failed to void authorization: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
            throw new PayPalException("Failed to void authorization.", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during void.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = amount.Value.ToString("F2") } }
            : null;

        PayPalServerSdk.Models.Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefundError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during refund.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }

        var refundId = refund.Id ?? throw new PayPalException("PayPal did not return a refund ID.");
        var refundedAmount = ParseMoney(refund.Amount);

        return new RefundResult(refundId, refundedAmount, refund.Amount?.CurrencyCode ?? currency);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        PaymentAuthorization result;
        try
        {
            result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = amount.ToString("F2") }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorizeError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during re-authorization.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }

        var newAuthId = result.Id ?? throw new PayPalException("PayPal did not return a new authorization ID.");

        DateTimeOffset? expiry = null;
        if (result.ExpirationTime is not null && DateTimeOffset.TryParse(result.ExpirationTime, out var parsed))
            expiry = parsed;

        return new ReauthorizeResult(newAuthId, expiry);
    }

    public async Task<VaultTokenResult> VaultCardAsync(
        string idempotencyKey,
        DirectCardDetails card,
        string? existingPayPalCustomerId,
        string merchantCustomerId,
        CancellationToken ct = default)
    {
        var request = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = new Address
                    {
                        CountryCode = card.CountryCode,
                        AddressLine1 = card.AddressLine1,
                        AdminArea2 = card.City,
                        AdminArea1 = card.State,
                        PostalCode = card.PostalCode
                    }
                }
            },
            Customer = new Customer
            {
                Id = existingPayPalCustomerId,
                MerchantCustomerId = merchantCustomerId
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: request,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response during card vaulting.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }

        var tokenId = response.Id ?? throw new PayPalException("PayPal did not return a vault token ID.");
        var customerId = response.Customer?.Id;
        var cardInfo = response.PaymentSource?.Card;

        return new VaultTokenResult(
            TokenId: tokenId,
            PayPalCustomerId: customerId,
            Last4: cardInfo?.LastDigits,
            Brand: cardInfo?.Brand?.Value,
            Expiry: cardInfo?.Expiry);
    }

    public async Task<IReadOnlyList<VaultTokenResult>> ListVaultedCardsAsync(
        string paypalCustomerId,
        CancellationToken ct = default)
    {
        var all = new List<VaultTokenResult>();
        int page = 1;
        int totalPages;

        do
        {
            CustomerVaultPaymentTokensResponse resp;
            try
            {
                resp = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: paypalCustomerId,
                    pageSize: 20,
                    page: page,
                    totalRequired: true,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                throw TranslateVaultListError(ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new PayPalException("PayPal returned an unprocessable response listing vault tokens.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("Unable to reach PayPal.", ex);
            }

            if (resp.PaymentTokens is not null)
            {
                foreach (var t in resp.PaymentTokens)
                {
                    if (t.Id is null) continue;
                    var cardInfo = t.PaymentSource?.Card;
                    all.Add(new VaultTokenResult(
                        TokenId: t.Id,
                        PayPalCustomerId: paypalCustomerId,
                        Last4: cardInfo?.LastDigits,
                        Brand: cardInfo?.Brand?.Value,
                        Expiry: cardInfo?.Expiry));
                }
            }

            totalPages = resp.TotalPages ?? 1;
            page++;
        } while (page <= totalPages);

        return all;
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: tokenId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateDeleteVaultError(ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response deleting vault token.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Unable to reach PayPal.", ex);
        }
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken ct = default)
    {
        var all = new List<TransactionRecord>();
        int page = 1;
        int totalPages;

        do
        {
            SearchResponse resp;
            try
            {
                resp = await _client.TransactionSearch.SearchTransactions(
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
                    fields: "all",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalException(
                    $"PayPal transaction search failed: {ex.Error.ReadAsString()}",
                    (int)ex.Error.StatusCode);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new PayPalException("PayPal returned an unprocessable response during transaction search.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("Unable to reach PayPal.", ex);
            }

            if (resp.TransactionDetails is not null)
            {
                foreach (var td in resp.TransactionDetails)
                {
                    var info = td.TransactionInfo;
                    all.Add(new TransactionRecord(
                        TransactionId: info?.TransactionId,
                        Amount: info?.TransactionAmount?.Value,
                        Currency: info?.TransactionAmount?.CurrencyCode,
                        Status: info?.TransactionStatus,
                        InitiatedDate: info?.TransactionInitiationDate,
                        InvoiceId: info?.InvoiceId,
                        CustomField: info?.CustomField,
                        ReferenceId: info?.PaypalReferenceId));
                }
            }

            totalPages = resp.TotalPages ?? 1;
            page++;
        } while (page <= totalPages);

        return all;
    }

    // --- helpers ---

    private static OrderRequest BuildOrderRequest(decimal amount, string currency, string eShopOrderId, PaymentSource paymentSource)
    {
        return new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2")
                    },
                    CustomId = eShopOrderId,
                    // InvoiceId deliberately omitted; CustomId carries the eShop order ID for reconciliation.
                    // PayPal sandbox rejects repeated invoice IDs even across in-memory DB restarts.
                }
            },
            PaymentSource = paymentSource
        };
    }

    private static PaymentSource BuildDirectCardPaymentSource(DirectCardDetails card)
    {
        return new PaymentSource
        {
            Card = new CardRequest
            {
                Name = card.Name,
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                BillingAddress = new Address
                {
                    CountryCode = card.CountryCode,
                    AddressLine1 = card.AddressLine1,
                    AdminArea2 = card.City,
                    AdminArea1 = card.State,
                    PostalCode = card.PostalCode
                },
                Attributes = new CardAttributes
                {
                    Verification = new CardVerification
                    {
                        Method = OrdersCardVerificationMethod.ScaWhenRequired
                    }
                }
            }
        };
    }

    private static PaymentSource BuildVaultedCardPaymentSource(string vaultTokenId)
    {
        return new PaymentSource
        {
            Card = new CardRequest
            {
                VaultId = vaultTokenId
            }
        };
    }

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value is null) return 0m;
        return decimal.TryParse(money.Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static PayPalException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body is not null)
        {
            var msg = body.Message ?? "Order creation rejected";
            if (body.Details is { Count: > 0 })
            {
                var issues = string.Join("; ", body.Details.Select(d => $"{d.Issue}[field={d.Field}]: {d.Description}"));
                msg = $"{msg} — Details: {issues}";
            }
            return new PayPalException($"PayPal rejected order creation: {msg}");
        }
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal order creation failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal order creation failed.", ex);
    }

    private static PayPalException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body is not null)
        {
            var msg = body.Message ?? "Authorization rejected";
            if (body.Details is { Count: > 0 })
            {
                var issues = string.Join("; ", body.Details.Select(d => $"{d.Issue}[field={d.Field}]: {d.Description}"));
                msg = $"{msg} — Details: {issues}";
            }
            return new PayPalException($"PayPal rejected authorization: {msg}");
        }
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal authorization failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal authorization failed.", ex);
    }

    private static PayPalException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body?.Message is not null)
            return new PayPalException($"PayPal capture failed: {body.Message}", 409);
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("PayPal capture failed with no detail.", 500);
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal capture failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal capture failed.", ex);
    }

    private static PayPalException TranslateRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body is not null)
        {
            var msg = body.Message ?? "Refund rejected";
            if (body.Details is { Count: > 0 })
            {
                var issues = string.Join("; ", body.Details.Select(d => $"{d.Issue}[field={d.Field}]: {d.Description}"));
                msg = $"{msg} — Details: {issues}";
            }
            return new PayPalException($"PayPal refund failed: {msg}");
        }
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("PayPal refund failed with no detail.", 500);
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal refund failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal refund failed.", ex);
    }

    private static PayPalException TranslateReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body?.Message is not null)
            return new PayPalException($"Authorization expired and re-authorization failed — order cannot proceed. PayPal: {body.Message}");
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("Authorization expired and re-authorization failed — order cannot proceed.", 500);
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"Authorization expired and re-authorization failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("Authorization expired and re-authorization failed — order cannot proceed.", ex);
    }

    private static PayPalException TranslateVaultError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1? body) && body?.Message is not null)
            return new PayPalException($"PayPal card vaulting failed: {body.Message}");
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal card vaulting failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal card vaulting failed.", ex);
    }

    private static PayPalException TranslateVaultListError(SdkException<ListCustomerPaymentTokensError> ex)
    {
        if (ex.Error.TryGetError1(out Error1? body) && body?.Message is not null)
            return new PayPalException($"PayPal vault list failed: {body.Message}");
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal vault list failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal vault list failed.", ex);
    }

    private static PayPalException TranslateDeleteVaultError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out Error1? body) && body?.Message is not null)
            return new PayPalException($"PayPal vault delete failed: {body.Message}");
        if (ex.Error.TryGetRawError(out RawError? raw))
            return new PayPalException($"PayPal vault delete failed: {raw?.ReadAsString()}", (int?)raw?.StatusCode ?? 0);
        return new PayPalException("PayPal vault delete failed.", ex);
    }
}
