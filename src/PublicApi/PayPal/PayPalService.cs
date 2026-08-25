using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalOrder = PayPalServerSdk.Models.Order;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;

    public PayPalService(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizeResult> AuthorizeWithCardAsync(
        decimal amount, string currency, CardDetails card, string orderRef, CancellationToken ct = default)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amountStr
                    },
                    CustomId = orderRef
                }
            }
        };

        PayPalOrder createResp;
        try
        {
            createResp = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var e))
            {
                var details = e?.Details != null
                    ? string.Join("; ", System.Linq.Enumerable.Select(e.Details, d => $"field={d.Field} issue={d.Issue}: {d.Description}"))
                    : "";
                msg = $"{e?.Message} [{e?.Name}]{(details.Length > 0 ? " | " + details : "")}";
            }
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal create order failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during order creation.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during order creation.", inner: ex);
        }

        var payPalOrderId = createResp.Id ?? throw new PayPalException("PayPal did not return an order ID.");

        // Pass card in the authorize body so PayPal validates and authorizes in one step
        var authorizeBody = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.Cvv,
                    Name = card.CardholderName,
                    BillingAddress = new Address
                    {
                        CountryCode = card.BillingCountry,
                        AddressLine1 = card.BillingStreet,
                        AdminArea2 = card.BillingCity,
                        AdminArea1 = card.BillingState,
                        PostalCode = card.BillingZip
                    }
                }
            }
        };
        return await DoAuthorizeOrderAsync(payPalOrderId, idempotencyKey, body: authorizeBody, ct);
    }

    public async Task<AuthorizeResult> AuthorizeWithVaultTokenAsync(
        decimal amount, string currency, string vaultToken, string orderRef, CancellationToken ct = default)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = amountStr
                    },
                    CustomId = orderRef
                }
            }
        };

        PayPalOrder createResp;
        try
        {
            createResp = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var e))
            {
                var details = e?.Details != null
                    ? string.Join("; ", System.Linq.Enumerable.Select(e.Details, d => $"field={d.Field} issue={d.Issue}: {d.Description}"))
                    : "";
                msg = $"{e?.Message} [{e?.Name}]{(details.Length > 0 ? " | " + details : "")}";
            }
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal create order (vault) failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during order creation.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during order creation.", inner: ex);
        }

        var payPalOrderId = createResp.Id ?? throw new PayPalException("PayPal did not return an order ID.");

        var authorizeBody = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Token = new Token
                {
                    Id = vaultToken,
                    Type = TokenType.FromValue("PAYMENT_METHOD_TOKEN")
                }
            }
        };
        return await DoAuthorizeOrderAsync(payPalOrderId, idempotencyKey, body: authorizeBody, ct);
    }

    private async Task<AuthorizeResult> DoAuthorizeOrderAsync(
        string payPalOrderId, string idempotencyKey, OrderAuthorizeRequest? body, CancellationToken ct)
    {
        OrderAuthorizeResponse authResp;
        try
        {
            authResp = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey + "-auth",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var e)) msg = $"{e?.Message} [{e?.Name}]";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal authorize order failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during authorization.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during authorization.", inner: ex);
        }

        var authorizationId = authResp.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id
            ?? throw new PayPalException("PayPal did not return an authorization ID.");

        return new AuthorizeResult(payPalOrderId, authorizationId);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, CancellationToken ct = default)
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        CapturedPayment captureResp;
        try
        {
            captureResp = await _client.Payments.CaptureAuthorizedPayment(
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
            string msg;
            if (ex.Error.TryGetError(out var e)) msg = $"{e?.Message} [{e?.Name}]";
            else if (ex.Error.TryGetNoContent(out var nc)) msg = $"HTTP 500 no content: {nc.ReadAsString()}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal capture failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during capture.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during capture.", inner: ex);
        }

        var captureId = captureResp.Id ?? throw new PayPalException("PayPal did not return a capture ID.");
        var capturedAmount = ParseMoney(captureResp.Amount);
        var fee = ParseMoney(captureResp.SellerReceivableBreakdown?.PaypalFee);
        var net = ParseMoney(captureResp.SellerReceivableBreakdown?.NetAmount);

        return new CaptureResult(captureId, capturedAmount, fee, net);
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
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var e)) msg = $"{e?.Message} [{e?.Name}]";
            else if (ex.Error.TryGetNoContent(out var nc)) msg = $"HTTP 500 no content: {nc.ReadAsString()}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal void failed: {msg}");
        }
        catch (SdkException<PayPalServerSdk.Core.ErrorResponse.RawError> ex)
        {
            throw new PayPalException($"PayPal void failed: HTTP {(int)ex.Error.StatusCode} {ex.Error.ReadAsString()}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during void.", inner: ex);
        }
        catch (System.Text.Json.JsonException)
        {
            // PayPal returns 204 No Content on successful void; the SDK throws JsonException
            // trying to deserialize an empty body. Treat this as success.
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, decimal? amount, string currency, CancellationToken ct = default)
    {
        RefundRequest? body = null;
        if (amount.HasValue)
        {
            body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = amount.Value.ToString("F2", CultureInfo.InvariantCulture)
                }
            };
        }

        Refund refundResp;
        try
        {
            refundResp = await _client.Payments.RefundCapturedPayment(
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
            string msg;
            if (ex.Error.TryGetError(out var e)) msg = $"{e?.Message} [{e?.Name}]";
            else if (ex.Error.TryGetNoContent(out var nc)) msg = $"HTTP 500 no content: {nc.ReadAsString()}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal refund failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during refund.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during refund.", inner: ex);
        }

        var refundId = refundResp.Id ?? throw new PayPalException("PayPal did not return a refund ID.");
        return new RefundResult(refundId);
    }

    public async Task<string> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var body = new ReauthorizeRequest
        {
            Amount = new Money
            {
                CurrencyCode = currency,
                Value = amount.ToString("F2", CultureInfo.InvariantCulture)
            }
        };

        PaymentAuthorization reauthorizeResp;
        try
        {
            reauthorizeResp = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            string msg;
            if (ex.Error.TryGetError(out var e)) msg = $"{e?.Message} [{e?.Name}]";
            else if (ex.Error.TryGetNoContent(out var nc)) msg = $"HTTP 500 no content: {nc.ReadAsString()}";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal reauthorize failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during reauthorization.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during reauthorization.", inner: ex);
        }

        return reauthorizeResp.Id ?? throw new PayPalException("PayPal did not return a new authorization ID after reauthorize.");
    }

    public async Task<VaultCardResult> VaultCardAsync(
        CardDetails card, string? existingPayPalCustomerId, string merchantCustomerId, CancellationToken ct = default)
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        var setupTokenRequest = new SetupTokenRequest
        {
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.Cvv,
                    Name = card.CardholderName,
                    BillingAddress = new Address
                    {
                        CountryCode = card.BillingCountry,
                        AddressLine1 = card.BillingStreet,
                        AdminArea2 = card.BillingCity,
                        AdminArea1 = card.BillingState,
                        PostalCode = card.BillingZip
                    },
                    VerificationMethod = VaultCardVerificationMethod.ScaWhenRequired,
                    ExperienceContext = new VaultCardExperienceContext
                    {
                        ReturnUrl = "https://localhost/vault-return",
                        CancelUrl = "https://localhost/vault-cancel",
                        UserAction = VaultUserAction.Continue
                    }
                }
            },
            Customer = new Customer
            {
                Id = existingPayPalCustomerId,
                MerchantCustomerId = existingPayPalCustomerId == null ? merchantCustomerId : null
            }
        };

        SetupTokenResponse setupResp;
        try
        {
            setupResp = await _client.Vault.CreateSetupToken(
                payPalRequestId: idempotencyKey,
                body: setupTokenRequest,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            string msg;
            if (ex.Error.TryGetError1(out var e))
            {
                var details = e?.Details != null
                    ? string.Join("; ", System.Linq.Enumerable.Select(e.Details, d => $"field={d.Field} issue={d.Issue}: {d.Description}"))
                    : "";
                msg = $"{e?.Message} [{e?.Name}]{(details.Length > 0 ? " | " + details : "")}";
            }
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal vault setup failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during vault setup.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during vault setup.", inner: ex);
        }

        var setupTokenId = setupResp.Id ?? throw new PayPalException("PayPal did not return a setup token ID.");
        var payPalCustomerId = setupResp.Customer?.Id;

        var paymentTokenRequest = new PaymentTokenRequest
        {
            Customer = new Customer { Id = payPalCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupTokenId,
                    Type = VaultTokenRequestType.SetupToken
                }
            }
        };

        PaymentTokenResponse tokenResp;
        try
        {
            tokenResp = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey + "-token",
                body: paymentTokenRequest,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            string msg;
            if (ex.Error.TryGetError1(out var e))
            {
                var details = e?.Details != null
                    ? string.Join("; ", System.Linq.Enumerable.Select(e.Details, d => $"field={d.Field} issue={d.Issue}: {d.Description}"))
                    : "";
                msg = $"{e?.Message} [{e?.Name}]{(details.Length > 0 ? " | " + details : "")}";
            }
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal vault token creation failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during vault token creation.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during vault token creation.", inner: ex);
        }

        var vaultToken = tokenResp.Id ?? throw new PayPalException("PayPal did not return a vault token ID.");
        var cardEntity = tokenResp.PaymentSource?.Card;
        var brandValue = cardEntity?.Brand?.Value;
        var last4 = cardEntity?.LastDigits;
        var expiry = cardEntity?.Expiry;

        return new VaultCardResult(vaultToken, payPalCustomerId, brandValue, last4, expiry);
    }

    public async Task DeleteVaultTokenAsync(string vaultToken, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: vaultToken,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            string msg;
            if (ex.Error.TryGetError1(out var e)) msg = $"{e?.Message} [{e?.Name}]";
            else if (ex.Error.TryGetRawError(out var raw)) msg = raw.ReadAsString();
            else msg = "unknown error";
            throw new PayPalException($"PayPal delete vault token failed: {msg}");
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal unreachable during vault token deletion.", inner: ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response during vault token deletion.", inner: ex);
        }
    }

    public async Task<IReadOnlyList<TransactionItem>> SearchTransactionsAsync(
        string startDate, string endDate, CancellationToken ct = default)
    {
        var results = new List<TransactionItem>();
        int currentPage = 1;
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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: currentPage,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                var body = ex.Error.ReadAsString();
                throw new PayPalException(
                    $"PayPal transaction search failed: HTTP {(int)ex.Error.StatusCode} | {body}",
                    httpStatus: (int)ex.Error.StatusCode,
                    payPalMessage: body,
                    inner: ex);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("PayPal unreachable during transaction search.", inner: ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new PayPalException("PayPal returned an unreadable response during transaction search.", inner: ex);
            }

            totalPages = resp.TotalPages ?? 1;

            if (resp.TransactionDetails != null)
            {
                foreach (var detail in resp.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    results.Add(new TransactionItem(
                        TransactionId: info?.TransactionId,
                        Amount: info?.TransactionAmount?.Value,
                        Currency: info?.TransactionAmount?.CurrencyCode,
                        Fee: info?.FeeAmount?.Value,
                        Status: info?.TransactionStatus,
                        InitiationDate: info?.TransactionInitiationDate,
                        EventCode: info?.TransactionEventCode));
                }
            }

            currentPage++;
        } while (currentPage <= totalPages);

        return results;
    }

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value is null) return 0m;
        return decimal.TryParse(money.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var val) ? val : 0m;
    }
}
