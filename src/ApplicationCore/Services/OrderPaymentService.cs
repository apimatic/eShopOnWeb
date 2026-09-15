using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Safety margin: renew a hold that is due to expire within this window rather than risk a stale capture.
    private static readonly TimeSpan ReauthorizeMargin = TimeSpan.FromMinutes(30);
    private const string DefaultAddressValue = "N/A";
    private const string DefaultPictureUri = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentConfiguration _configuration;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPaymentGateway gateway,
        IPaymentConfiguration configuration,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _configuration = configuration;
        _logger = logger;
    }

    private string Currency => _configuration.Currency;

    public async Task<Result<OrderPlaced>> PlaceOrderAsync(
        string buyerId, IReadOnlyList<OrderLine> lines, ShippingAddressInput? address, CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            return Result<OrderPlaced>.Invalid(new ValidationError { ErrorMessage = "At least one order line is required." });
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            return Result<OrderPlaced>.Invalid(new ValidationError { ErrorMessage = "Every order line quantity must be greater than zero." });
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var missing = ids.Where(id => !byId.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            return Result<OrderPlaced>.Invalid(new ValidationError
            {
                ErrorMessage = $"Unknown catalog item id(s): {string.Join(", ", missing)}."
            });
        }

        var items = lines.Select(line =>
        {
            var catalogItem = byId[line.CatalogItemId];
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri) ? DefaultPictureUri : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shipTo = address is null
            ? new Address(DefaultAddressValue, DefaultAddressValue, DefaultAddressValue, DefaultAddressValue, DefaultAddressValue)
            : new Address(address.Street, address.City, address.State, address.Country, address.ZipCode);

        var order = new Order(buyerId, shipTo, items);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation("Order {OrderId} placed by {BuyerId} awaiting payment.", order.Id, buyerId);
        return Result<OrderPlaced>.Created(new OrderPlaced(order.Id, order.Status.ToString(), order.Total(), Currency));
    }

    public async Task<Result<PaymentView>> PayAsync(
        string buyerId, int orderId, PayInstruction instruction, CancellationToken ct = default)
    {
        // Load with items so order.Total() (the amount to hold) is computed from the order lines.
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return Result<PaymentView>.NotFound();
        }

        var existingPayment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        // Idempotent in effect: a double-click never authorizes twice — if a hold already exists, return it.
        if (existingPayment is not null)
        {
            return Result<PaymentView>.Success(ToView(order, existingPayment));
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            return Result<PaymentView>.Conflict($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        // Resolve the instrument (raw card or a saved card the caller owns).
        Interfaces.Payments.PaymentInstrument gatewayInstrument;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var method = await _paymentMethodRepository.GetByIdAsync(savedId, ct);
            if (method is null || method.BuyerId != buyerId)
            {
                return Result<PaymentView>.NotFound();
            }
            gatewayInstrument = Interfaces.Payments.PaymentInstrument.FromVault(method.PayPalVaultId);
        }
        else if (instruction.Card is not null)
        {
            gatewayInstrument = Interfaces.Payments.PaymentInstrument.FromCard(ToGatewayCard(instruction.Card));
        }
        else
        {
            return Result<PaymentView>.Invalid(new ValidationError
            {
                ErrorMessage = "Provide either card details or a saved payment method id to pay with."
            });
        }

        var amount = order.Total();
        var authorization = await _gateway.AuthorizeOrderAsync(amount, Currency, gatewayInstrument, IdempotencyBase(order), ct);

        var payment = new Payment(orderId, buyerId, amount, Currency, authorization.PayPalOrderId);
        payment.SetAuthorized(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
        await _paymentRepository.AddAsync(payment, ct);

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} authorized (hold placed) for {BuyerId}.", orderId, buyerId);
        return Result<PaymentView>.Success(ToView(order, payment));
    }

    public async Task<Result<PaymentView>> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Result<PaymentView>.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            return Result<PaymentView>.Conflict($"Order {orderId} has no authorized payment to fulfil.");
        }

        // Idempotent: already captured -> return current state without capturing again.
        if (payment.Status == PaymentStatus.Captured || order.Status == OrderStatus.Fulfilled)
        {
            return Result<PaymentView>.Success(ToView(order, payment));
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment.AuthorizationId is null)
        {
            return Result<PaymentView>.Conflict($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var authorizationId = payment.AuthorizationId;

        // Proactively renew a hold that is expired or about to expire.
        if (payment.AuthorizationExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow.Add(ReauthorizeMargin))
        {
            var renewed = await _gateway.ReauthorizeAsync(authorizationId, payment.Amount, payment.CurrencyCode, IdempotencyBase(order), ct);
            payment.SetReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            authorizationId = renewed.AuthorizationId;
        }

        GatewayCaptureOutcome capture;
        try
        {
            capture = await CaptureAsync(authorizationId, order, ct);
        }
        catch (PaymentGatewayException ex) when (ex.IndicatesAuthorizationExpired)
        {
            // Reactive path: the hold lapsed before capture — renew (may itself be operator-actionable) then retry.
            _logger.LogWarning("Authorization for order {OrderId} expired; attempting renewal before capture.", orderId);
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.CurrencyCode, IdempotencyBase(order), ct);
            payment.SetReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            capture = await CaptureAsync(renewed.AuthorizationId, order, ct);
        }

        payment.SetCaptured(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} fulfilled; captured {Amount} {Currency}.", orderId, capture.CapturedAmount, payment.CurrencyCode);
        return Result<PaymentView>.Success(ToView(order, payment));
    }

    public async Task<Result<PaymentView>> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
        {
            return Result<PaymentView>.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);

        // Idempotent: already cancelled.
        if (order.Status == OrderStatus.Cancelled)
        {
            return Result<PaymentView>.Success(ToView(order, payment));
        }

        if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
        {
            return Result<PaymentView>.Conflict($"Order {orderId} is {order.Status} and can no longer be cancelled.");
        }

        // Release any held funds.
        if (payment is not null && payment.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, IdempotencyBase(order), ct);
            payment.SetVoided(AuthorizationStatusVoided);
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} cancelled; any held funds released.", orderId);
        return Result<PaymentView>.Success(ToView(order, payment));
    }

    public async Task<Result<RefundResult>> RefundAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Result<RefundResult>.Invalid(new ValidationError { ErrorMessage = "An idempotency key is required for refunds." });
        }
        if (amount is <= 0m)
        {
            return Result<RefundResult>.Invalid(new ValidationError { ErrorMessage = "Refund amount must be greater than zero." });
        }

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null || order.BuyerId != buyerId)
        {
            return Result<RefundResult>.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null || payment.CaptureId is null ||
            (payment.Status != PaymentStatus.Captured && payment.Status != PaymentStatus.PartiallyRefunded))
        {
            return Result<RefundResult>.Conflict($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotency: the same key never refunds twice — return the original refund.
        var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (priorRefund is not null)
        {
            return Result<RefundResult>.Success(new RefundResult(
                priorRefund.RefundId, priorRefund.Amount, priorRefund.Status, payment.Status.ToString(), order.Status.ToString()));
        }

        var refundable = payment.RefundableRemaining();
        if (refundable <= 0m)
        {
            return Result<RefundResult>.Conflict($"Order {orderId} has already been fully refunded.");
        }

        // A "full" refund of an already-partly-refunded capture means the remaining amount — never more than captured.
        var effectiveAmount = amount ?? refundable;
        if (effectiveAmount > refundable)
        {
            return Result<RefundResult>.Invalid(new ValidationError
            {
                ErrorMessage = $"Refund of {effectiveAmount} {payment.CurrencyCode} exceeds the refundable remaining {refundable} {payment.CurrencyCode}."
            });
        }

        var gatewayRefund = await _gateway.RefundAsync(payment.CaptureId, effectiveAmount, payment.CurrencyCode, idempotencyKey, ct);

        var recorded = payment.AddRefund(gatewayRefund.RefundId, effectiveAmount, gatewayRefund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkRefunded(payment.IsFullyRefunded);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency} (refund {RefundId}).",
            orderId, effectiveAmount, payment.CurrencyCode, recorded.RefundId);
        return Result<RefundResult>.Created(new RefundResult(
            recorded.RefundId, recorded.Amount, recorded.Status, payment.Status.ToString(), order.Status.ToString()));
    }

    public async Task<Result<IReadOnlyList<OrderSummaryView>>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerIdSpecification(buyerId), ct);
        var paymentByOrderId = payments.ToDictionary(p => p.OrderId);

        var summaries = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order =>
            {
                paymentByOrderId.TryGetValue(order.Id, out var payment);
                return new OrderSummaryView(
                    order.Id, order.OrderDate, order.Status.ToString(), order.Total(), Currency,
                    payment is null ? null : ToView(order, payment));
            })
            .ToList();

        return Result<IReadOnlyList<OrderSummaryView>>.Success(summaries);
    }

    // --- helpers ---

    private const string AuthorizationStatusVoided = "VOIDED";

    // Anchored on the order's persisted token so retries/double-clicks share a key while distinct orders
    // never collide (even when integer ids repeat across an in-memory reset).
    private static string IdempotencyBase(Order order) => $"eshop-order-{order.IdempotencyToken:N}";

    private readonly struct GatewayCaptureOutcome
    {
        public GatewayCaptureOutcome(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
        {
            CaptureId = captureId;
            Status = status;
            CapturedAmount = capturedAmount;
            PayPalFee = payPalFee;
            NetAmount = netAmount;
        }
        public string CaptureId { get; }
        public string Status { get; }
        public decimal CapturedAmount { get; }
        public decimal? PayPalFee { get; }
        public decimal? NetAmount { get; }
    }

    private async Task<GatewayCaptureOutcome> CaptureAsync(string authorizationId, Order order, CancellationToken ct)
    {
        var capture = await _gateway.CaptureAsync(authorizationId, IdempotencyBase(order), ct);
        return new GatewayCaptureOutcome(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
    }

    private static Interfaces.Payments.GatewayCard ToGatewayCard(CardInput card)
    {
        GatewayBillingAddress? billing = null;
        if (!string.IsNullOrEmpty(card.CountryCode) &&
            (card.BillingLine1 is not null || card.BillingPostalCode is not null || card.BillingCity is not null))
        {
            billing = new GatewayBillingAddress(
                card.BillingLine1, card.BillingLine2, card.BillingState, card.BillingCity, card.BillingPostalCode, card.CountryCode);
        }
        return new GatewayCard(card.Number, card.Expiry, card.SecurityCode, card.CardholderName, billing);
    }

    private PaymentView ToView(Order order, Payment? payment)
    {
        if (payment is null)
        {
            return new PaymentView(order.Id, order.Status.ToString(), PaymentStatus.Created.ToString(),
                order.Total(), Currency, string.Empty, null, null, null, null, null, null, null, null, 0m,
                Array.Empty<RefundView>());
        }

        var refunds = payment.Refunds
            .Select(r => new RefundView(r.RefundId, r.Amount, r.Status))
            .ToList();

        return new PaymentView(
            order.Id,
            order.Status.ToString(),
            payment.Status.ToString(),
            payment.Amount,
            payment.CurrencyCode,
            payment.PayPalOrderId,
            payment.AuthorizationId,
            payment.AuthorizationStatus,
            payment.AuthorizationExpiresAt,
            payment.CaptureId,
            payment.CaptureStatus,
            payment.CapturedAmount,
            payment.PayPalFee,
            payment.NetAmount,
            payment.TotalRefunded(),
            refunds);
    }
}
