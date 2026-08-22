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

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PayPalGateway : IPayPalPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private readonly PayPalServerSdkClient _client;

    public PayPalGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<PayPalAuthorizationResult> AuthorizeCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CardPaymentDetails card,
        CancellationToken cancellationToken)
    {
        var body = BuildAuthorizeRequest(orderId, amount, currency, BuildCardRequest(card));
        return AuthorizeAsync(body, payPalRequestId, amount, currency, cancellationToken);
    }

    public Task<PayPalAuthorizationResult> AuthorizeVaultedCardAsync(
        int orderId,
        decimal amount,
        string currency,
        string payPalRequestId,
        string vaultId,
        CancellationToken cancellationToken)
    {
        var card = new CardRequest
        {
            VaultId = vaultId,
            StoredCredential = new CardStoredCredential
            {
                PaymentInitiator = PaymentInitiator.Customer,
                PaymentType = StoredPaymentSourcePaymentType.OneTime,
                Usage = StoredPaymentSourceUsageType.Subsequent
            }
        };
        var body = BuildAuthorizeRequest(orderId, amount, currency, card);
        return AuthorizeAsync(body, payPalRequestId, amount, currency, cancellationToken);
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(
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
                    ct: ct),
                cancellationToken);

            return MapAuthorizationDetails(authorization);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw PayPalExceptionMapper.FromGetAuthorizedPayment(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<PayPalAuthorizationDetails> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money
            {
                CurrencyCode = currency,
                Value = MoneyFormatter.ToPayPalValue(amount)
            }
        };

        try
        {
            var authorization = await Bounded(
                ct => _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct),
                cancellationToken);

            return MapAuthorizationDetails(authorization);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw PayPalExceptionMapper.FromReauthorizePayment(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        var body = new CaptureRequest
        {
            FinalCapture = true
        };

        try
        {
            var capture = await Bounded(
                ct => _client.Payments.CaptureAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct),
                cancellationToken);

            return MapCapture(capture);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw PayPalExceptionMapper.FromCaptureAuthorizedPayment(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<PayPalCaptureResult> GetCaptureAsync(
        string captureId,
        CancellationToken cancellationToken)
    {
        try
        {
            var capture = await Bounded(
                ct => _client.Payments.GetCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    ct: ct),
                cancellationToken);

            return MapCapture(capture);
        }
        catch (SdkException<GetCapturedPaymentError> ex)
        {
            throw PayPalExceptionMapper.FromGetCapturedPayment(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task VoidAsync(
        string authorizationId,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(
                ct => _client.Payments.VoidPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    payPalRequestId: payPalRequestId,
                    prefer: "return=representation",
                    ct: ct),
                cancellationToken);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw PayPalExceptionMapper.FromVoidPayment(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string? currency,
        string payPalRequestId,
        CancellationToken cancellationToken)
    {
        RefundRequest? body = null;
        if (amount.HasValue)
        {
            if (string.IsNullOrWhiteSpace(currency))
            {
                throw new ArgumentException("Currency is required for a partial refund.", nameof(currency));
            }

            body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = MoneyFormatter.ToPayPalValue(amount.Value)
                }
            };
        }

        try
        {
            var refund = await Bounded(
                ct => _client.Payments.RefundCapturedPayment(
                    captureId: captureId,
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct),
                cancellationToken);

            return MapRefund(refund);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw PayPalExceptionMapper.FromRefundCapturedPayment(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<PayPalVaultedCard> SaveCardAsync(
        string merchantCustomerId,
        string? payPalCustomerId,
        string payPalRequestId,
        CardPaymentDetails card,
        CancellationToken cancellationToken)
    {
        var customer = new Customer
        {
            MerchantCustomerId = merchantCustomerId
        };
        if (!string.IsNullOrWhiteSpace(payPalCustomerId))
        {
            customer = new Customer
            {
                Id = payPalCustomerId,
                MerchantCustomerId = merchantCustomerId
            };
        }

        var body = new PaymentTokenRequest
        {
            Customer = customer,
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = SanitizePan(card.Number),
                    Expiry = NormalizeExpiry(card.Expiry),
                    SecurityCode = card.SecurityCode,
                    BillingAddress = ToPayPalAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await Bounded(
                ct => _client.Vault.CreatePaymentToken(
                    payPalRequestId: payPalRequestId,
                    body: body,
                    ct: ct),
                cancellationToken);

            return MapVaultedCard(token);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw PayPalExceptionMapper.FromCreatePaymentToken(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalVaultedCard>> ListCardsAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ListCardsForCustomerIdAsync(customerId, cancellationToken);
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            throw PayPalExceptionMapper.FromListCustomerPaymentTokens(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task DeleteCardAsync(string paymentTokenId, CancellationToken cancellationToken)
    {
        try
        {
            await Bounded(
                ct => _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: ct),
                cancellationToken);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw PayPalExceptionMapper.FromDeletePaymentToken(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<PayPalTransactionRecord>();

        try
        {
            foreach (var (start, end) in SplitIntoWindows(from, to, TimeSpan.FromDays(31)))
            {
                var page = 1;
                int totalPages;
                do
                {
                    var response = await Bounded(
                        ct => _client.TransactionSearch.SearchTransactions(
                            startDate: ToPayPalTimestamp(start),
                            endDate: ToPayPalTimestamp(end),
                            transactionId: null,
                            transactionType: null,
                            transactionStatus: null,
                            transactionAmount: null,
                            transactionCurrency: null,
                            paymentInstrumentType: null,
                            storeId: null,
                            terminalId: null,
                            fields: "all",
                            balanceAffectingRecordsOnly: "N",
                            pageSize: 100,
                            page: page,
                            ct: ct),
                        cancellationToken);

                    if (response.TransactionDetails is not null)
                    {
                        results.AddRange(response.TransactionDetails.Select(MapTransaction));
                    }

                    totalPages = response.TotalPages ?? page;
                    page++;
                } while (page <= totalPages);
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw PayPalExceptionMapper.FromSearchTransactions(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }

        return results;
    }

    private async Task<PayPalAuthorizationResult> AuthorizeAsync(
        OrderRequest body,
        string payPalRequestId,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        try
        {
            var order = await Bounded(
                ct => _client.Orders.CreateOrder(
                    payPalMockResponse: null,
                    payPalRequestId: payPalRequestId,
                    payPalPartnerAttributionId: null,
                    payPalClientMetadataId: null,
                    payPalAuthAssertion: null,
                    body: body,
                    prefer: "return=representation",
                    ct: ct),
                cancellationToken);

            if (order.Status == OrderStatus.PayerActionRequired)
            {
                throw new PaymentException(
                    "This card requires shopper approval in a browser, which is not supported. Use a card that completes without a 3-D Secure challenge.");
            }

            var authorization = FirstAuthorization(order);
            if (authorization is null && !string.IsNullOrEmpty(order.Id))
            {
                order = await Bounded(
                    ct => _client.Orders.GetOrder(
                        id: order.Id,
                        fields: null,
                        payPalMockResponse: null,
                        payPalAuthAssertion: null,
                        ct: ct),
                    cancellationToken);

                if (order.Status == OrderStatus.PayerActionRequired)
                {
                    throw new PaymentException(
                        "This card requires shopper approval in a browser, which is not supported. Use a card that completes without a 3-D Secure challenge.");
                }

                authorization = FirstAuthorization(order);
            }

            if (authorization is null || string.IsNullOrEmpty(authorization.Id) || string.IsNullOrEmpty(order.Id))
            {
                throw new PaymentException(
                    "PayPal authorized the order but did not return an authorization id. The hold cannot be captured later.");
            }

            return new PayPalAuthorizationResult
            {
                PayPalOrderId = order.Id,
                PayPalOrderStatus = order.Status?.Value,
                AuthorizationId = authorization.Id,
                AuthorizationStatus = authorization.Status?.Value,
                ExpirationTime = ParseTimestamp(authorization.ExpirationTime),
                AuthorizedAmount = authorization.Amount is null
                    ? amount
                    : MoneyFormatter.FromPayPalValue(authorization.Amount.Value),
                Currency = authorization.Amount?.CurrencyCode ?? currency
            };
        }
        catch (PaymentException)
        {
            throw;
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw PayPalExceptionMapper.FromCreateOrder(ex);
        }
        catch (SdkException<GetOrderError> ex)
        {
            throw PayPalExceptionMapper.FromGetOrder(ex);
        }
        catch (JsonException ex)
        {
            throw PayPalExceptionMapper.FromJson(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw PayPalExceptionMapper.Unreachable(ex);
        }
    }

    private async Task<IReadOnlyList<PayPalVaultedCard>> ListCardsForCustomerIdAsync(
        string customerId,
        CancellationToken cancellationToken)
    {
        var cards = new List<PayPalVaultedCard>();
        var page = 1;
        int totalPages;
        do
        {
            var response = await Bounded(
                ct => _client.Vault.ListCustomerPaymentTokens(
                    customerId: customerId,
                    pageSize: 20,
                    page: page,
                    totalRequired: true,
                    ct: ct),
                cancellationToken);

            if (response.PaymentTokens is not null)
            {
                cards.AddRange(response.PaymentTokens.Select(MapVaultedCard));
            }

            totalPages = response.TotalPages ?? page;
            page++;
        } while (page <= totalPages);

        return cards;
    }

    private static OrderRequest BuildAuthorizeRequest(
        int orderId,
        decimal amount,
        string currency,
        CardRequest card)
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
                        Value = MoneyFormatter.ToPayPalValue(amount)
                    },
                    InvoiceId = $"ESHOP-{orderId}",
                    CustomId = orderId.ToString(CultureInfo.InvariantCulture)
                }
            },
            PaymentSource = new PaymentSource
            {
                Card = card
            }
        };
    }

    private static CardRequest BuildCardRequest(CardPaymentDetails card)
    {
        return new CardRequest
        {
            Name = card.Name,
            Number = SanitizePan(card.Number),
            Expiry = NormalizeExpiry(card.Expiry),
            SecurityCode = card.SecurityCode,
            BillingAddress = ToPayPalAddress(card.BillingAddress) ?? new PayPalAddress
            {
                AddressLine1 = "123 Main St.",
                AdminArea2 = "Kent",
                AdminArea1 = "OH",
                PostalCode = "44240",
                CountryCode = "US"
            },
            Attributes = new CardAttributes
            {
                Verification = new CardVerification
                {
                    Method = OrdersCardVerificationMethod.AvsCvv
                }
            }
        };
    }

    private static AuthorizationWithAdditionalData? FirstAuthorization(Order order)
    {
        return order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Array.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();
    }

    private static PayPalAuthorizationDetails MapAuthorizationDetails(PaymentAuthorization authorization)
    {
        if (string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentException("PayPal did not return an authorization id.");
        }

        return new PayPalAuthorizationDetails
        {
            AuthorizationId = authorization.Id,
            Status = authorization.Status?.Value,
            ExpirationTime = ParseTimestamp(authorization.ExpirationTime),
            Amount = authorization.Amount is null ? null : MoneyFormatter.FromPayPalValue(authorization.Amount.Value)
        };
    }

    private static PayPalCaptureResult MapCapture(CapturedPayment capture)
    {
        if (string.IsNullOrEmpty(capture.Id))
        {
            throw new PaymentException("PayPal did not return a capture id.");
        }

        var breakdown = capture.SellerReceivableBreakdown;
        return new PayPalCaptureResult
        {
            CaptureId = capture.Id,
            Status = capture.Status?.Value,
            CapturedAmount = capture.Amount is null
                ? MoneyFormatter.FromPayPalValue(breakdown?.GrossAmount.Value)
                : MoneyFormatter.FromPayPalValue(capture.Amount.Value),
            PaypalFee = breakdown?.PaypalFee is null ? null : MoneyFormatter.FromPayPalValue(breakdown.PaypalFee.Value),
            NetAmount = breakdown?.NetAmount is null ? null : MoneyFormatter.FromPayPalValue(breakdown.NetAmount.Value),
            Currency = capture.Amount?.CurrencyCode
                ?? breakdown?.GrossAmount.CurrencyCode
                ?? string.Empty
        };
    }

    private static PayPalRefundResult MapRefund(Refund refund)
    {
        if (string.IsNullOrEmpty(refund.Id))
        {
            throw new PaymentException("PayPal did not return a refund id.");
        }

        return new PayPalRefundResult
        {
            RefundId = refund.Id,
            Status = refund.Status?.Value,
            Amount = refund.Amount is null ? 0m : MoneyFormatter.FromPayPalValue(refund.Amount.Value),
            TotalRefundedAmount = refund.SellerPayableBreakdown?.TotalRefundedAmount is null
                ? null
                : MoneyFormatter.FromPayPalValue(refund.SellerPayableBreakdown.TotalRefundedAmount.Value)
        };
    }

    private static PayPalVaultedCard MapVaultedCard(PaymentTokenResponse token)
    {
        if (string.IsNullOrEmpty(token.Id))
        {
            throw new PaymentException("PayPal did not return a payment token id for the saved card.");
        }

        var card = token.PaymentSource?.Card;
        return new PayPalVaultedCard
        {
            PaymentTokenId = token.Id,
            PayPalCustomerId = token.Customer?.Id,
            MerchantCustomerId = token.Customer?.MerchantCustomerId,
            LastDigits = card?.LastDigits,
            Brand = card?.Brand?.Value,
            Expiry = card?.Expiry,
            Name = card?.Name
        };
    }

    private static PayPalTransactionRecord MapTransaction(TransactionDetails details)
    {
        var info = details.TransactionInfo;
        return new PayPalTransactionRecord
        {
            TransactionId = info?.TransactionId,
            InvoiceId = info?.InvoiceId,
            CustomField = info?.CustomField,
            PaypalReferenceId = info?.PaypalReferenceId,
            Status = info?.TransactionStatus,
            Amount = info?.TransactionAmount?.Value,
            FeeAmount = info?.FeeAmount?.Value,
            Currency = info?.TransactionAmount?.CurrencyCode,
            InitiationDate = ParseTimestamp(info?.TransactionInitiationDate)
        };
    }

    private static PayPalAddress? ToPayPalAddress(CardBillingAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return new PayPalAddress
        {
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.AdminArea2,
            AdminArea1 = address.AdminArea1,
            PostalCode = address.PostalCode,
            CountryCode = address.CountryCode
        };
    }

    private static string SanitizePan(string number)
    {
        return new string(number.Where(char.IsDigit).ToArray());
    }

    private static string NormalizeExpiry(string expiry)
    {
        var trimmed = expiry.Trim();
        if (trimmed.Length == 7 && trimmed[4] == '-')
        {
            return trimmed;
        }

        var parts = trimmed.Split('/', '-', ' ');
        if (parts.Length == 2
            && int.TryParse(parts[0], out var month)
            && int.TryParse(parts[1], out var year))
        {
            if (year < 100)
            {
                year += 2000;
            }

            return $"{year:D4}-{month:D2}";
        }

        return trimmed;
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
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

    private static string ToPayPalTimestamp(DateTimeOffset value)
    {
        return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> SplitIntoWindows(
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan maxWindow)
    {
        var cursor = from;
        var max = maxWindow.Subtract(TimeSpan.FromSeconds(1));
        while (cursor < to)
        {
            var end = cursor + max;
            if (end > to)
            {
                end = to;
            }

            yield return (cursor, end);
            cursor = end.AddSeconds(1);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }
}
