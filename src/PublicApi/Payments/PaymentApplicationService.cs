using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentApplicationService
{
    private readonly CatalogContext _context;
    private readonly IPayPalClient _payPal;
    private readonly PayPalOptions _options;
    private readonly PaymentOperationLock _operationLock;
    private readonly IUriComposer _uriComposer;

    public PaymentApplicationService(CatalogContext context, IPayPalClient payPal,
        IOptions<PayPalOptions> options, PaymentOperationLock operationLock, IUriComposer uriComposer)
    {
        _context = context;
        _payPal = payPal;
        _options = options.Value;
        _operationLock = operationLock;
        _uriComposer = uriComposer;
    }

    public async Task<PlaceOrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        _options.EnsureConfigured();
        if (request.Items.Count == 0)
            throw new PaymentValidationException("At least one catalog item is required.");
        if (request.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
            throw new PaymentValidationException("Catalog item IDs and quantities must be positive.");

        var requestedItems = request.Items
            .GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));
        var catalogItems = await _context.CatalogItems
            .Where(item => requestedItems.Keys.Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != requestedItems.Count)
            throw new PaymentValidationException("One or more catalog items do not exist.");

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, _uriComposer.ComposePicUri(item.PictureUri)),
            item.Price,
            requestedItems[item.Id])).ToList();
        var shipping = request.ShippingAddress ?? new ShippingAddressRequest();
        if (string.IsNullOrWhiteSpace(shipping.Street) || string.IsNullOrWhiteSpace(shipping.City) ||
            string.IsNullOrWhiteSpace(shipping.Country) || string.IsNullOrWhiteSpace(shipping.ZipCode))
            throw new PaymentValidationException("A complete shipping address is required when shippingAddress is supplied.");
        var order = new Order(buyerId,
            new Address(shipping.Street, shipping.City, shipping.State, shipping.Country, shipping.ZipCode),
            orderItems,
            _options.Currency.ToUpperInvariant());
        if (order.Total() <= 0)
            throw new PaymentValidationException("The order total must be positive.");
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return new PlaceOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            Currency = order.PaymentCurrency!,
            PaymentStatus = order.PaymentStatus.ToString()
        };
    }

    public async Task<OrderDto> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
            return Map(order);
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
            throw new PaymentConflictException($"Order {orderId} cannot be paid while its payment state is {order.PaymentStatus}.");
        if ((request.Card is null) == (request.PaymentMethodId is null))
            throw new PaymentValidationException("Supply either card details or paymentMethodId, but not both.");

        var paymentReference = order.PaymentReference
            ?? throw new PaymentConflictException("The order has no payment reference.");
        var requestId = PaymentOperationLock.RequestId($"order:{paymentReference}:authorize:v1");
        PayPalAuthorizationResult authorization;
        if (request.Card is not null)
        {
            authorization = await _payPal.AuthorizeCardAsync(paymentReference, order.Total(), order.PaymentCurrency!,
                request.Card, requestId, cancellationToken);
        }
        else
        {
            var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
            var paymentMethod = buyer?.PaymentMethods.SingleOrDefault(method => method.Id == request.PaymentMethodId);
            if (paymentMethod is null)
                throw new PaymentNotFoundException("The saved payment method does not exist.");
            authorization = await _payPal.AuthorizeSavedCardAsync(paymentReference, order.Total(), order.PaymentCurrency!,
                paymentMethod.VaultId, requestId, cancellationToken);
        }

        EnsureMoney(authorization.Amount, authorization.Currency, order.Total(), order.PaymentCurrency!);
        order.RecordAuthorization(authorization.OrderId, authorization.AuthorizationId, authorization.Status,
            authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
        await _context.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderDto> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled)
            return Map(order);

        if (order.PaymentStatus == OrderPaymentStatus.CapturePending && order.PayPalCaptureId is not null)
        {
            var currentCapture = await _payPal.GetCaptureAsync(order.PayPalCaptureId, cancellationToken);
            EnsureMoney(currentCapture.Amount, currentCapture.Currency, order.Total(), order.PaymentCurrency!);
            order.RecordCapture(currentCapture.CaptureId, currentCapture.Status, currentCapture.Amount,
                currentCapture.Fee, currentCapture.Net, currentCapture.CreatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            return Map(order);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.CurrentAuthorization is null)
            throw new PaymentConflictException($"Order {orderId} cannot be fulfilled while its payment state is {order.PaymentStatus}.");

        var authorization = order.CurrentAuthorization;
        PayPalAuthorizationResult processorAuthorization;
        try
        {
            processorAuthorization = await _payPal.GetAuthorizationAsync(authorization.PayPalAuthorizationId,
                cancellationToken);
        }
        catch (PayPalApiException exception)
        {
            throw new PaymentConflictException(
                "PayPal could not confirm that the authorization is still usable. Do not ship; ask the shopper " +
                $"to authorize payment again on a new order. {exception.Message}");
        }
        if (processorAuthorization.Status != "CREATED")
        {
            throw new PaymentConflictException(
                $"PayPal authorization {authorization.PayPalAuthorizationId} is {processorAuthorization.Status}. " +
                "Do not ship this order; ask the shopper to create a new payment authorization.");
        }

        if (authorization.CreatedAt.AddDays(3) <= DateTimeOffset.UtcNow)
        {
            if (DateTimeOffset.UtcNow >= authorization.ExpiresAt)
            {
                throw new PaymentConflictException(
                    "The PayPal authorization is outside its 29-day validity window and cannot be renewed. " +
                    "Do not ship; ask the shopper to authorize payment again on a new order.");
            }

            try
            {
                var renewed = await _payPal.ReauthorizeAsync(authorization.PayPalAuthorizationId, order.Total(),
                    order.PaymentCurrency!, PaymentOperationLock.RequestId($"order:{order.PaymentReference}:reauthorize:{authorization.PayPalAuthorizationId}"),
                    cancellationToken);
                EnsureMoney(renewed.Amount, renewed.Currency, order.Total(), order.PaymentCurrency!);
                order.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.Amount,
                    renewed.CreatedAt, renewed.ExpiresAt);
                await _context.SaveChangesAsync(cancellationToken);
                authorization = order.CurrentAuthorization!;
            }
            catch (PayPalApiException exception)
            {
                throw new PaymentConflictException(
                    "PayPal could not renew the stale authorization. Do not ship; ask the shopper to authorize " +
                    $"payment again on a new order. {exception.Message}");
            }
        }

        var capture = await _payPal.CaptureAsync(authorization.PayPalAuthorizationId, order.Total(),
            order.PaymentCurrency!, PaymentOperationLock.RequestId($"order:{order.PaymentReference}:capture:v1"), cancellationToken);
        EnsureMoney(capture.Amount, capture.Currency, order.Total(), order.PaymentCurrency!);
        order.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.Fee, capture.Net,
            capture.CreatedAt);
        await _context.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderDto> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operation = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            return Map(order);
        if (order.PaymentStatus == OrderPaymentStatus.AwaitingPayment)
        {
            order.CancelWithoutAuthorization();
            await _context.SaveChangesAsync(cancellationToken);
            return Map(order);
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.CurrentAuthorization is null)
            throw new PaymentConflictException($"Order {orderId} cannot be cancelled while its payment state is {order.PaymentStatus}.");

        var status = await _payPal.VoidAsync(order.CurrentAuthorization.PayPalAuthorizationId,
            PaymentOperationLock.RequestId($"order:{order.PaymentReference}:void:v1"), cancellationToken);
        order.RecordVoid(status);
        await _context.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 128)
            throw new PaymentValidationException("idempotencyKey is required and must be at most 128 characters.");
        if (request.Amount is { } requestedAmount &&
            (requestedAmount <= 0 || decimal.Round(requestedAmount, 2) != requestedAmount))
            throw new PaymentValidationException("Refund amount must be positive with no more than two decimal places.");

        using var operation = await _operationLock.AcquireAsync(orderId, cancellationToken);
        var order = await LoadOrderAsync(orderId, cancellationToken);
        EnsureOwner(order, buyerId);
        var existing = order.FindRefund(request.IdempotencyKey);
        if (existing is not null)
            return Map(existing, order.PaymentCurrency!);
        if (order.PaymentStatus is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded) ||
            order.PayPalCaptureId is null)
            throw new PaymentConflictException($"Order {orderId} has no captured payment available to refund.");

        var amount = request.Amount ?? order.RefundableAmount();
        if (amount > order.RefundableAmount())
            throw new PaymentConflictException($"Only {order.RefundableAmount():0.00} {order.PaymentCurrency} remains refundable.");

        var refund = await _payPal.RefundAsync(order.PayPalCaptureId, amount, order.PaymentCurrency!,
            PaymentOperationLock.RequestId($"capture:{order.PayPalCaptureId}:refund:{request.IdempotencyKey}"),
            cancellationToken);
        EnsureMoney(refund.Amount, refund.Currency, amount, order.PaymentCurrency!);
        order.RecordRefund(refund.RefundId, request.IdempotencyKey, refund.Status, refund.Amount, refund.CreatedAt);
        await _context.SaveChangesAsync(cancellationToken);
        return Map(refund);
    }

    public async Task<IReadOnlyCollection<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _context.Orders
            .Where(order => order.BuyerId == buyerId)
            .Include(order => order.OrderItems)
            .Include(order => order.PaymentAuthorizations)
            .Include(order => order.PaymentRefunds)
            .AsSplitQuery()
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(Map).ToList();
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (request.Alias?.Length > 64)
            throw new PaymentValidationException("Payment method alias must be at most 64 characters.");
        var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
        var existingCustomerId = buyer?.PaymentMethods.FirstOrDefault()?.CustomerId;
        var vaulted = await _payPal.VaultCardAsync(PaymentOperationLock.RequestId($"buyer:{buyerId}"),
            existingCustomerId, request.Card,
            PaymentOperationLock.RequestId($"buyer:{buyerId}:vault:{Guid.NewGuid():N}"), cancellationToken);

        buyer ??= new Buyer(buyerId);
        if (buyer.Id == 0)
            _context.Buyers.Add(buyer);
        var method = buyer.AddPaymentMethod(vaulted.VaultId, vaulted.CustomerId, vaulted.Brand,
            vaulted.Last4, vaulted.Expiry, request.Alias);
        await _context.SaveChangesAsync(cancellationToken);
        return new SavePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Alias = method.Alias,
            Brand = method.Brand,
            Last4 = method.Last4,
            Expiry = method.Expiry
        };
    }

    public async Task<IReadOnlyCollection<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
        return buyer?.PaymentMethods.Select(Map).ToList() ?? new List<PaymentMethodDto>();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var buyer = await LoadBuyerAsync(buyerId, cancellationToken);
        var method = buyer?.PaymentMethods.SingleOrDefault(item => item.Id == paymentMethodId);
        if (buyer is null || method is null)
            throw new PaymentNotFoundException("The saved payment method does not exist.");

        await _payPal.DeletePaymentTokenAsync(method.VaultId, cancellationToken);
        buyer.RemovePaymentMethod(paymentMethodId);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
            throw new PaymentValidationException("from must be earlier than or equal to to.");

        var payPalTransactions = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _context.Orders
            .Where(order =>
                order.PaymentAuthorizations.Any(item => item.CreatedAt >= from && item.CreatedAt <= to) ||
                order.CapturedAt >= from && order.CapturedAt <= to ||
                order.PaymentRefunds.Any(item => item.CreatedAt >= from && item.CreatedAt <= to))
            .Include(order => order.PaymentAuthorizations)
            .Include(order => order.PaymentRefunds)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var externalIdToOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var localEvents = new List<ReconciliationEntry>();
        foreach (var order in orders)
        {
            if (order.PayPalOrderId is not null)
                externalIdToOrder[order.PayPalOrderId] = order.Id;
            foreach (var authorization in order.PaymentAuthorizations.Where(item => item.CreatedAt >= from && item.CreatedAt <= to))
            {
                externalIdToOrder[authorization.PayPalAuthorizationId] = order.Id;
                localEvents.Add(LocalEntry(order.Id, authorization.PayPalAuthorizationId, "AUTHORIZATION",
                    authorization.Status, authorization.CreatedAt, authorization.Amount, order.PaymentCurrency));
            }
            if (order.PayPalCaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
            {
                externalIdToOrder[order.PayPalCaptureId] = order.Id;
                localEvents.Add(LocalEntry(order.Id, order.PayPalCaptureId, "CAPTURE",
                    order.PayPalCaptureStatus ?? string.Empty, order.CapturedAt.Value, order.CapturedAmount,
                    order.PaymentCurrency));
            }
            foreach (var refund in order.PaymentRefunds.Where(item => item.CreatedAt >= from && item.CreatedAt <= to))
            {
                externalIdToOrder[refund.PayPalRefundId] = order.Id;
                localEvents.Add(LocalEntry(order.Id, refund.PayPalRefundId, "REFUND", refund.Status,
                    refund.CreatedAt, refund.Amount, order.PaymentCurrency));
            }
        }

        var paypalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var response = new ReconciliationResponse { From = from, To = to };
        foreach (var transaction in payPalTransactions)
        {
            paypalIds.Add(transaction.TransactionId);
            if (transaction.ReferenceId is not null)
                paypalIds.Add(transaction.ReferenceId);
            int? orderId = null;
            if (externalIdToOrder.TryGetValue(transaction.TransactionId, out var directOrderId))
                orderId = directOrderId;
            else if (transaction.ReferenceId is not null && externalIdToOrder.TryGetValue(transaction.ReferenceId, out var referenceOrderId))
                orderId = referenceOrderId;
            response.Entries.Add(new ReconciliationEntry
            {
                MatchStatus = orderId is null ? "PayPalOnly" : "Matched",
                OrderId = orderId,
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                TransactionType = transaction.EventCode,
                Status = transaction.Status,
                OccurredAt = transaction.InitiatedAt,
                Amount = transaction.Amount,
                Fee = transaction.Fee,
                Currency = transaction.Currency
            });
        }
        response.Entries.AddRange(localEvents.Where(item => !paypalIds.Contains(item.TransactionId)));
        return response;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _context.Orders
            .Include(order => order.OrderItems)
            .Include(order => order.PaymentAuthorizations)
            .Include(order => order.PaymentRefunds)
            .AsSplitQuery()
            .SingleOrDefaultAsync(order => order.Id == orderId, cancellationToken)
        ?? throw new PaymentNotFoundException($"Order {orderId} does not exist.");

    private Task<Buyer?> LoadBuyerAsync(string buyerId, CancellationToken cancellationToken) =>
        _context.Buyers.Include(buyer => buyer.PaymentMethods)
            .SingleOrDefaultAsync(buyer => buyer.IdentityGuid == buyerId, cancellationToken);

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Order {order.Id} does not exist.");
    }

    private static void EnsureMoney(decimal actualAmount, string actualCurrency, decimal expectedAmount,
        string expectedCurrency)
    {
        if (actualAmount != expectedAmount || !string.Equals(actualCurrency, expectedCurrency,
                StringComparison.OrdinalIgnoreCase))
            throw new PaymentConflictException("PayPal returned an amount or currency different from the eShop order.");
    }

    private static OrderDto Map(Order order) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = order.Total(),
        Currency = order.PaymentCurrency,
        PaymentStatus = order.PaymentStatus.ToString(),
        FulfilmentStatus = order.FulfilmentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        PayPalCaptureId = order.PayPalCaptureId,
        PayPalCaptureStatus = order.PayPalCaptureStatus,
        CapturedAmount = order.CapturedAmount,
        PayPalFee = order.PayPalFee,
        NetProceeds = order.NetProceeds,
        RefundedAmount = order.RefundedAmount,
        RefundableAmount = order.RefundableAmount(),
        Items = order.OrderItems.Select(item => new OrderItemDto
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Quantity = item.Units
        }).ToList(),
        Authorizations = order.PaymentAuthorizations.Select(item => new AuthorizationDto
        {
            AuthorizationId = item.PayPalAuthorizationId,
            Status = item.Status,
            Amount = item.Amount,
            CreatedAt = item.CreatedAt,
            ExpiresAt = item.ExpiresAt,
            IsCurrent = item.IsCurrent
        }).ToList(),
        Refunds = order.PaymentRefunds.Select(item => new RefundDto
        {
            RefundId = item.PayPalRefundId,
            Status = item.Status,
            Amount = item.Amount,
            CreatedAt = item.CreatedAt
        }).ToList()
    };

    private static PaymentMethodDto Map(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Alias = method.Alias,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry
    };

    private static RefundOrderResponse Map(PaymentRefund refund, string currency) => new()
    {
        RefundId = refund.PayPalRefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = currency
    };

    private static RefundOrderResponse Map(PayPalRefundResult refund) => new()
    {
        RefundId = refund.RefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency
    };

    private static ReconciliationEntry LocalEntry(int orderId, string transactionId, string type,
        string status, DateTimeOffset occurredAt, decimal? amount, string? currency) => new()
        {
            MatchStatus = "EShopOnly",
            OrderId = orderId,
            TransactionId = transactionId,
            TransactionType = type,
            Status = status,
            OccurredAt = occurredAt,
            Amount = amount,
            Currency = currency
        };
}
