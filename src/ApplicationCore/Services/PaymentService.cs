using System;
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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderPayment> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(lines));

        var catalogItemIds = lines.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), ct);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} was not found.", nameof(lines));
            if (line.Quantity <= 0)
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be positive.", nameof(lines));

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        // Local record before any PayPal call: created awaiting payment, carrying its correlation
        // invoice id and a per-order idempotency seed.
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _gateway.Currency);
        payment = await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Order {order.Id} placed for {buyerId}; total {payment.Amount} {payment.Currency}; awaiting payment.");
        return payment;
    }

    public async Task<OrderPayment> PayAsync(int orderId, string buyerId, PayInstruction instruction, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var payment = await LoadOwnPaymentAsync(orderId, buyerId, ct);

        // Idempotent in effect: an already-authorized order returns its state with no second hold.
        if (payment.State == PaymentState.Authorized)
        {
            _logger.LogInformation($"Order {orderId} already authorized; returning existing hold.");
            return payment;
        }
        if (payment.State != PaymentState.AwaitingPayment && payment.State != PaymentState.Failed)
            throw new PaymentStateException($"Order {orderId} cannot be paid from state {payment.State}.");

        var (card, vaultTokenId) = await ResolvePaymentSourceAsync(buyerId, instruction, ct);

        var command = new AuthorizeCommand
        {
            Amount = payment.Amount,
            InvoiceId = payment.InvoiceId,
            CustomId = payment.InvoiceId,
            Description = $"eShopOnWeb order {orderId}",
            CreateRequestId = payment.CreateRequestId,
            AuthorizeRequestId = payment.AuthorizeRequestId,
            Card = card,
            VaultTokenId = vaultTokenId
        };

        AuthorizeResult result;
        try
        {
            result = await _gateway.AuthorizeAsync(command, ct);
        }
        catch (PaymentGatewayException ex)
        {
            payment.MarkFailed(Truncate(ex.Message));
            await _paymentRepository.UpdateAsync(payment, ct);
            throw;
        }

        if (!result.Success || string.IsNullOrEmpty(result.AuthorizationId))
        {
            var reason = result.FailureReason ?? "Authorization was not approved.";
            payment.MarkFailed(Truncate(reason));
            await _paymentRepository.UpdateAsync(payment, ct);
            throw new PaymentGatewayException(reason);
        }

        payment.MarkAuthorized(result.PayPalOrderId!, result.AuthorizationId!, result.AuthorizationStatus);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} authorized: paypalOrder={result.PayPalOrderId} auth={result.AuthorizationId} status={result.AuthorizationStatus}.");
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        // Idempotent: an already-fulfilled order captures nothing more.
        if (payment.State == PaymentState.Fulfilled ||
            payment.State == PaymentState.Refunded ||
            payment.State == PaymentState.PartiallyRefunded)
        {
            _logger.LogInformation($"Order {orderId} already fulfilled; no capture performed.");
            return payment;
        }
        if (payment.State != PaymentState.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled from state {payment.State}.");

        var authorizationId = payment.AuthorizationId!;

        // A stale hold is renewed rather than failing the fulfilment outright.
        if (IsAuthorizationStale(payment.AuthorizationStatus))
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.PayPalOrderId!, payment.Amount,
                payment.CaptureRequestId, ct);
        }
        catch (PaymentGatewayException ex) when (LooksLikeExpiredAuthorization(ex))
        {
            // The hold went stale between the check and the capture — renew and capture once more.
            _logger.LogWarning($"Order {orderId} capture reported a stale authorization ({ex.ProviderCode}); renewing.");
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            capture = await _gateway.CaptureAsync(authorizationId, payment.PayPalOrderId!, payment.Amount,
                payment.CaptureRequestId, ct);
        }

        if (!capture.Success || string.IsNullOrEmpty(capture.CaptureId))
        {
            var reason = capture.FailureReason ?? "Capture was not completed.";
            throw new PaymentGatewayException(reason, statusCode: null);
        }

        payment.MarkCaptured(capture.CaptureId!, capture.Status ?? "COMPLETED", capture.CapturedAmount,
            capture.PaypalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} fulfilled: capture={capture.CaptureId} status={capture.Status} gross={capture.CapturedAmount} fee={capture.PaypalFee} net={capture.NetAmount}.");
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        if (payment.State == PaymentState.Cancelled)
        {
            _logger.LogInformation($"Order {orderId} already cancelled; no void performed.");
            return payment;
        }
        if (payment.State != PaymentState.Authorized || string.IsNullOrEmpty(payment.AuthorizationId))
            throw new PaymentStateException($"Order {orderId} cannot be cancelled from state {payment.State}. Cancel is only valid before fulfilment.");

        var result = await _gateway.VoidAsync(payment.AuthorizationId!, payment.VoidRequestId, ct);
        payment.MarkCancelled(result.Status);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} cancelled: held funds released (auth={payment.AuthorizationId}, status={result.Status}).");
        return payment;
    }

    public async Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId,
        decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnPaymentAsync(orderId, buyerId, ct);

        if (payment.State != PaymentState.Fulfilled &&
            payment.State != PaymentState.PartiallyRefunded)
            throw new PaymentStateException($"Order {orderId} cannot be refunded from state {payment.State}. A payment must be captured first.");
        if (string.IsNullOrEmpty(payment.CaptureId))
            throw new PaymentStateException($"Order {orderId} has no captured payment to refund.");

        // Repeat under the same idempotency key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Order {orderId} refund replayed for idempotency key {idempotencyKey}; returning existing refund {existing.PayPalRefundId}.");
            return (payment, existing);
        }

        // A partial refund must never push the cumulative total past the captured amount.
        var remaining = payment.RefundableRemaining();
        if (amount is not null)
        {
            if (amount.Value <= 0m)
                throw new PaymentStateException("Refund amount must be positive.");
            if (amount.Value > remaining)
                throw new PaymentStateException($"Refund of {amount.Value} exceeds the refundable remaining {remaining} for order {orderId}.");
        }
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentStateException($"Order {orderId} has no refundable amount remaining.");

        // Local record before the provider call.
        var refund = payment.AddRefund(idempotencyKey, refundAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        RefundResult result;
        try
        {
            result = await _gateway.RefundAsync(payment.CaptureId!, amount, idempotencyKey, ct);
        }
        catch (PaymentGatewayException)
        {
            // Leave the pending local refund in place; a replay under the same key settles it (PayPal dedups).
            throw;
        }

        if (!result.Success || string.IsNullOrEmpty(result.RefundId))
        {
            var reason = result.FailureReason ?? "Refund was not completed.";
            throw new PaymentGatewayException(reason);
        }

        refund.SetResult(result.RefundId!, result.Status ?? "PENDING");
        payment.RecomputeRefundState();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Order {orderId} refunded {refundAmount}: refund={result.RefundId} status={result.Status}; state={payment.State}.");
        return (payment, refund);
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpecification(buyerId), ct);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => new OrderWithPayment(o, paymentsByOrder.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    // ---- helpers ----

    private async Task<OrderPayment> LoadPaymentAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
            throw new OrderNotFoundException(orderId);
        return payment;
    }

    private async Task<OrderPayment> LoadOwnPaymentAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var payment = await LoadPaymentAsync(orderId, ct);
        // One shopper must never see or act on another's order.
        if (!string.Equals(payment.BuyerId, buyerId, StringComparison.Ordinal))
            throw new OrderNotFoundException(orderId);
        return payment;
    }

    private async Task<(CardDetails? Card, string? VaultTokenId)> ResolvePaymentSourceAsync(
        string buyerId, PayInstruction instruction, CancellationToken ct)
    {
        var hasCard = instruction.Card is not null;
        var hasSaved = instruction.SavedPaymentMethodId is not null;
        if (hasCard == hasSaved)
            throw new ArgumentException("Provide either card details or a saved payment method id, but not both.");

        if (hasSaved)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdAndBuyerSpecification(instruction.SavedPaymentMethodId!.Value, buyerId), ct);
            if (method is null || string.IsNullOrEmpty(method.PayPalVaultTokenId))
                throw new PaymentMethodNotFoundException(instruction.SavedPaymentMethodId!.Value);
            return (null, method.PayPalVaultTokenId);
        }

        return (instruction.Card, null);
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        ReauthorizeResult reauth;
        try
        {
            reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.ReauthorizeRequestId, ct);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentGatewayException(
                $"The authorization for order {payment.OrderId} has expired and could not be renewed ({ex.ProviderCode ?? "cannot reauthorize"}). " +
                "Ask the shopper to pay again to place a fresh hold.", ex.ProviderCode, ex.ProviderDebugId, ex.StatusCode, ex);
        }

        if (!reauth.Success || string.IsNullOrEmpty(reauth.AuthorizationId))
        {
            throw new PaymentGatewayException(
                $"The authorization for order {payment.OrderId} has expired and could not be renewed ({reauth.FailureReason ?? "cannot reauthorize"}). " +
                "Ask the shopper to pay again to place a fresh hold.");
        }

        payment.ReplaceAuthorization(reauth.AuthorizationId!, reauth.Status);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogWarning($"Order {payment.OrderId} authorization renewed: new auth={reauth.AuthorizationId} status={reauth.Status}.");
        return reauth.AuthorizationId!;
    }

    private static bool IsAuthorizationStale(string? status) =>
        string.Equals(status, "EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "VOIDED", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeExpiredAuthorization(PaymentGatewayException ex) =>
        string.Equals(ex.ProviderCode, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value) =>
        value.Length <= 400 ? value : value.Substring(0, 400);
}
