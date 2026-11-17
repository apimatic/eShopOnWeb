using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.Infrastructure.Finix;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class OrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IFinixClient _finixClient;
    private readonly FinixOptions _options;
    private readonly OrderLockProvider _locks;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<SavedCard> savedCardRepository,
        IFinixClient finixClient,
        IOptions<FinixOptions> options,
        OrderLockProvider locks,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _savedCardRepository = savedCardRepository;
        _finixClient = finixClient;
        _options = options.Value;
        _locks = locks;
        _logger = logger;
    }

    public async Task<OrderResponse> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        var requestedItems = request.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToList();

        if (requestedItems.Any(i => i.CatalogItemId <= 0))
        {
            throw new PaymentValidationException("Every catalog item id must be greater than zero.");
        }

        if (requestedItems.Any(i => i.Quantity <= 0))
        {
            throw new PaymentValidationException("Every item quantity must be greater than zero.");
        }

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(requestedItems.Select(i => i.CatalogItemId).ToArray()),
            cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missingIds = requestedItems
            .Where(i => !catalogById.ContainsKey(i.CatalogItemId))
            .Select(i => i.CatalogItemId)
            .ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentValidationException(
                $"Catalog item(s) not found: {string.Join(", ", missingIds)}.");
        }

        var orderItems = requestedItems.Select(requested =>
        {
            var catalogItem = catalogById[requested.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(ordered, catalogItem.Price, requested.Quantity);
        }).ToList();

        var order = new Order(buyerId, MapAddress(request.ShippingAddress), orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation(
            "Created order {OrderId} for buyer {BuyerId} with {ItemCount} line(s), total {TotalCents} cents.",
            order.Id,
            buyerId,
            orderItems.Count,
            order.TotalCents());

        return PaymentMapper.ToOrderResponse(order, _options.ResolveCurrency());
    }

    public async Task<OrderResponse> PayAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var orderLock = await _locks.AcquireAsync(orderId, cancellationToken);

        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be paid.");
        }

        if (order.PaymentStatus is OrderPaymentStatus.Authorizing or
            OrderPaymentStatus.Authorized or
            OrderPaymentStatus.Captured or
            OrderPaymentStatus.PartiallyRefunded or
            OrderPaymentStatus.Refunded)
        {
            return PaymentMapper.ToOrderResponse(order, _options.ResolveCurrency());
        }

        if (request.PaymentMethodId.HasValue && request.Card is not null)
        {
            throw new PaymentValidationException(
                "Provide either paymentMethodId or card details, not both.");
        }

        if (!request.PaymentMethodId.HasValue && request.Card is null)
        {
            throw new PaymentValidationException(
                "Provide either a saved paymentMethodId or card details.");
        }

        var currency = _options.ResolveCurrency();
        var amountCents = order.TotalCents();
        var attempt = order.AuthorizationAttempt + 1;
        var idempotencyId = $"eshop-order-{order.PaymentReference:N}-auth-{attempt}";

        try
        {
            var source = await ResolvePaymentSourceAsync(buyerId, request, order.Id, cancellationToken);

            var authorization = await _finixClient.CreateAuthorizationAsync(
                new FinixCreateAuthorizationRequest
                {
                    Amount = amountCents,
                    Currency = currency,
                    Merchant = _options.MerchantId,
                    Source = source,
                    IdempotencyId = idempotencyId,
                    Tags = new Dictionary<string, string>
                    {
                        ["eshop_order_id"] = order.Id.ToString(),
                        ["eshop_buyer_id"] = buyerId
                    }
                },
                cancellationToken);

            order.RecordAuthorization(
                authorization.Id,
                authorization.State ?? "UNKNOWN",
                authorization.Amount,
                authorization.Currency ?? currency,
                attempt,
                authorization.ExpiresAt,
                authorization.FailureCode,
                authorization.FailureMessage);

            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation(
                "Authorization {AuthorizationId} for order {OrderId} is {State} ({AmountCents} cents).",
                authorization.Id,
                order.Id,
                authorization.State,
                authorization.Amount);

            if (order.PaymentStatus == OrderPaymentStatus.Failed)
            {
                throw new PaymentConflictException(
                    $"Finix declined authorization {authorization.Id} for order {order.Id}: " +
                    $"{authorization.FailureMessage ?? authorization.FailureCode ?? authorization.State}. " +
                    "The shopper can retry payment with the same or a different card.");
            }

            return PaymentMapper.ToOrderResponse(order, currency);
        }
        catch (FinixApiException ex)
        {
            order.SetAuthorizationAttempt(attempt);
            order.RecordAuthorizationFailure(
                order.FinixAuthorizationId,
                "FAILED",
                ex.Code,
                ex.Message);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            throw new PaymentConflictException(
                $"Finix could not authorize payment for order {order.Id}: {ex.Message}");
        }
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var orderLock = await _locks.AcquireAsync(orderId, cancellationToken);

        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be fulfilled.");
        }

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled)
        {
            return PaymentMapper.ToOrderResponse(order, _options.ResolveCurrency());
        }

        if (order.PaymentStatus is OrderPaymentStatus.Captured or
            OrderPaymentStatus.PartiallyRefunded or
            OrderPaymentStatus.Refunded)
        {
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return PaymentMapper.ToOrderResponse(order, _options.ResolveCurrency());
        }

        if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            throw new PaymentConflictException(
                $"Order {orderId} has no authorization to capture. The shopper must pay for the order first.");
        }

        var authorizationId = order.FinixAuthorizationId;
        var currency = _options.ResolveCurrency();

        try
        {
            var authorization = await _finixClient.GetAuthorizationAsync(authorizationId, cancellationToken);
            order.UpdateAuthorizationSnapshot(
                authorization.State ?? "UNKNOWN",
                authorization.IsVoid == true,
                authorization.VoidState,
                authorization.ExpiresAt,
                authorization.FailureCode,
                authorization.FailureMessage);

            EnsureAuthorizationCanBeCaptured(order, authorization);

            var capture = await _finixClient.CaptureAuthorizationAsync(
                authorizationId,
                order.TotalCents(),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(capture.Transfer))
            {
                throw new PaymentConflictException(
                    $"Finix accepted capture of authorization {authorizationId} but did not return a transfer id. " +
                    "Retry fulfilment or inspect the authorization in Finix before taking further action.");
            }

            var transfer = await PollTransferAsync(capture.Transfer, cancellationToken);
            if (IsFailedTransfer(transfer.State))
            {
                order.UpdateCaptureState(transfer.State ?? "FAILED", transfer.Amount, transfer.Fee);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentConflictException(
                    $"Capture transfer {transfer.Id} for order {orderId} ended in state {transfer.State}: " +
                    $"{transfer.FailureMessage ?? transfer.FailureCode ?? "no failure detail returned"}. " +
                    "The order was not fulfilled. Re-authorize or cancel the order.");
            }

            var capturedAmount = transfer.Amount != 0 ? transfer.Amount : capture.Amount;
            var feeCents = transfer.Fee ?? 0;

            order.RecordCapture(
                transfer.Id,
                transfer.State ?? capture.State ?? "UNKNOWN",
                capturedAmount,
                feeCents,
                capturedAmount - feeCents);
            order.MarkFulfilled();

            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation(
                "Captured {CapturedAmountCents} cents for order {OrderId} as transfer {TransferId}; fee {FeeCents} cents.",
                capturedAmount,
                order.Id,
                transfer.Id,
                feeCents);

            return PaymentMapper.ToOrderResponse(order, currency);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentConflictException(
                $"Finix could not capture authorization {authorizationId} for order {orderId}: {ex.Message}. " +
                "The hold may have expired, been voided, or already been captured. Re-authorize the order or cancel it.");
        }
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var orderLock = await _locks.AcquireAsync(orderId, cancellationToken);

        var order = await GetOrderAsync(orderId, cancellationToken);

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
        {
            return PaymentMapper.ToOrderResponse(order, _options.ResolveCurrency());
        }

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled ||
            order.PaymentStatus is OrderPaymentStatus.Captured or
                OrderPaymentStatus.PartiallyRefunded or
                OrderPaymentStatus.Refunded)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been fulfilled. Use the refunds endpoint to return captured money.");
        }

        if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId) && !order.FinixAuthorizationVoided)
        {
            try
            {
                var authorization = await _finixClient.VoidAuthorizationAsync(
                    order.FinixAuthorizationId,
                    cancellationToken);

                order.UpdateAuthorizationSnapshot(
                    authorization.State ?? order.FinixAuthorizationState ?? "UNKNOWN",
                    authorization.IsVoid == true,
                    authorization.VoidState ?? "SUCCEEDED",
                    authorization.ExpiresAt,
                    authorization.FailureCode,
                    authorization.FailureMessage);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentConflictException(
                    $"Authorization {order.FinixAuthorizationId} for order {orderId} can no longer be voided: " +
                    $"{ex.Message}. If the hold has already expired it will release on its own; otherwise inspect " +
                    "the authorization in Finix before cancelling the order.");
            }
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Cancelled order {OrderId}; any held funds were released.", order.Id);
        return PaymentMapper.ToOrderResponse(order, _options.ResolveCurrency());
    }

    public async Task<RefundResponse> RefundAsync(
        string buyerId,
        int orderId,
        CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        using var orderLock = await _locks.AcquireAsync(orderId, cancellationToken);

        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }

        if (string.IsNullOrWhiteSpace(order.FinixCaptureTransferId) || !order.CapturedAmountCents.HasValue)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has not been captured yet. Refunds are only available after fulfilment.");
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.Captured or
            OrderPaymentStatus.PartiallyRefunded or
            OrderPaymentStatus.Refunded))
        {
            throw new PaymentConflictException(
                $"Order {orderId} is in payment state {order.PaymentStatus}; it is not refundable.");
        }

        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("idempotencyKey is required for refunds.");
        }

        var requestedAmount = ResolveRefundAmount(request);
        var existing = order.Refunds.FirstOrDefault(
            r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

        if (existing is not null)
        {
            if (requestedAmount.HasValue && requestedAmount.Value != existing.AmountCents)
            {
                throw new PaymentConflictException(
                    $"Idempotency key '{idempotencyKey}' was already used to refund {existing.AmountCents} cents; " +
                    "it cannot be reused with a different amount.");
            }

            return PaymentMapper.ToRefundResponse(order, existing, true);
        }

        var remaining = order.RemainingRefundableCents();
        var amountCents = requestedAmount ?? remaining;

        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The refund amount must be greater than zero.");
        }

        if (remaining <= 0)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been fully refunded ({order.RefundedAmountCents} cents).");
        }

        if (amountCents > remaining)
        {
            throw new PaymentConflictException(
                $"Refund of {amountCents} cents exceeds the {remaining} cents still refundable on order {orderId}. " +
                "The total refunded amount can never exceed the captured amount.");
        }

        await EnsureCaptureTransferSettledAsync(order, cancellationToken);

        try
        {
            var refund = await _finixClient.CreateRefundAsync(
                order.FinixCaptureTransferId!,
                amountCents,
                cancellationToken);

            var refundEntity = new OrderRefund(
                order.Id,
                refund.Id,
                amountCents,
                refund.State ?? "UNKNOWN",
                idempotencyKey);

            order.AddRefund(refundEntity);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation(
                "Refunded {AmountCents} cents on order {OrderId} as Finix transfer {RefundId} ({State}).",
                amountCents,
                order.Id,
                refund.Id,
                refund.State);

            return PaymentMapper.ToRefundResponse(order, refundEntity, false);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentConflictException(
                $"Finix could not refund order {orderId}: {ex.Message}");
        }
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentsSpecification(buyerId),
            cancellationToken);

        var currency = _options.ResolveCurrency();
        return new MyOrdersResponse
        {
            Orders = orders.Select(o => PaymentMapper.ToOrderResponse(o, currency)).ToList()
        };
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentsByIdSpecification(orderId),
            cancellationToken);

        if (order is null)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<string> ResolvePaymentSourceAsync(
        string buyerId,
        PayOrderRequest request,
        int orderId,
        CancellationToken cancellationToken)
    {
        if (request.PaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdAndBuyerSpecification(request.PaymentMethodId.Value, buyerId),
                cancellationToken);

            if (savedCard is null)
            {
                throw new PaymentResourceNotFoundException(
                    $"Saved payment method {request.PaymentMethodId.Value} was not found.");
            }

            return savedCard.FinixPaymentInstrumentId;
        }

        var card = request.Card!;
        ValidateCard(card);

        var identity = await _finixClient.CreateBuyerIdentityAsync(buyerId, cancellationToken);
        var paymentInstrument = await _finixClient.CreatePaymentInstrumentAsync(
            MapCard(card, identity.Id, orderId),
            cancellationToken);

        return paymentInstrument.Id;
    }

    internal static FinixCreatePaymentInstrumentRequest MapCard(
        CardDetailsRequest card,
        string identityId,
        int? orderId = null)
    {
        return new FinixCreatePaymentInstrumentRequest
        {
            Type = "PAYMENT_CARD",
            Name = card.Name?.Trim(),
            Number = NormalizeCardNumber(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = NormalizeYear(card.ExpirationYear),
            SecurityCode = card.SecurityCode?.Trim(),
            Identity = identityId,
            Address = MapCardAddress(card.Address),
            Tags = orderId.HasValue
                ? new Dictionary<string, string> { ["eshop_order_id"] = orderId.Value.ToString() }
                : null
        };
    }

    internal static void ValidateCard(CardDetailsRequest card)
    {
        var number = NormalizeCardNumber(card.Number);
        if (number.Length is < 12 or > 19 || !number.All(char.IsDigit))
        {
            throw new PaymentValidationException("Card number must contain 12 to 19 digits.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("Card expiration month must be between 1 and 12.");
        }

        var year = NormalizeYear(card.ExpirationYear);
        if (year < DateTime.UtcNow.Year || year > DateTime.UtcNow.Year + 30)
        {
            throw new PaymentValidationException("Card expiration year is not valid.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.SecurityCode.Length is < 3 or > 4 ||
            !card.SecurityCode.All(char.IsDigit))
        {
            throw new PaymentValidationException("Card security code must be 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("Cardholder name is required.");
        }
    }

    internal static string NormalizeCardNumber(string? number) =>
        new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    internal static int NormalizeYear(int year) => year < 100 ? 2000 + year : year;

    internal static FinixAddress? MapCardAddress(CardAddressRequest? address)
    {
        if (address is null)
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country
        };
    }

    private static Address MapAddress(ShippingAddressRequest? address)
    {
        return new Address(
            string.IsNullOrWhiteSpace(address?.Street) ? "1 Main Street" : address!.Street!,
            string.IsNullOrWhiteSpace(address?.City) ? "Seattle" : address!.City!,
            string.IsNullOrWhiteSpace(address?.State) ? "WA" : address!.State!,
            string.IsNullOrWhiteSpace(address?.Country) ? "USA" : address!.Country!,
            string.IsNullOrWhiteSpace(address?.ZipCode) ? "98101" : address!.ZipCode!);
    }

    private static long? ResolveRefundAmount(CreateRefundRequest request)
    {
        if (request.AmountCents.HasValue && request.Amount.HasValue)
        {
            throw new PaymentValidationException("Provide either amountCents or amount, not both.");
        }

        if (request.AmountCents.HasValue)
        {
            return request.AmountCents.Value;
        }

        if (request.Amount.HasValue)
        {
            return Money.ToCents(request.Amount.Value);
        }

        return null;
    }

    private async Task EnsureCaptureTransferSettledAsync(Order order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
        {
            return;
        }

        var transfer = await _finixClient.GetTransferAsync(order.FinixCaptureTransferId!, cancellationToken);

        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (!string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await _finixClient.GetTransferAsync(order.FinixCaptureTransferId!, cancellationToken);
        }

        if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                $"Capture transfer {transfer.Id} for order {order.Id} is still PENDING at Finix. " +
                "Partial refunds become available after the capture succeeds; retry the refund shortly.");
        }

        if (IsFailedTransfer(transfer.State))
        {
            throw new PaymentConflictException(
                $"Capture transfer {transfer.Id} for order {order.Id} is {transfer.State}: " +
                $"{transfer.FailureMessage ?? transfer.FailureCode ?? "no failure detail returned"}. " +
                "It cannot be refunded.");
        }

        order.UpdateCaptureState(
            transfer.State ?? order.FinixCaptureState ?? "UNKNOWN",
            transfer.Amount,
            transfer.Fee);
        await _orderRepository.UpdateAsync(order, cancellationToken);
    }

    private async Task<FinixTransfer> PollTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        FinixTransfer? transfer = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);
            if (!string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                return transfer;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return transfer!;
    }

    private static bool IsFailedTransfer(string? state) =>
        string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "RETURNED", StringComparison.OrdinalIgnoreCase);

    private static void EnsureAuthorizationCanBeCaptured(Order order, FinixAuthorization authorization)
    {
        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                $"Authorization {authorization.Id} for order {order.Id} is in state {authorization.State}; " +
                "only a SUCCEEDED authorization can be captured. Ask the shopper to re-authorize the order " +
                "or cancel it.");
        }

        if (authorization.IsVoid == true ||
            string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentConflictException(
                $"Authorization {authorization.Id} for order {order.Id} has been voided and can no longer be " +
                "captured. Ask the shopper to re-authorize the order or cancel it.");
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            throw new PaymentConflictException(
                $"Authorization {authorization.Id} for order {order.Id} expired at " +
                $"{authorization.ExpiresAt.Value:O} and can no longer be captured. Ask the shopper to " +
                "re-authorize the order or cancel it.");
        }
    }
}
