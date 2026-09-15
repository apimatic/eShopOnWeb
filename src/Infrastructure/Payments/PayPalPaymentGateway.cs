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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalAddress = PayPalServerSdk.Models.Address;
using PayPalMoneyModel = PayPalServerSdk.Models.Money;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const string PreferRepresentation = "return=representation";

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            var cardRequest = ToCardRequest(request);
            var body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new()
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = request.Currency,
                            Value = PayPalMoney.Format(request.Amount, request.Currency)
                        },
                        CustomId = request.CustomId,
                        InvoiceId = request.InvoiceId,
                        Description = $"eShopOnWeb order {request.InvoiceId}"
                    }
                },
                PaymentSource = new PaymentSource
                {
                    Card = cardRequest
                }
            };

            PayPalServerSdk.Models.Order created;
            try
            {
                created = await _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: request.IdempotencyKey,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<CreateOrderError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }

            if (created.Status == OrderStatus.PayerActionRequired)
            {
                return PayerAction(created.Id ?? string.Empty, created.Status?.Value);
            }

            var existing = FirstAuthorization(created.PurchaseUnits);
            if (existing is not null && !string.IsNullOrEmpty(existing.Id))
            {
                return new AuthorizationResult
                {
                    PayPalOrderId = created.Id ?? string.Empty,
                    PayPalOrderStatus = created.Status?.Value,
                    AuthorizationId = existing.Id,
                    AuthorizationStatus = existing.Status?.Value,
                    Expiration = ParseTime(existing.ExpirationTime),
                    PayerActionRequired = false
                };
            }

            PayPalServerSdk.Models.OrderAuthorizeResponse authorized;
            try
            {
                using (PayPalWriteGuard.Begin())
                {
                    authorized = await _client.Orders.AuthorizeOrder(
                        id: created.Id ?? throw new PaymentException("PayPal did not return an order id.", 502),
                        payPalMockResponse: null,
                        payPalRequestId: request.IdempotencyKey + "-auth",
                        payPalClientMetadataId: null,
                        payPalAuthAssertion: null,
                        body: null,
                        prefer: PreferRepresentation,
                        requestOptions: null,
                        ct: ct);
                }
            }
            catch (SdkException<AuthorizeOrderError> ex)
            {
                var recovered = await GetAuthorizationFromOrder(
                    created.Id ?? throw new PaymentException("PayPal did not return an order id.", 502),
                    ct);
                if (recovered is not null && !string.IsNullOrEmpty(recovered.Id))
                {
                    return new AuthorizationResult
                    {
                        PayPalOrderId = created.Id ?? string.Empty,
                        PayPalOrderStatus = created.Status?.Value,
                        AuthorizationId = recovered.Id,
                        AuthorizationStatus = recovered.Status?.Value,
                        Expiration = ParseTime(recovered.ExpirationTime),
                        PayerActionRequired = false
                    };
                }

                PayPalGatewayErrors.Throw(ex);
                throw;
            }

            if (authorized.Status == OrderStatus.PayerActionRequired)
            {
                return PayerAction(authorized.Id ?? created.Id ?? string.Empty, authorized.Status?.Value);
            }

            var auth = FirstAuthorization(authorized.PurchaseUnits);
            if (auth is null && !string.IsNullOrEmpty(authorized.Id ?? created.Id))
            {
                auth = await GetAuthorizationFromOrder(authorized.Id ?? created.Id!, ct);
            }

            if (auth is null || string.IsNullOrEmpty(auth.Id))
            {
                throw new PaymentException("PayPal authorized the order but did not return an authorization id.", 502);
            }

            return new AuthorizationResult
            {
                PayPalOrderId = authorized.Id ?? created.Id ?? string.Empty,
                PayPalOrderStatus = authorized.Status?.Value ?? created.Status?.Value,
                AuthorizationId = auth.Id,
                AuthorizationStatus = auth.Status?.Value,
                Expiration = ParseTime(auth.ExpirationTime),
                PayerActionRequired = false
            };
        }, cancellationToken);
    }

    public Task<PaymentAuthorizationSnapshot> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        return CallRead(async ct =>
        {
            try
            {
                var auth = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<GetAuthorizedPaymentError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task<PaymentAuthorizationSnapshot> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            try
            {
                var auth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new ReauthorizeRequest
                    {
                        Amount = new PayPalMoneyModel
                        {
                            CurrencyCode = currency,
                            Value = PayPalMoney.Format(amount, currency)
                        }
                    },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return ToSnapshot(auth);
            }
            catch (SdkException<ReauthorizePaymentError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            try
            {
                await _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: idempotencyKey,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return 0;
            }
            catch (SdkException<VoidPaymentError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task<CaptureResult> CaptureAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            try
            {
                var captured = await _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: new CaptureRequest { FinalCapture = true },
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);
                return ToCaptureResult(captured);
            }
            catch (SdkException<CaptureAuthorizedPaymentError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task<CaptureResult> GetCaptureAsync(string captureId, CancellationToken cancellationToken)
    {
        return CallRead(async ct =>
        {
            try
            {
                var captured = await _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    requestOptions: null,
                    ct: ct);
                return ToCaptureResult(captured);
            }
            catch (SdkException<GetCapturedPaymentError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task<RefundResult> RefundCaptureAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            RefundRequest? body = null;
            if (amount is decimal refundAmount)
            {
                body = new RefundRequest
                {
                    Amount = new PayPalMoneyModel
                    {
                        CurrencyCode = currency,
                        Value = PayPalMoney.Format(refundAmount, currency)
                    }
                };
            }

            try
            {
                var refund = await _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: idempotencyKey,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: PreferRepresentation,
                    requestOptions: null,
                    ct: ct);

                return new RefundResult
                {
                    RefundId = refund.Id ?? throw new PaymentException("PayPal did not return a refund id.", 502),
                    Status = refund.Status?.Value,
                    Amount = PayPalMoney.Parse(refund.Amount?.Value)
                };
            }
            catch (SdkException<RefundCapturedPaymentError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task<VaultedCardResult> VaultCardAsync(
        CardDetails card,
        string merchantCustomerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            var body = new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                PaymentSource = new PaymentTokenRequestPaymentSource
                {
                    Card = new PaymentTokenRequestCard
                    {
                        Name = card.Name,
                        Number = card.Number,
                        Expiry = card.Expiry,
                        SecurityCode = card.SecurityCode,
                        BillingAddress = ToPayPalAddress(card.BillingAddress)
                    }
                }
            };

            try
            {
                var token = await _client.Vault.CreatePaymentToken(
                    payPalRequestId: idempotencyKey,
                    body: body,
                    requestOptions: null,
                    ct: ct);

                var cardEntity = token.PaymentSource?.Card;
                return new VaultedCardResult
                {
                    PaymentTokenId = token.Id ?? throw new PaymentException("PayPal did not return a payment token id.", 502),
                    PayPalCustomerId = token.Customer?.Id,
                    MerchantCustomerId = token.Customer?.MerchantCustomerId ?? merchantCustomerId,
                    LastDigits = cardEntity?.LastDigits,
                    Brand = cardEntity?.Brand?.Value,
                    Expiry = cardEntity?.Expiry,
                    CardholderName = cardEntity?.Name
                };
            }
            catch (SdkException<CreatePaymentTokenError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        return CallWrite(async ct =>
        {
            try
            {
                await _client.Vault.DeletePaymentToken(id: paymentTokenId, requestOptions: null, ct: ct);
                return 0;
            }
            catch (SdkException<DeletePaymentTokenError> ex)
            {
                PayPalGatewayErrors.Throw(ex);
                throw;
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ProcessorTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return CallRead(async ct =>
        {
            var results = new List<ProcessorTransaction>();
            var cursor = from;
            do
            {
                var chunkEnd = cursor.AddDays(31);
                if (chunkEnd > to || chunkEnd <= cursor)
                {
                    chunkEnd = to;
                }

                await AddWindow(results, cursor, chunkEnd, ct);
                if (chunkEnd >= to)
                {
                    break;
                }

                cursor = chunkEnd;
            } while (true);

            return (IReadOnlyList<ProcessorTransaction>)results;
        }, cancellationToken);
    }

    private async Task AddWindow(
        List<ProcessorTransaction> results,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken ct)
    {
        var page = 1;
        var totalPages = 1;
        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: Rfc3339(start),
                    endDate: Rfc3339(end),
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
                PayPalGatewayErrors.Throw(ex);
                throw;
            }

            if (response.TransactionDetails is not null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    results.Add(new ProcessorTransaction
                    {
                        TransactionId = info?.TransactionId,
                        PaypalReferenceId = info?.PaypalReferenceId,
                        InvoiceId = info?.InvoiceId,
                        CustomField = info?.CustomField,
                        Status = info?.TransactionStatus,
                        AmountValue = info?.TransactionAmount?.Value,
                        AmountCurrency = info?.TransactionAmount?.CurrencyCode,
                        FeeValue = info?.FeeAmount?.Value,
                        InitiationDate = ParseTime(info?.TransactionInitiationDate)
                    });
                }
            }

            totalPages = response.TotalPages ?? 1;
            page++;
        } while (page <= totalPages);
    }

    private async Task<AuthorizationWithAdditionalData?> GetAuthorizationFromOrder(string orderId, CancellationToken ct)
    {
        try
        {
            var order = await _client.Orders.GetOrder(
                id: orderId,
                fields: null,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);
            return FirstAuthorization(order.PurchaseUnits);
        }
        catch (SdkException<GetOrderError> ex)
        {
            PayPalGatewayErrors.Throw(ex);
            throw;
        }
    }

    private static AuthorizationResult PayerAction(string orderId, string? status) =>
        new()
        {
            PayPalOrderId = orderId,
            PayPalOrderStatus = status,
            AuthorizationId = string.Empty,
            PayerActionRequired = true
        };

    private static CardRequest ToCardRequest(AuthorizePaymentRequest request)
    {
        if (!string.IsNullOrEmpty(request.VaultId))
        {
            return new CardRequest { VaultId = request.VaultId };
        }

        var card = request.Card ?? throw new PaymentException("Card details are required when not using a saved card.", 400);
        return new CardRequest
        {
            Name = card.Name,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress)
        };
    }

    private static PayPalAddress? ToPayPalAddress(CardBillingAddress? billing)
    {
        if (billing is null)
        {
            return null;
        }

        return new PayPalAddress
        {
            AddressLine1 = billing.AddressLine1,
            AddressLine2 = billing.AddressLine2,
            AdminArea1 = billing.AdminArea1,
            AdminArea2 = billing.AdminArea2,
            PostalCode = billing.PostalCode,
            CountryCode = billing.CountryCode
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(IReadOnlyList<PurchaseUnit>? units)
    {
        return units?
            .SelectMany(unit => unit.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault(auth => !string.IsNullOrEmpty(auth.Id));
    }

    private static PaymentAuthorizationSnapshot ToSnapshot(PaymentAuthorization auth)
    {
        return new PaymentAuthorizationSnapshot
        {
            AuthorizationId = auth.Id ?? throw new PaymentException("PayPal did not return an authorization id.", 502),
            Status = auth.Status?.Value,
            Expiration = ParseTime(auth.ExpirationTime)
        };
    }

    private static CaptureResult ToCaptureResult(CapturedPayment captured)
    {
        var breakdown = captured.SellerReceivableBreakdown;
        var amount = PayPalMoney.Parse(captured.Amount?.Value ?? breakdown?.GrossAmount?.Value);
        return new CaptureResult
        {
            CaptureId = captured.Id ?? throw new PaymentException("PayPal did not return a capture id.", 502),
            Status = captured.Status?.Value,
            CapturedAmount = amount,
            PaypalFee = breakdown?.PaypalFee is null ? null : PayPalMoney.Parse(breakdown.PaypalFee.Value),
            NetAmount = breakdown?.NetAmount is null ? null : PayPalMoney.Parse(breakdown.NetAmount.Value),
            IsPending = captured.Status == CaptureStatus.Pending
        };
    }

    private static DateTimeOffset? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string Rfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static async Task<T> CallWrite<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var writeScope = PayPalWriteGuard.Begin();
        return await Bounded(operation, cancellationToken);
    }

    private static Task<T> CallRead<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        Bounded(operation, cancellationToken);

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        try
        {
            return await operation(cts.Token);
        }
        catch (PaymentException)
        {
            throw;
        }
        catch (PayPalDuplicateSendException ex)
        {
            throw PayPalGatewayErrors.DuplicateWrite(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalGatewayErrors.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw PayPalGatewayErrors.Unreachable(ex);
        }
    }
}
