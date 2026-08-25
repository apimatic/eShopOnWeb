using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public record AuthorizeResult(string PayPalOrderId, string AuthorizationId);
public record CaptureResult(string CaptureId, string CapturedAmount, string Currency, string? FeeAmount, string? NetAmount);
public record RefundResult(string RefundId, string? RefundedAmount, string? Currency);
public record ReauthorizeResult(string NewAuthorizationId);
public record VaultResult(string VaultTokenId, string? Last4, string? CardBrand, string? PayPalCustomerId);
public record SavedCardInfo(string VaultTokenId, string? Last4, string? CardBrand);
public record TransactionRecord(
    string? TransactionId,
    string? CustomField,
    string? Status,
    string? Amount,
    string? Currency,
    int? EShopOrderId);

public class PayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalPaymentService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _currency = settings.Value.Currency;
    }

    public async Task<AuthorizeResult> AuthorizeAsync(
        int orderId, decimal amount, string? cardNumber, string? expiry, string? cvv,
        string? vaultTokenId, CancellationToken ct)
    {
        // Pattern A: card/vault goes into CreateOrder's PaymentSource.
        // The authorization is processed inline; AuthorizeOrder is not called.
        CardRequest? cardRequest = null;
        if (!string.IsNullOrEmpty(vaultTokenId))
        {
            cardRequest = new CardRequest { VaultId = vaultTokenId };
        }
        else if (!string.IsNullOrEmpty(cardNumber))
        {
            cardRequest = new CardRequest
            {
                Number = cardNumber,
                Expiry = expiry,
                SecurityCode = cvv,
                BillingAddress = new Address { CountryCode = "US" }
            };
        }

        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = _currency,
                        Value = amount.ToString("F2")
                    },
                    CustomId = orderId.ToString()
                }
            },
            PaymentSource = cardRequest != null ? new PaymentSource { Card = cardRequest } : null
        };

        PayPalServerSdk.Models.Order createdOrder;
        try
        {
            createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-order-{orderId}-{Guid.NewGuid():N}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during order creation.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during order creation.", 502, ex);
        }

        // Auth ID is returned in the CreateOrder response (Pattern A)
        var authId = createdOrder.PurchaseUnits?[0].Payments?.Authorizations?[0].Id
            ?? throw new PayPalException("PayPal did not return an authorization ID in the CreateOrder response.", 502);

        return new AuthorizeResult(createdOrder.Id!, authId);
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, int orderId, CancellationToken ct)
    {
        CapturedPayment result;
        try
        {
            result = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"capture-{orderId}",
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureError(ex, authorizationId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during capture.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during capture.", 502, ex);
        }

        return new CaptureResult(
            CaptureId: result.Id ?? throw new PayPalException("PayPal did not return a capture ID.", 502),
            CapturedAmount: result.SellerReceivableBreakdown?.GrossAmount?.Value ?? "0",
            Currency: result.SellerReceivableBreakdown?.GrossAmount?.CurrencyCode ?? _currency,
            FeeAmount: result.SellerReceivableBreakdown?.PaypalFee?.Value,
            NetAmount: result.SellerReceivableBreakdown?.NetAmount?.Value);
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, int orderId, CancellationToken ct)
    {
        PaymentAuthorization result;
        try
        {
            result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{orderId}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = _currency, Value = amount.ToString("F2") }
                },
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorizeError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during reauthorization.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during reauthorization.", 502, ex);
        }

        return new ReauthorizeResult(result.Id ?? throw new PayPalException("Reauthorization did not return a new authorization ID.", 502));
    }

    public async Task VoidAsync(string authorizationId, int orderId, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"void-{orderId}",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            // 409 means already voided or captured — treat as idempotent success
            if (ex.Error.TryGetError(out var error))
            {
                if (IsConflict(error)) return;
                throw new PayPalException($"Void failed: {SummarizeError(error)}", 422);
            }
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("Void failed with PayPal internal error.", 502);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Void failed: {raw.ReadAsString()}", (int)raw.StatusCode);
            throw new PayPalException("Void failed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during void.", 502, ex);
        }
        catch (JsonException)
        {
            // PayPal VoidPayment returns 204 No Content on success.
            // The SDK throws JsonException when deserializing the empty response body — treat as success.
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = _currency, Value = amount.Value.ToString("F2") } }
            : null;

        Refund result;
        try
        {
            result = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            // 409 means this idempotency key already succeeded — extract refund ID from error if possible
            if (ex.Error.TryGetError(out var error) && IsConflict(error))
            {
                // The body describes the existing refund; surface as success with unknown amounts
                return new RefundResult(RefundId: $"duplicate-{idempotencyKey}", RefundedAmount: null, Currency: null);
            }
            if (ex.Error.TryGetError(out var err))
                throw new PayPalException($"Refund failed: {SummarizeError(err)}", 422);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("Refund failed with PayPal internal error.", 502);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Refund failed: {raw.ReadAsString()}", (int)raw.StatusCode);
            throw new PayPalException("Refund failed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during refund.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during refund.", 502, ex);
        }

        return new RefundResult(
            RefundId: result.Id ?? throw new PayPalException("Refund did not return an ID.", 502),
            RefundedAmount: result.SellerPayableBreakdown?.GrossAmount?.Value,
            Currency: result.SellerPayableBreakdown?.GrossAmount?.CurrencyCode);
    }

    public async Task<VaultResult> SaveCardAsync(
        string cardNumber, string expiry, string cvv, string? cardName,
        string? existingCustomerId, CancellationToken ct)
    {
        // Step A: CreateSetupToken
        var setupTokenRequest = new SetupTokenRequest
        {
            PaymentSource = new SetupTokenRequestPaymentSource
            {
                Card = new SetupTokenRequestCard
                {
                    Number = cardNumber,
                    Expiry = expiry,
                    SecurityCode = cvv,
                    Name = cardName
                }
            }
        };
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            setupTokenRequest = new SetupTokenRequest
            {
                Customer = new Customer { Id = existingCustomerId },
                PaymentSource = new SetupTokenRequestPaymentSource
                {
                    Card = new SetupTokenRequestCard
                    {
                        Number = cardNumber,
                        Expiry = expiry,
                        SecurityCode = cvv,
                        Name = cardName
                    }
                }
            };
        }

        SetupTokenResponse setupToken;
        try
        {
            setupToken = await _client.Vault.CreateSetupToken(
                payPalRequestId: $"setup-{Guid.NewGuid():N}",
                body: setupTokenRequest,
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PayPalException($"Card vault setup failed: {err.Message ?? "Unknown error"}", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Card vault setup failed: {raw.ReadAsString()}", (int)raw.StatusCode);
            throw new PayPalException("Card vault setup failed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during vault setup.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during vault setup.", 502, ex);
        }

        // Step B: CreatePaymentToken
        var paymentTokenRequest = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Token = new VaultTokenRequest
                {
                    Id = setupToken.Id!,
                    Type = VaultTokenRequestType.SetupToken
                }
            }
        };
        if (!string.IsNullOrEmpty(existingCustomerId))
        {
            paymentTokenRequest = new PaymentTokenRequest
            {
                Customer = new Customer { Id = existingCustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Token = new VaultTokenRequest
                    {
                        Id = setupToken.Id!,
                        Type = VaultTokenRequestType.SetupToken
                    }
                }
            };
        }

        PaymentTokenResponse paymentToken;
        try
        {
            paymentToken = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"vault-{Guid.NewGuid():N}",
                body: paymentTokenRequest,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PayPalException($"Card vaulting failed: {err.Message ?? "Unknown error"}", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Card vaulting failed: {raw.ReadAsString()}", (int)raw.StatusCode);
            throw new PayPalException("Card vaulting failed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during card vaulting.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during card vaulting.", 502, ex);
        }

        return new VaultResult(
            VaultTokenId: paymentToken.Id ?? throw new PayPalException("Vault did not return a token ID.", 502),
            Last4: paymentToken.PaymentSource?.Card?.LastDigits,
            CardBrand: paymentToken.PaymentSource?.Card?.Brand?.Value,
            PayPalCustomerId: paymentToken.Customer?.Id);
    }

    public async Task<IReadOnlyList<SavedCardInfo>> ListSavedCardsAsync(string payPalCustomerId, CancellationToken ct)
    {
        var allTokens = new List<SavedCardInfo>();
        int page = 1;
        int? totalPages = null;

        do
        {
            CustomerVaultPaymentTokensResponse response;
            try
            {
                response = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: payPalCustomerId,
                    pageSize: 20,
                    page: page,
                    totalRequired: page == 1,
                    ct: ct);
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                if (ex.Error.TryGetError1(out var err))
                    throw new PayPalException($"Listing saved cards failed: {err.Message ?? "Unknown error"}", 422);
                if (ex.Error.TryGetRawError(out var raw))
                    throw new PayPalException($"Listing saved cards failed: {raw.ReadAsString()}", (int)raw.StatusCode);
                throw new PayPalException("Listing saved cards failed.", 502, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new PayPalException("PayPal service unavailable during list.", 502, ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalException("Invalid response from PayPal during list.", 502, ex);
            }

            if (page == 1) totalPages = response.TotalPages ?? 1;

            if (response.PaymentTokens != null)
            {
                foreach (var t in response.PaymentTokens)
                {
                    allTokens.Add(new SavedCardInfo(
                        VaultTokenId: t.Id ?? string.Empty,
                        Last4: t.PaymentSource?.Card?.LastDigits,
                        CardBrand: t.PaymentSource?.Card?.Brand?.Value));
                }
            }

            page++;
        } while (page <= totalPages);

        return allTokens;
    }

    public async Task DeleteSavedCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PayPalException($"Deleting saved card failed: {err.Message ?? "Unknown error"}", 422);
            if (ex.Error.TryGetRawError(out var raw))
            {
                // 404 — already gone, treat as idempotent success
                if ((int)raw.StatusCode == 404) return;
                throw new PayPalException($"Deleting saved card failed: {raw.ReadAsString()}", (int)raw.StatusCode);
            }
            throw new PayPalException("Deleting saved card failed.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new PayPalException("PayPal service unavailable during delete.", 502, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("Invalid response from PayPal during delete.", 502, ex);
        }
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var all = new List<TransactionRecord>();
        int page = 1;
        int? totalPages = null;

        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz");
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz");

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
                    pageSize: 500,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalException(
                    $"Transaction search failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}",
                    (int)ex.Error.StatusCode, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new PayPalException("PayPal service unavailable during transaction search.", 502, ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalException("Invalid response from PayPal during transaction search.", 502, ex);
            }

            if (page == 1) totalPages = response.TotalPages ?? 1;

            if (response.TransactionDetails != null)
            {
                foreach (var td in response.TransactionDetails)
                {
                    var customField = td.TransactionInfo?.CustomField;
                    int? eShopOrderId = null;
                    if (int.TryParse(customField, out var parsed)) eShopOrderId = parsed;

                    all.Add(new TransactionRecord(
                        TransactionId: td.TransactionInfo?.TransactionId,
                        CustomField: customField,
                        Status: td.TransactionInfo?.TransactionStatus,
                        Amount: td.TransactionInfo?.TransactionAmount?.Value,
                        Currency: td.TransactionInfo?.TransactionAmount?.CurrencyCode,
                        EShopOrderId: eShopOrderId));
                }
            }

            page++;
        } while (page <= totalPages);

        return all;
    }

    // --- Error translation helpers ---

    private PayPalException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"Order creation failed: {SummarizeError(error)}", 422);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"Order creation failed: {raw.ReadAsString()}", (int)raw.StatusCode);
        return new PayPalException("Order creation failed.", 502, ex);
    }

    private PayPalException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"Authorization failed: {SummarizeError(error)}", 422);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"Authorization failed: {raw.ReadAsString()}", (int)raw.StatusCode);
        return new PayPalException("Authorization failed.", 502, ex);
    }

    private PayPalException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex, string authorizationId)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var body = SummarizeError(error);
            if (body.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase))
                return new PayPalAuthorizationExpiredException(authorizationId);
            if (IsConflict(error))
            {
                // 409 = duplicate capture key, treat as success — caller handles
                return new PayPalException("DUPLICATE_CAPTURE", 409);
            }
            return new PayPalException($"Capture failed: {body}", 422);
        }
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("Capture failed with PayPal internal error.", 502);
        if (ex.Error.TryGetRawError(out var raw))
        {
            var rawBody = raw.ReadAsString();
            if (rawBody.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase))
                return new PayPalAuthorizationExpiredException(authorizationId);
            return new PayPalException($"Capture failed: {rawBody}", (int)raw.StatusCode);
        }
        return new PayPalException("Capture failed.", 502, ex);
    }

    private PayPalException TranslateReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var body = SummarizeError(error);
            if (body.Contains("CANNOT_BE_REAUTHORIZED", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("MAX_NUMBER_OF_PAYMENT_ATTEMPTS_EXCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return new PayPalException(
                    "The authorization can no longer be renewed. A new order must be created.",
                    422);
            }
            return new PayPalException($"Reauthorization failed: {body}", 422);
        }
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("Reauthorization failed with PayPal internal error.", 502);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"Reauthorization failed: {raw.ReadAsString()}", (int)raw.StatusCode);
        return new PayPalException("Reauthorization failed.", 502, ex);
    }

    private static string SummarizeError(Error error)
    {
        var msg = error.Message ?? error.Name ?? "Unknown PayPal error";
        if (error.Details != null && error.Details.Count > 0)
        {
            var details = string.Join("; ", System.Linq.Enumerable.Select(error.Details,
                d => $"{d.Issue ?? d.Field}: {d.Description ?? d.Value}"));
            return $"{msg} [{details}]";
        }
        return msg;
    }

    private static bool IsConflict(Error error)
    {
        return error.Name?.Contains("DUPLICATE", StringComparison.OrdinalIgnoreCase) == true
            || error.Name?.Contains("ALREADY", StringComparison.OrdinalIgnoreCase) == true;
    }
}
