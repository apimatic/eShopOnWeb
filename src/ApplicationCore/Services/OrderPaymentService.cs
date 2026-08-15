using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPalClient;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // Unique per process run. Combined with the order id it makes PayPal invoice ids and idempotency
    // keys globally unique across restarts (the in-memory store resets order ids to 1) while staying
    // stable within a run so a genuine retry that reaches PayPal is still de-duplicated.
    private static readonly string RunNonce = System.Guid.NewGuid().ToString("N").Substring(0, 12);

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalClient payPalClient,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _payPalClient = payPalClient;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IEnumerable<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        var requested = lines?.ToList() ?? new List<OrderLineRequest>();
        if (requested.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (requested.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var catalogItemIds = requested.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = requested.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for buyer with total {order.Total()} {Currency}.");
        return order.Id;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} is already authorized; returning existing hold.");
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        var amount = order.Total();
        if (amount <= 0m)
        {
            throw new PaymentException($"Order {orderId} has a non-positive total and cannot be paid.");
        }

        var idempotencyKey = $"pay-{orderId}-{RunNonce}";
        var invoiceId = InvoiceIdFor(orderId);

        PayPalAuthorizationResult authResult;
        if (command.PaymentMethodId is int paymentMethodId)
        {
            var paymentMethod = await _paymentMethodRepository
                .FirstOrDefaultAsync(new PaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId), cancellationToken);
            if (paymentMethod is null)
            {
                throw new PaymentException($"Saved card {paymentMethodId} was not found for this shopper.");
            }

            authResult = await _payPalClient.AuthorizeOrderWithVaultedCardAsync(
                amount, Currency, paymentMethod.PayPalVaultId, idempotencyKey, invoiceId, cancellationToken);
        }
        else if (command.Card is not null)
        {
            authResult = await _payPalClient.AuthorizeOrderWithCardAsync(
                amount, Currency, command.Card, idempotencyKey, invoiceId, cancellationToken);
        }
        else
        {
            throw new PaymentException("Provide either card details or a saved paymentMethodId to pay.");
        }

        var payment = new Payment(authResult.PayPalOrderId, Currency, amount, authResult.InstrumentDescription, invoiceId);
        payment.SetAuthorization(authResult.AuthorizationId, authResult.Status);
        order.MarkAuthorized(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized {amount} {Currency} for order {orderId} (authorization {authResult.AuthorizationId}).");
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = order.Payment;

        // Idempotent: capturing an already-fulfilled order does nothing.
        if (order.Status == OrderStatus.Fulfilled && payment is { IsCaptured: true })
        {
            _logger.LogInformation($"Order {orderId} is already fulfilled; returning existing capture.");
            return order;
        }

        if (order.Status != OrderStatus.Authorized || payment is null || !payment.IsAuthorized)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var authorizationId = payment.AuthorizationId!;

        // Renew a stale hold rather than failing outright.
        var state = await _payPalClient.GetAuthorizationAsync(authorizationId, cancellationToken);
        payment.UpdateAuthorizationStatus(state.Status);

        if (IsVoided(state.Status))
        {
            throw new PaymentException(
                $"Order {orderId}'s authorization was voided (the order was cancelled); it cannot be fulfilled.");
        }

        if (!IsCapturable(state.Status))
        {
            _logger.LogInformation($"Authorization {authorizationId} for order {orderId} is {state.Status}; attempting to renew.");
            try
            {
                var renewed = await _payPalClient.ReauthorizeAsync(
                    authorizationId, payment.AuthorizedAmount, Currency, $"reauth-{orderId}-{RunNonce}", cancellationToken);
                payment.ReplaceAuthorization(renewed.AuthorizationId, renewed.Status);
                authorizationId = renewed.AuthorizationId;
                await _orderRepository.UpdateAsync(order, cancellationToken);
                _logger.LogInformation($"Renewed authorization for order {orderId}: new authorization {authorizationId}.");
            }
            catch (PayPalApiException ex)
            {
                throw new PaymentException(
                    $"Order {orderId}'s authorization ({state.Status}) is stale and could not be renewed: {ex.Message}. " +
                    "The hold can no longer be captured — ask the shopper to pay for the order again.", ex);
            }
        }

        var captureKey = $"capture-{orderId}-{RunNonce}";
        var capture = await _payPalClient.CaptureAuthorizationAsync(
            authorizationId, payment.AuthorizedAmount, Currency, captureKey, payment.InvoiceId, cancellationToken);

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        payment.UpdateAuthorizationStatus("CAPTURED");
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Captured {capture.GrossAmount} {Currency} for order {orderId} (fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status == OrderStatus.Fulfilled
            || order.Status == OrderStatus.PartiallyRefunded
            || order.Status == OrderStatus.Refunded)
        {
            throw new PaymentException(
                $"Order {orderId} has already been fulfilled; issue a refund instead of cancelling.");
        }

        var payment = order.Payment;
        if (payment is { IsAuthorized: true, IsCaptured: false })
        {
            await _payPalClient.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            payment.UpdateAuthorizationStatus("VOIDED");
            _logger.LogInformation($"Voided authorization {payment.AuthorizationId} for order {orderId}; held funds released.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);
        var payment = order.Payment;

        if (payment is null || !payment.IsCaptured
            || (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException(
                $"Order {orderId} has not been captured, so there is nothing to refund.");
        }

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} with key {idempotencyKey} already exists; returning it.");
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {orderId} has already been fully refunded.");
        }

        if (amount is decimal requestedAmount)
        {
            if (requestedAmount <= 0m)
            {
                throw new PaymentException("Refund amount must be greater than zero.");
            }
            // A partly-refunded order must never become refundable beyond what was captured.
            if (requestedAmount > remaining)
            {
                throw new PaymentException(
                    $"Refund of {requestedAmount} {Currency} exceeds the {remaining} {Currency} still refundable on order {orderId}.");
            }
        }

        var refundAmount = amount ?? remaining;
        // Local dedup uses the caller's raw key; the PayPal-Request-Id is scoped to this capture so the
        // same caller key used against a different capture never collides (DUPLICATE_REQUEST_ID).
        var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, amount, Currency, payPalRequestId, payment.InvoiceId, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        order.MarkRefundApplied();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Refunded {refundAmount} {Currency} on order {orderId} (refund {result.RefundId}).");
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        return orders;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        // One shopper must never see or act on another's order.
        if (order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private static string InvoiceIdFor(int orderId) => OrderInvoice.For(orderId, RunNonce);

    private static bool IsCapturable(string status) =>
        status is "CREATED" or "PENDING" or "PARTIALLY_CAPTURED";

    private static bool IsVoided(string status) => status is "VOIDED";
}
