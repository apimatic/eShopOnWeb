using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly string _currency;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency!;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line item.");
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            // Price comes from the catalog, never from the caller.
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} placed for {buyerId}, total {order.Total():0.00} {_currency}, awaiting payment.");
        return order;
    }

    public async Task AuthorizeOrderAsync(Order order, PaymentInstruction instruction, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        Guard.Against.Null(instruction, nameof(instruction));

        // Idempotent in effect: a double-click on an already-authorized order does nothing.
        if (order.Status == OrderStatus.Authorized && order.Payment?.AuthorizationId is not null)
        {
            _logger.LogInformation($"Order {order.Id} is already authorized ({order.Payment.AuthorizationId}); skipping.");
            return;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {order.Id} cannot be authorized because it is {order.Status}.");
        }

        var amount = new Money(order.Total(), _currency);
        // Stable key: even truly-concurrent double submits are de-duplicated by PayPal into one hold.
        var idempotencyKey = $"eshop-order-{order.Id}-authorize";

        CardAuthorizationResult result;
        if (instruction.UsesSavedCard)
        {
            var paymentMethod = await _paymentMethodRepository.GetByIdAsync(instruction.SavedPaymentMethodId!.Value, cancellationToken);
            if (paymentMethod is null || paymentMethod.BuyerId != order.BuyerId)
            {
                // Do not reveal whether someone else's saved card exists.
                throw new EntityNotFoundException($"Saved card {instruction.SavedPaymentMethodId} was not found.");
            }
            result = await _gateway.AuthorizeWithVaultedCardAsync(amount, paymentMethod.PayPalVaultId, idempotencyKey, cancellationToken);
        }
        else
        {
            if (instruction.Card is null)
            {
                throw new PaymentException("Either card details or a saved card id must be supplied to pay.");
            }
            result = await _gateway.AuthorizeWithCardAsync(amount, instruction.Card, idempotencyKey, cancellationToken);
        }

        var payment = new Payment(result.PayPalOrderId, _currency, amount.Amount, result.CardBrand, result.CardLast4);
        payment.RecordAuthorization(result.AuthorizationId, result.AuthorizationStatus, result.ExpiresAt);
        order.SetAuthorized(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} authorized. Hold {result.AuthorizationId} for {amount.Amount:0.00} {_currency}.");
    }

    public async Task FulfilOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        // Idempotent: already captured -> nothing to do.
        if (order.Status == OrderStatus.Fulfilled && order.Payment?.CaptureId is not null)
        {
            _logger.LogInformation($"Order {order.Id} is already fulfilled ({order.Payment.CaptureId}); skipping.");
            return;
        }
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentException($"Order {order.Id} cannot be fulfilled because it is {order.Status}.");
        }

        var amount = new Money(order.Payment.AuthorizedAmount, order.Payment.Currency);
        var authorizationId = order.Payment.AuthorizationId!;
        // Stable key: a lost response replays the same capture rather than capturing twice.
        var captureKey = $"eshop-order-{order.Id}-capture";

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, amount, captureKey, cancellationToken);
        }
        catch (PaymentGatewayException captureEx)
        {
            // The authorization may have gone stale before fulfilment. Renew it rather than failing outright.
            _logger.LogWarning($"Capture of authorization {authorizationId} for order {order.Id} failed ({captureEx.Message}). Attempting to renew the authorization.");
            ReauthorizationResult reauth;
            try
            {
                reauth = await _gateway.ReauthorizeAsync(authorizationId, amount, cancellationToken);
            }
            catch (PaymentGatewayException reauthEx)
            {
                // Can no longer be renewed: say so in terms an operator can act on.
                throw new PaymentGatewayException(
                    $"Order {order.Id} could not be fulfilled. Capturing the authorization failed ({captureEx.Message}) and it can no longer be renewed ({reauthEx.Message}). " +
                    "The hold has expired beyond PayPal's reauthorization window; ask the shopper to pay for the order again to create a fresh authorization.");
            }

            order.Payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id} authorization renewed to {reauth.AuthorizationId}. Retrying capture.");

            capture = await _gateway.CaptureAsync(reauth.AuthorizationId, amount, $"{captureKey}-{reauth.AuthorizationId}", cancellationToken);
        }

        order.SetFulfilled(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation(
            $"Order {order.Id} fulfilled. Capture {capture.CaptureId}: gross {capture.GrossAmount:0.00}, fee {capture.PayPalFee:0.00}, net {capture.NetAmount:0.00} {capture.Currency}.");
    }

    public async Task CancelOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));

        // Idempotent: already cancelled -> nothing to do.
        if (order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation($"Order {order.Id} is already cancelled; skipping.");
            return;
        }
        if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentException($"Order {order.Id} cannot be cancelled because it is {order.Status}. Cancellation is only possible before fulfilment.");
        }

        await _gateway.VoidAuthorizationAsync(order.Payment.AuthorizationId!, cancellationToken);
        order.SetCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} cancelled. Hold {order.Payment.AuthorizationId} released; no money moved.");
    }

    public async Task<PaymentRefund> RefundOrderAsync(Order order, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(order, nameof(order));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (order.Payment?.CaptureId is null)
        {
            throw new PaymentException($"Order {order.Id} cannot be refunded because it has not been captured.");
        }

        // Idempotent: the same key returns the original refund instead of refunding twice.
        var existing = order.Payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund with key '{idempotencyKey}' already applied to order {order.Id} ({existing.PayPalRefundId}); returning original.");
            return existing;
        }

        // A null amount means "refund everything still refundable".
        var refundAmount = amount ?? order.Payment.RefundableRemaining;
        order.EnsureCanRefund(refundAmount);

        var result = await _gateway.RefundAsync(
            order.Payment.CaptureId!,
            new Money(refundAmount, order.Payment.Currency),
            idempotencyKey,
            cancellationToken);

        var refund = new PaymentRefund(idempotencyKey, result.RefundId, result.Status, refundAmount, DateTimeOffset.UtcNow);
        order.RecordRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} refunded {refundAmount:0.00} {order.Payment.Currency} (refund {result.RefundId}). Status now {order.Status}.");
        return refund;
    }
}
