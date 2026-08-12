using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using Order = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalPaymentService : IPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;
    private readonly ILogger<PayPalPaymentService> _logger;

    public PayPalPaymentService(PayPalServerSdkClient client, string currency, ILogger<PayPalPaymentService> logger)
    {
        _client = client;
        _currency = currency;
        _logger = logger;
    }

    public async Task<PaymentAuthorizationResult> AuthorizePaymentAsync(Order order, PaymentDetails payment, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var amount = FormatAmount(order.Total());
            var paymentSource = CreatePaymentSource(payment, order);

            var request = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        ReferenceId = order.Id.ToString(),
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = _currency,
                            Value = amount
                        },
                        Items = order.OrderItems.Select(oi => new ItemRequest
                        {
                            Name = oi.ItemOrdered.ProductName,
                            UnitAmount = new Money
                            {
                                CurrencyCode = _currency,
                                Value = FormatAmount(oi.UnitPrice)
                            },
                            Quantity = oi.Units.ToString()
                        }).ToList()
                    }
                },
                PaymentSource = paymentSource
            };

            var response = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: request,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct
            );

            if (response?.Id == null)
                return new PaymentAuthorizationResult { Success = false, ErrorMessage = "Order creation failed" };

            var authorizeResponse = await _client.Orders.AuthorizeOrder(
                id: response.Id,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest(),
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct
            );

            var authorizationId = ExtractAuthorizationId(authorizeResponse);
            if (string.IsNullOrEmpty(authorizationId))
                return new PaymentAuthorizationResult { Success = false, ErrorMessage = "Authorization ID not found in response" };

            _logger.LogInformation("Payment authorized successfully. OrderId: {OrderId}, AuthorizationId: {AuthorizationId}", response.Id, authorizationId);

            return new PaymentAuthorizationResult
            {
                Success = true,
                OrderId = response.Id,
                AuthorizationId = authorizationId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authorization failed for order {OrderId}", order.Id);
            return new PaymentAuthorizationResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<PaymentCaptureResult> CapturePaymentAsync(PaymentReference paymentRef, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(paymentRef.AuthorizationId))
                return new PaymentCaptureResult { Success = false, ErrorMessage = "Authorization ID is missing" };

            var captureRequest = new CaptureRequest
            {
                Amount = new Money
                {
                    CurrencyCode = paymentRef.Currency ?? _currency,
                    Value = FormatAmount(paymentRef.AuthorizedAmount ?? 0m)
                }
            };

            var response = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: paymentRef.AuthorizationId,
                payPalMockResponse: null,
                payPalRequestId: Guid.NewGuid().ToString(),
                payPalAuthAssertion: null,
                body: captureRequest,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct
            );

            var fee = ParseDecimal(response.SellerReceivableBreakdown?.PaypalFee?.Value);
            var capturedAmount = ParseDecimal(response.Amount?.Value);

            _logger.LogInformation("Payment captured successfully. CaptureId: {CaptureId}, Amount: {Amount}, Fee: {Fee}",
                response.Id, capturedAmount, fee);

            return new PaymentCaptureResult
            {
                Success = true,
                CaptureId = response.Id,
                CapturedAmount = capturedAmount,
                PaypalFee = fee
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Capture failed for authorization {AuthorizationId}", paymentRef.AuthorizationId);
            return new PaymentCaptureResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<PaymentVoidResult> VoidPaymentAsync(PaymentReference paymentRef, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(paymentRef.AuthorizationId))
                return new PaymentVoidResult { Success = false, ErrorMessage = "Authorization ID is missing" };

            await _client.Payments.VoidPayment(
                authorizationId: paymentRef.AuthorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: Guid.NewGuid().ToString(),
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct
            );

            _logger.LogInformation("Payment voided successfully. AuthorizationId: {AuthorizationId}", paymentRef.AuthorizationId);

            return new PaymentVoidResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Void failed for authorization {AuthorizationId}", paymentRef.AuthorizationId);
            return new PaymentVoidResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<PaymentRefundResult> RefundPaymentAsync(PaymentReference paymentRef, decimal refundAmount, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrEmpty(paymentRef.CaptureId))
                return new PaymentRefundResult { Success = false, ErrorMessage = "Capture ID is missing" };

            RefundRequest refundRequest = null;

            if (refundAmount < paymentRef.CapturedAmount)
            {
                refundRequest = new RefundRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = paymentRef.Currency ?? _currency,
                        Value = FormatAmount(refundAmount)
                    }
                };
            }

            var response = await _client.Payments.RefundCapturedPayment(
                captureId: paymentRef.CaptureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: refundRequest,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct
            );

            _logger.LogInformation("Payment refunded successfully. RefundId: {RefundId}, Amount: {Amount}",
                response.Id, response.Amount?.Value);

            return new PaymentRefundResult
            {
                Success = true,
                RefundId = response.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund failed for capture {CaptureId}", paymentRef.CaptureId);
            return new PaymentRefundResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<SavedCardDetails> SaveCardAsync(string buyerId, string cardToken, string? cardholderName, CancellationToken ct = default)
    {
        try
        {
            var cardNumber = cardToken.Replace(" ", "").Replace("-", "");

            var request = new PaymentTokenRequest
            {
                Customer = new Customer { Id = buyerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Number = cardNumber,
                        Name = cardholderName
                    }
                }
            };

            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: request,
                requestOptions: null,
                ct: ct
            );

            _logger.LogInformation("Card saved successfully. PaymentTokenId: {TokenId}", response.Id);

            return new SavedCardDetails
            {
                Id = response.Id ?? string.Empty,
                LastFourDigits = response.PaymentSource?.Card?.LastDigits,
                Brand = response.PaymentSource?.Card?.Brand?.ToString(),
                CardholderName = response.PaymentSource?.Card?.Name,
                ExpiryDate = response.PaymentSource?.Card?.Expiry
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Card save failed for buyer {BuyerId}", buyerId);
            throw;
        }
    }

    public async Task DeleteSavedCardAsync(string payPalPaymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: payPalPaymentTokenId,
                requestOptions: null,
                ct: ct
            );
            _logger.LogInformation("Card deleted successfully. PaymentTokenId: {TokenId}", payPalPaymentTokenId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Card delete failed for token {TokenId}", payPalPaymentTokenId);
            throw;
        }
    }

    public async Task<IReadOnlyList<SavedCardDetails>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: buyerId,
                pageSize: 100,
                page: 1,
                totalRequired: false,
                requestOptions: null,
                ct: ct
            );

            if (response?.PaymentTokens == null)
                return new List<SavedCardDetails>();

            return response.PaymentTokens
                .Select(token => new SavedCardDetails
                {
                    Id = token.Id ?? string.Empty,
                    LastFourDigits = token.PaymentSource?.Card?.LastDigits,
                    Brand = token.PaymentSource?.Card?.Brand?.ToString(),
                    CardholderName = token.PaymentSource?.Card?.Name,
                    ExpiryDate = token.PaymentSource?.Card?.Expiry
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "List cards failed for buyer {BuyerId}", buyerId);
            throw;
        }
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(DateTime fromDate, DateTime toDate, CancellationToken ct = default)
    {
        try
        {
            var startDate = fromDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDate = toDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

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
                page: 1,
                requestOptions: null,
                ct: ct
            );

            if (response?.TransactionDetails == null)
                return new List<TransactionRecord>();

            return response.TransactionDetails
                .Where(d => d.TransactionInfo != null)
                .Select(d => new TransactionRecord
                {
                    TransactionId = d.TransactionInfo?.TransactionId ?? string.Empty,
                    Status = d.TransactionInfo?.TransactionStatus ?? string.Empty,
                    Amount = ParseDecimal(d.TransactionInfo?.TransactionAmount?.Value),
                    Currency = d.TransactionInfo?.TransactionAmount?.CurrencyCode ?? _currency,
                    CreatedAt = ParseDateTime(d.TransactionInfo?.TransactionInitiationDate) ?? DateTimeOffset.Now,
                    InvoiceId = d.TransactionInfo?.InvoiceId
                })
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction search failed for range {FromDate} to {ToDate}", fromDate, toDate);
            throw;
        }
    }

    private PaymentSource CreatePaymentSource(PaymentDetails payment, Order order)
    {
        if (!string.IsNullOrEmpty(payment.SavedPaymentMethodId))
        {
            return new PaymentSource
            {
                Token = new Token
                {
                    Id = payment.SavedPaymentMethodId,
                    Type = TokenType.BillingAgreement
                }
            };
        }

        if (payment.CardDetails != null)
        {
            return new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = payment.CardDetails.CardNumber.Replace(" ", "").Replace("-", ""),
                    Expiry = payment.CardDetails.Expiry.Replace("/", "-"),
                    SecurityCode = payment.CardDetails.Cvv,
                    Name = payment.CardDetails.CardholderName
                }
            };
        }

        throw new InvalidOperationException("Payment source not specified");
    }

    private string? ExtractAuthorizationId(OrderAuthorizeResponse response)
    {
        return response?.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()?.Id;
    }

    private string FormatAmount(decimal amount)
    {
        return amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            return result;

        return 0m;
    }

    private DateTimeOffset? ParseDateTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (DateTime.TryParse(value, out var result))
            return new DateTimeOffset(result);

        return null;
    }
}
