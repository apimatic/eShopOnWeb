using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Payments.Dto;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Coordinates the eShop order state machine with the PayPal payment lifecycle:
/// authorize (hold) at checkout, capture at fulfilment, void on cancel, refund on return.
/// All PayPal interactions go through <see cref="IPayPalClient"/>, which is built against
/// the OpenAPI specifications in api-specs/paypal.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalOptions _options;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IOptions<PayPalOptions> options,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderPayment> AuthorizePaymentAsync(Order order, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (card == null && savedPaymentMethodId == null)
        {
            throw new PaymentException("Payment requires either card details or a saved payment method.");
        }

        var payment = await GetPaymentAsync(order.Id, cancellationToken);

        // Idempotency: an order that already holds funds returns its existing payment.
        if (payment?.Status == OrderPaymentStatus.Authorized || payment?.Status == OrderPaymentStatus.Captured)
        {
            return payment;
        }
        if (order.Status != OrderStatus.PendingPayment)
        {
            throw new OrderStateException($"Order {order.Id} cannot be paid while in state {order.Status}.");
        }

        PayPalPaymentSource paymentSource;
        if (savedPaymentMethodId.HasValue)
        {
            var methods = await _paymentMethodRepository.ListAsync(
                new SavedPaymentMethodsByBuyerIdSpec(order.BuyerId), cancellationToken);
            var method = methods.FirstOrDefault(m => m.Id == savedPaymentMethodId.Value)
                ?? throw new PaymentException($"Saved payment method {savedPaymentMethodId} was not found for this shopper.");
            paymentSource = new PayPalPaymentSource
            {
                Token = new PayPalTokenRequest { Id = method.VaultTokenId }
            };
        }
        else
        {
            paymentSource = new PayPalPaymentSource { Card = MapCard(card!) };
        }

        payment ??= new OrderPayment(order.Id, order.BuyerId, order.Total(), _options.Currency);

        PayPalOrderResponse? createdForAuthorization = null;

        // Reuse the PayPal order when a previous attempt already created one, so a
        // retried payment never produces a second hold.
        if (string.IsNullOrEmpty(payment.PayPalOrderId))
        {
            var orderRequest = new PayPalOrderRequest
            {
                Intent = "AUTHORIZE",
                PurchaseUnits =
                {
                    new PayPalPurchaseUnitRequest
                    {
                        ReferenceId = order.Id.ToString(CultureInfo.InvariantCulture),
                        CustomId = order.Id.ToString(CultureInfo.InvariantCulture),
                        InvoiceId = payment.InvoiceId,
                        Description = $"eShop order {order.Id}",
                        Amount = new PayPalMoney
                        {
                            CurrencyCode = _options.Currency,
                            Value = FormatMoney(payment.Amount)
                        }
                    }
                },
                PaymentSource = paymentSource
            };

            PayPalOrderResponse created;
            try
            {
                // The request id derives from the payment's unique invoice id: stable for
                // retries of this payment, never reused across payments or runs.
                created = await _payPalClient.CreateOrderAsync(orderRequest, $"{payment.InvoiceId}-create", cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                payment.MarkAuthorizationFailed();
                await SavePaymentAsync(payment, cancellationToken);
                throw ToPaymentException(ex, "create the PayPal order");
            }

            if (string.Equals(created.Status, "PAYER_ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorizationFailed();
                await SavePaymentAsync(payment, cancellationToken);
                throw new PaymentException(
                    "PayPal requires the shopper to approve this payment in a browser (PAYER_ACTION_REQUIRED), " +
                    "which this integration does not support.");
            }

            payment.RecordPayPalOrderId(created.Id);
            createdForAuthorization = created;
            await SavePaymentAsync(payment, cancellationToken);
        }

        // When the order is created with a card or vaulted-card payment source, PayPal
        // processes the authorization as part of the create call and returns it on the
        // order. The explicit authorize call is only a fallback for sources that were not
        // processed at create time.
        var authorization = FindAuthorization(createdForAuthorization);

        if (authorization == null)
        {
            authorization = FindAuthorization(
                await _payPalClient.GetOrderAsync(payment.PayPalOrderId!, cancellationToken));
        }

        if (authorization == null)
        {
            PayPalOrderResponse authorized;
            try
            {
                authorized = await _payPalClient.AuthorizeOrderAsync(payment.PayPalOrderId!, $"{payment.InvoiceId}-authorize", cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                payment.MarkAuthorizationFailed();
                await SavePaymentAsync(payment, cancellationToken);
                throw ToPaymentException(ex, "authorize the payment");
            }
            authorization = FindAuthorization(authorized);
        }

        if (authorization == null)
        {
            payment.MarkAuthorizationFailed();
            await SavePaymentAsync(payment, cancellationToken);
            throw new PaymentException("PayPal did not return an authorization for the payment.");
        }

        if (string.Equals(authorization.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkAuthorizationFailed();
            await SavePaymentAsync(payment, cancellationToken);
            throw new PaymentException("PayPal denied the payment authorization.");
        }

        payment.RecordAuthorization(payment.PayPalOrderId!, authorization.Id, authorization.Status ?? "CREATED",
            authorization.ExpirationTime);
        order.MarkPaymentAuthorized();

        await SavePaymentAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} authorized: PayPal authorization {AuthorizationId}",
            order.Id, payment.AuthorizationId);
        return payment;
    }

    public async Task<OrderPayment> CapturePaymentAsync(Order order, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(order.Id, cancellationToken)
            ?? throw new PaymentException($"Order {order.Id} has no payment to capture.");

        // Idempotency: fulfilling an already-fulfilled order returns the existing capture.
        if (payment.Status == OrderPaymentStatus.Captured)
        {
            return payment;
        }
        if (payment.Status != OrderPaymentStatus.Authorized || payment.AuthorizationId == null)
        {
            throw new OrderStateException(
                $"Order {order.Id} cannot be fulfilled: its payment is in state {payment.Status}. Pay the order first.");
        }

        // A hold that has gone stale must be renewed before the money can be taken.
        if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            await ReauthorizeAsync(payment, cancellationToken);
        }

        var captureRequest = new PayPalCaptureRequest
        {
            Amount = new PayPalMoney { CurrencyCode = payment.Currency, Value = FormatMoney(payment.Amount) },
            // No invoice_id here: the capture then reports the authorizing transaction's
            // invoice id, and merchant accounts may reject reused invoice ids.
            FinalCapture = true,
            NoteToPayer = $"eShop order {order.Id}"
        };

        PayPalCapture capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId, captureRequest, $"{payment.InvoiceId}-capture", cancellationToken);
        }
        catch (PayPalApiException ex) when (IsRenewableAuthorizationError(ex))
        {
            // The hold lapsed between our check and the capture: renew once and retry once.
            await ReauthorizeAsync(payment, cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, captureRequest, $"{payment.InvoiceId}-capture-retry", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw ToPaymentException(ex, "capture the payment");
        }

        // The fee breakdown is omitted from the capture response while the capture is
        // still pending; fetch the settled capture record once if it is missing.
        if (capture.SellerReceivableBreakdown?.PayPalFee == null && capture.Id != null)
        {
            try
            {
                var settled = await _payPalClient.GetCaptureAsync(capture.Id, cancellationToken);
                if (settled.SellerReceivableBreakdown != null)
                {
                    capture.SellerReceivableBreakdown = settled.SellerReceivableBreakdown;
                }
                if (!string.IsNullOrEmpty(settled.Status))
                {
                    capture.Status = settled.Status;
                }
            }
            catch (PayPalApiException)
            {
                // The capture itself succeeded; the breakdown simply stays unavailable.
            }
        }

        var breakdown = capture.SellerReceivableBreakdown;
        payment.RecordCapture(
            capture.Id,
            capture.Status ?? "COMPLETED",
            ParseMoney(capture.Amount) ?? payment.Amount,
            ParseMoney(breakdown?.PayPalFee),
            ParseMoney(breakdown?.NetAmount),
            capture.CreateTime ?? DateTimeOffset.UtcNow);
        order.MarkFulfilled();

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} captured: PayPal capture {CaptureId}", order.Id, payment.CaptureId);
        return payment;
    }

    public async Task<OrderPayment?> VoidPaymentAsync(Order order, CancellationToken cancellationToken = default)
    {
        var payment = await GetPaymentAsync(order.Id, cancellationToken);

        if (payment?.Status == OrderPaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            try
            {
                await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId, $"{payment.InvoiceId}-void", cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // The hold is already gone at PayPal; the local void still proceeds so the
                // shopper is never charged.
                _logger.LogWarning("Authorization {AuthorizationId} not found at PayPal while cancelling order {OrderId}",
                    payment.AuthorizationId, order.Id);
            }
            catch (PayPalApiException ex)
            {
                throw ToPaymentException(ex, "release the held funds");
            }
            payment.MarkVoided("VOIDED");
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} cancelled; held funds released.", order.Id);
        return payment;
    }

    public async Task<PaymentRefund> RefundPaymentAsync(Order order, decimal? amount, string idempotencyKey,
        string? noteToPayer, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException("A refund requires a caller-supplied idempotency key.");
        }

        var payment = await GetPaymentAsync(order.Id, cancellationToken)
            ?? throw new PaymentException($"Order {order.Id} has no payment to refund.");

        // Idempotency: a repeated key returns the refund it already produced.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (payment.Status != OrderPaymentStatus.Captured || payment.CaptureId == null)
        {
            throw new OrderStateException(
                $"Order {order.Id} cannot be refunded: its payment is in state {payment.Status}. Only captured payments can be refunded.");
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            throw new OrderStateException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the refundable amount of {payment.RefundableAmount} {payment.Currency} for order {order.Id}.");
        }

        var refundRequest = new PayPalRefundRequest
        {
            // An explicit amount makes a partial refund; for a full refund the amount is
            // still sent so the refunded sum is unambiguous.
            Amount = new PayPalMoney { CurrencyCode = payment.Currency, Value = FormatMoney(refundAmount) },
            CustomId = idempotencyKey,
            NoteToPayer = noteToPayer
        };

        PayPalRefund refund;
        try
        {
            refund = await _payPalClient.RefundCaptureAsync(
                payment.CaptureId, refundRequest, $"{payment.InvoiceId}-refund-{idempotencyKey}", cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw ToPaymentException(ex, "refund the payment");
        }

        var recorded = payment.AddRefund(refund.Id, refund.Status ?? "PENDING", refundAmount, idempotencyKey, noteToPayer);
        order.MarkRefunded(payment.RefundableAmount == 0m);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency}: PayPal refund {RefundId}",
            order.Id, refundAmount, payment.Currency, refund.Id);
        return recorded;
    }

    private static PayPalAuthorization? FindAuthorization(PayPalOrderResponse? order)
        => order?.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<PayPalAuthorization>())
            .OrderByDescending(a => a.CreateTime)
            .FirstOrDefault();

    private async Task ReauthorizeAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _payPalClient.ReauthorizeAuthorizationAsync(
                payment.AuthorizationId!,
                new PayPalReauthorizeRequest
                {
                    Amount = new PayPalMoney { CurrencyCode = payment.Currency, Value = FormatMoney(payment.Amount) }
                },
                $"{payment.InvoiceId}-reauthorize",
                cancellationToken);

            payment.RecordReauthorization(renewed.Id, renewed.Status ?? "CREATED", renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The payment hold for order {payment.OrderId} has expired and PayPal could not renew it " +
                $"({ex.ErrorName ?? ex.StatusCode.ToString()}). Ask the shopper to pay the order again, then fulfil it. " +
                $"PayPal debug id: {ex.DebugId}.", ex);
        }
    }

    private static bool IsRenewableAuthorizationError(PayPalApiException ex)
        => ex.StatusCode == HttpStatusCode.UnprocessableEntity
           || ex.StatusCode == HttpStatusCode.BadRequest;

    private async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
        => await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);

    private async Task SavePaymentAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
    }

    private static PayPalCardRequest MapCard(CardDetails card) => new()
    {
        Number = card.Number,
        Expiry = card.Expiry,
        SecurityCode = card.SecurityCode,
        Name = card.CardholderName,
        BillingAddress = card.BillingCountryCode == null ? null : new PayPalAddress
        {
            AddressLine1 = card.BillingAddressLine1,
            AddressLine2 = card.BillingAddressLine2,
            AdminArea2 = card.BillingCity,
            AdminArea1 = card.BillingState,
            PostalCode = card.BillingPostalCode,
            CountryCode = card.BillingCountryCode
        }
    };

    private static PaymentException ToPaymentException(PayPalApiException ex, string action) => new(
        $"PayPal could not {action}: {ex.Message} " +
        $"(error {ex.ErrorName ?? ex.StatusCode.ToString()}, debug id {ex.DebugId}).", ex);

    internal static string FormatMoney(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    internal static decimal? ParseMoney(PayPalMoney? money)
        => money != null && decimal.TryParse(money.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
