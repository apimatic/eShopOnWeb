using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PaypalServerSdk.Standard;
using PaypalServerSdk.Standard.Exceptions;
using PaypalServerSdk.Standard.Models;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PaypalServerSdkClient _client;

    public PayPalPaymentService(PaypalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<string> CreateOrderAsync(decimal total, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.OrdersController.CreateOrderAsync(
                new CreateOrderInput
                {
                    ContentType = "application/json",
                    PaypalRequestId = idempotencyKey,
                    Prefer = "return=representation",
                    Body = new OrderRequest
                    {
                        Intent = CheckoutPaymentIntent.Authorize,
                        PurchaseUnits = new List<PurchaseUnitRequest>
                        {
                            new PurchaseUnitRequest
                            {
                                Amount = new AmountWithBreakdown
                                {
                                    CurrencyCode = currency,
                                    MValue = total.ToString("F2", CultureInfo.InvariantCulture),
                                }
                            }
                        }
                    }
                }, ct);

            return resp.Data?.Id
                ?? throw new PayPalPaymentException("PayPal did not return an order ID.", 502);
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(
        string paypalOrderId, CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.OrdersController.AuthorizeOrderAsync(
                new AuthorizeOrderInput
                {
                    Id = paypalOrderId,
                    ContentType = "application/json",
                    Prefer = "return=representation",
                    PaypalRequestId = idempotencyKey,
                    Body = new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource
                        {
                            Card = new CardRequest
                            {
                                Number = card.CardNumber,
                                Expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
                                SecurityCode = card.Cvv,
                                Name = card.CardholderName,
                                BillingAddress = new Address
                                {
                                    CountryCode = card.CountryCode,
                                    AddressLine1 = card.Street,
                                    AdminArea2 = card.City,
                                    AdminArea1 = card.State,
                                    PostalCode = card.PostalCode,
                                }
                            }
                        }
                    }
                }, ct);

            return ExtractAuthorizationResult(resp.Data);
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultTokenAsync(
        string paypalOrderId, string vaultTokenId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.OrdersController.AuthorizeOrderAsync(
                new AuthorizeOrderInput
                {
                    Id = paypalOrderId,
                    ContentType = "application/json",
                    Prefer = "return=representation",
                    PaypalRequestId = idempotencyKey,
                    Body = new OrderAuthorizeRequest
                    {
                        PaymentSource = new OrderAuthorizeRequestPaymentSource
                        {
                            Card = new CardRequest { VaultId = vaultTokenId }
                        }
                    }
                }, ct);

            return ExtractAuthorizationResult(resp.Data);
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.PaymentsController.CaptureAuthorizedPaymentAsync(
                new CaptureAuthorizedPaymentInput
                {
                    AuthorizationId = authorizationId,
                    ContentType = "application/json",
                    Prefer = "return=representation",
                    PaypalRequestId = idempotencyKey,
                    Body = new CaptureRequest { FinalCapture = true }
                }, ct);

            var data = resp.Data;
            return new CaptureResult(
                CaptureId: data?.Id
                    ?? throw new PayPalPaymentException("PayPal did not return a capture ID.", 502),
                Status: data?.Status?.ToString() ?? "",
                Amount: data?.Amount?.MValue,
                PayPalFee: data?.SellerReceivableBreakdown?.PaypalFee?.MValue,
                NetAmount: data?.SellerReceivableBreakdown?.NetAmount?.MValue
            );
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<AuthorizationResult> ReauthorizeAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.PaymentsController.ReauthorizePaymentAsync(
                new ReauthorizePaymentInput
                {
                    AuthorizationId = authorizationId,
                    ContentType = "application/json",
                    Prefer = "return=representation",
                    PaypalRequestId = idempotencyKey,
                    Body = new ReauthorizeRequest()
                }, ct);

            var data = resp.Data;
            return new AuthorizationResult(
                AuthorizationId: data?.Id
                    ?? throw new PayPalPaymentException("PayPal did not return a re-authorization ID.", 502),
                Status: data?.Status?.ToString() ?? "",
                ExpiresAt: data?.ExpirationTime
            );
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            var details = ex.Details is { Count: > 0 }
                ? string.Join("; ", ex.Details.Select(d => d.Issue ?? d.Description ?? ""))
                : "";
            var msg = $"Re-authorization failed: {ex.Name ?? ex.Message}. {details}".TrimEnd();
            throw new PayPalPaymentException(msg, ex.HttpContext?.Response?.StatusCode ?? 422, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(
                $"Re-authorization cannot be completed (HTTP {ex.HttpContext?.Response?.StatusCode}). Contact PayPal support.",
                ex.HttpContext?.Response?.StatusCode ?? 422, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.PaymentsController.VoidPaymentAsync(
                new VoidPaymentInput { AuthorizationId = authorizationId }, ct);
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var body = amount.HasValue
                ? new RefundRequest
                  {
                      Amount = new Money
                      {
                          CurrencyCode = currency,
                          MValue = amount.Value.ToString("F2", CultureInfo.InvariantCulture)
                      }
                  }
                : null;

            var resp = await _client.PaymentsController.RefundCapturedPaymentAsync(
                new RefundCapturedPaymentInput
                {
                    CaptureId = captureId,
                    ContentType = "application/json",
                    Prefer = "return=representation",
                    PaypalRequestId = idempotencyKey,
                    Body = body,
                }, ct);

            var data = resp.Data;
            return new RefundResult(
                RefundId: data?.Id
                    ?? throw new PayPalPaymentException("PayPal did not return a refund ID.", 502),
                Status: data?.Status?.ToString() ?? "",
                Amount: data?.Amount?.MValue
            );
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<VaultTokenResult> VaultCardAsync(
        string customerId, CardPaymentDetails card, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.VaultController.CreatePaymentTokenAsync(
                new CreatePaymentTokenInput
                {
                    ContentType = "application/json",
                    Body = new PaymentTokenRequest
                    {
                        Customer = new Customer { Id = customerId },
                        PaymentSource = new PaymentTokenRequestPaymentSource
                        {
                            Card = new PaymentTokenRequestCard
                            {
                                Number = card.CardNumber,
                                Expiry = $"{card.ExpiryYear:D4}-{card.ExpiryMonth:D2}",
                                SecurityCode = card.Cvv,
                                Name = card.CardholderName,
                            }
                        }
                    }
                }, ct);

            var data = resp.Data;
            return new VaultTokenResult(
                TokenId: data?.Id
                    ?? throw new PayPalPaymentException("PayPal did not return a vault token ID.", 502),
                Last4: data?.PaymentSource?.Card?.LastDigits,
                Brand: data?.PaymentSource?.Card?.Brand?.ToString(),
                Expiry: data?.PaymentSource?.Card?.Expiry,
                CardType: data?.PaymentSource?.Card?.Type?.ToString()
            );
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<IReadOnlyList<VaultTokenResult>> ListVaultedTokensAsync(
        string customerId, CancellationToken ct = default)
    {
        try
        {
            var allTokens = new List<VaultTokenResult>();
            int page = 1;
            CustomerVaultPaymentTokensResponse? resp = null;
            do
            {
                var apiResp = await _client.VaultController.ListCustomerPaymentTokensAsync(
                    new ListCustomerPaymentTokensInput(customerId, pageSize: 100, page: page, totalRequired: true),
                    ct);
                resp = apiResp.Data;
                if (resp?.PaymentTokens is { Count: > 0 } tokens)
                {
                    foreach (var token in tokens)
                    {
                        allTokens.Add(new VaultTokenResult(
                            TokenId: token.Id ?? "",
                            Last4: token.PaymentSource?.Card?.LastDigits,
                            Brand: token.PaymentSource?.Card?.Brand?.ToString(),
                            Expiry: token.PaymentSource?.Card?.Expiry,
                            CardType: token.PaymentSource?.Card?.Type?.ToString()
                        ));
                    }
                }
                page++;
            } while (resp != null && page <= (resp.TotalPages ?? 1));

            return allTokens;
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task DeleteVaultedTokenAsync(string tokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.VaultController.DeletePaymentTokenAsync(tokenId, ct);
        }
        catch (PayPalPaymentException) { throw; }
        catch (ErrorException ex)
        {
            if (ex.HttpContext?.Response?.StatusCode == 404) return; // already deleted
            throw new PayPalPaymentException(BuildErrorMessage(ex), ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            if (ex.HttpContext?.Response?.StatusCode == 404) return;
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    public async Task<IReadOnlyList<TransactionSearchResult>> SearchTransactionsAsync(
        string startDate, string endDate, CancellationToken ct = default)
    {
        try
        {
            var all = new List<TransactionSearchResult>();
            int page = 1;
            SearchResponse? resp = null;
            do
            {
                var apiResp = await _client.TransactionSearchController.SearchTransactionsAsync(
                    new SearchTransactionsInput
                    {
                        StartDate = startDate,
                        EndDate = endDate,
                        Fields = "transaction_info",
                        PageSize = 100,
                        Page = page,
                    }, ct);
                resp = apiResp.Data;
                if (resp?.TransactionDetails is { Count: > 0 } details)
                {
                    foreach (var td in details)
                    {
                        all.Add(new TransactionSearchResult(
                            TransactionId: td.TransactionInfo?.TransactionId ?? "",
                            Status: td.TransactionInfo?.TransactionStatus,
                            Amount: td.TransactionInfo?.TransactionAmount?.MValue,
                            Currency: td.TransactionInfo?.TransactionAmount?.CurrencyCode,
                            InitiationDate: td.TransactionInfo?.TransactionInitiationDate,
                            PayPalReferenceId: td.TransactionInfo?.PaypalReferenceId,
                            ReferenceType: td.TransactionInfo?.PaypalReferenceIdType?.ToString()
                        ));
                    }
                }
                page++;
            } while (resp != null && page <= (resp.TotalPages ?? 1));

            return all;
        }
        catch (PayPalPaymentException) { throw; }
        catch (SearchErrorException ex)
        {
            throw new PayPalPaymentException(
                $"Transaction search failed: {ex.Name ?? ex.Message}",
                ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (ApiException ex)
        {
            throw new PayPalPaymentException(ex.Message, ex.HttpContext?.Response?.StatusCode ?? 502, ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PayPalPaymentException("An unexpected error occurred communicating with PayPal.", 502, ex);
        }
    }

    private static AuthorizationResult ExtractAuthorizationResult(OrderAuthorizeResponse? data)
    {
        var auth = data?.PurchaseUnits?[0]?.Payments?.Authorizations?[0]
            ?? throw new PayPalPaymentException(
                "PayPal authorization not found in response. If a browser challenge is required, this integration cannot proceed.",
                422);

        return new AuthorizationResult(
            AuthorizationId: auth.Id
                ?? throw new PayPalPaymentException("PayPal did not return an authorization ID.", 502),
            Status: auth.Status?.ToString() ?? "",
            ExpiresAt: auth.ExpirationTime
        );
    }

    private static string BuildErrorMessage(ErrorException ex)
    {
        var name = ex.Name ?? "PayPal error";
        var details = ex.Details is { Count: > 0 }
            ? ": " + string.Join("; ", ex.Details.Select(d => d.Issue ?? d.Description ?? "").Where(s => s.Length > 0))
            : "";
        return name + details;
    }
}
