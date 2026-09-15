using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api/orders")]
public sealed class OrdersController : ControllerBase
{
    private readonly ICommercePaymentService _payments;
    public OrdersController(ICommercePaymentService payments) => _payments = payments;

    [HttpPost]
    [ProducesResponseType<OrderResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<OrderResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var address = new Address(request.ShippingAddress.Street, request.ShippingAddress.City,
            request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);
        var order = await _payments.PlaceOrderAsync(BuyerId(), request.Items.Select(x =>
            new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(), address, cancellationToken);
        var response = PaymentResponseMapper.Order(order);
        return Created($"/api/orders/{order.Id}", response);
    }

    [HttpPost("{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _payments.AuthorizeAsync(BuyerId(), orderId, request.Card?.ToData(),
            request.PaymentMethodId, cancellationToken);
        return Ok(PaymentResponseMapper.Order(order));
    }

    [HttpPost("{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.FulfilAsync(orderId, cancellationToken);
        return Ok(PaymentResponseMapper.Order(order));
    }

    [HttpPost("{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var order = await _payments.CancelAsync(orderId, cancellationToken);
        return Ok(PaymentResponseMapper.Order(order));
    }

    [HttpPost("{orderId:int}/refunds")]
    [ProducesResponseType<RefundResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(BuyerId(), orderId, request.Amount,
            request.IdempotencyKey, cancellationToken);
        var response = PaymentResponseMapper.Refund(refund);
        return Created($"/api/orders/{orderId}/refunds/{refund.PayPalRefundId}", response);
    }

    [HttpGet("/api/my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var orders = await _payments.GetOrdersAsync(BuyerId(), cancellationToken);
        return Ok(orders.Select(PaymentResponseMapper.Order).ToList());
    }

    private string BuyerId() => User.Identity?.Name ??
        throw new UnauthorizedAccessException("The bearer token does not contain a name claim.");
}
