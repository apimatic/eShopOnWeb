using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly CatalogContext _db;
    private readonly PaymentService _payments;
    private readonly PayPalOptions _options;

    public OrdersController(CatalogContext db, PaymentService payments, IOptions<PayPalOptions> options)
    {
        _db = db;
        _payments = payments;
        _options = options.Value;
    }

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(OrderResponse), (int)HttpStatusCode.Created)]
    public async Task<ActionResult<OrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0 || request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            throw new PaymentWorkflowException(HttpStatusCode.BadRequest,
                "At least one catalog item with a positive quantity is required.");
        if (request.ShippingAddress is null || string.IsNullOrWhiteSpace(request.ShippingAddress.Street) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.City) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.Country) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.ZipCode))
            throw new PaymentWorkflowException(HttpStatusCode.BadRequest,
                "A complete shipping address is required.");

        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Values.Any(x => x > 1000))
            throw new PaymentWorkflowException(HttpStatusCode.BadRequest,
                "An order line quantity cannot exceed 1000.");
        var catalogItems = await _db.CatalogItems.Where(x => requested.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missing.Length > 0)
            throw new PaymentWorkflowException(HttpStatusCode.BadRequest,
                $"Catalog item(s) not found: {string.Join(", ", missing)}.");

        var address = request.ShippingAddress;
        var lines = catalogItems.Select(x => new OrderItem(
            new CatalogItemOrdered(x.Id, x.Name, x.PictureUri ?? string.Empty), x.Price, requested[x.Id]))
            .ToList();
        var order = new Order(BuyerId(),
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode), lines);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        var response = Map(order, _options.Currency);
        return Created($"/api/orders/{order.Id}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        Ok(Map(await _payments.PayAsync(orderId, BuyerId(), request.Card, request.PaymentMethodId,
            cancellationToken), _options.Currency));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(Map(await _payments.FulfilAsync(orderId, cancellationToken), _options.Currency));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(Map(await _payments.CancelAsync(orderId, cancellationToken), _options.Currency));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), (int)HttpStatusCode.OK)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(orderId, BuyerId(), request.Amount,
            request.IdempotencyKey, cancellationToken);
        return Ok(Map(refund));
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var orders = await _db.Orders.AsNoTracking().Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Refunds).OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return Ok(orders.Select(x => Map(x, _options.Currency)).ToList());
    }

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name) ??
        throw new PaymentWorkflowException(HttpStatusCode.Unauthorized, "The token has no user identity.");

    internal static OrderResponse Map(Order order, string configuredCurrency) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        Total = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero),
        Currency = order.PaymentCurrency ?? configuredCurrency,
        PaymentStatus = order.PaymentStatus.ToString(),
        FulfillmentStatus = order.FulfillmentStatus.ToString(),
        PayPalOrderId = order.PayPalOrderId,
        AuthorizationId = order.PayPalAuthorizationId,
        AuthorizationStatus = order.PayPalAuthorizationStatus,
        AuthorizationExpiresAt = order.AuthorizationExpiresAt,
        CaptureId = order.PayPalCaptureId,
        CaptureStatus = order.PayPalCaptureStatus,
        CapturedAmount = order.CapturedAmount,
        PayPalFee = order.PayPalFee,
        NetProceeds = order.NetProceeds,
        RefundedAmount = order.RefundedTotal(),
        Items = order.OrderItems.Select(x => new OrderLineResponse
        {
            CatalogItemId = x.ItemOrdered.CatalogItemId,
            ProductName = x.ItemOrdered.ProductName,
            UnitPrice = x.UnitPrice,
            Quantity = x.Units
        }).ToList(),
        Refunds = order.Refunds.Where(x => x.PayPalRefundId is not null).Select(Map).ToList()
    };

    internal static RefundResponse Map(PaymentRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId ?? string.Empty,
        Status = refund.Status,
        Amount = refund.Amount,
        RefundedPayPalFee = refund.RefundedPayPalFee,
        MerchantNetDebit = refund.MerchantNetDebit
    };
}
