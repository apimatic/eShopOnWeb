using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.Payments;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[Route("api")]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentApplicationService _service;

    public PaymentsController(PaymentApplicationService service) => _service = service;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _service.CreateOrderAsync(BuyerId(), request, cancellationToken);
        var response = new CreateOrderResponse(order.Id, Map(order));
        return Created($"/api/orders/{order.Id}", response);
    }

    [HttpPost("orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _service.PayAsync(orderId, BuyerId(), request, cancellationToken);
        var response = Map(order);
        return order.PaymentStatus == OrderPaymentStatus.AuthorizationPending
            ? Accepted(response)
            : Ok(response);
    }

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status202Accepted)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId,
        CancellationToken cancellationToken)
    {
        var order = await _service.FulfilAsync(orderId, cancellationToken);
        var response = Map(order);
        return order.PaymentStatus is OrderPaymentStatus.CapturePending or
            OrderPaymentStatus.AuthorizationPending
            ? Accepted(response)
            : Ok(response);
    }

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId,
        CancellationToken cancellationToken) =>
        Ok(Map(await _service.CancelAsync(orderId, cancellationToken)));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(CreateRefundResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CreateRefundResponse>> Refund(int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _service.RefundAsync(orderId, BuyerId(), request, cancellationToken);
        return Ok(new CreateRefundResponse(result.Refund.PayPalRefundId, Map(result.Order)));
    }

    [HttpGet("my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderResponse>>> MyOrders(
        CancellationToken cancellationToken) =>
        Ok((await _service.MyOrdersAsync(BuyerId(), cancellationToken)).Select(Map).ToList());

    [HttpPost("payment-methods")]
    [ProducesResponseType(typeof(CreatePaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatePaymentMethodResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        var method = await _service.SavePaymentMethodAsync(BuyerId(), request.Card.ToGatewayModel(),
            idempotencyKey, cancellationToken);
        var response = new CreatePaymentMethodResponse(method.Id, Map(method));
        return Created($"/api/payment-methods/{method.Id}", response);
    }

    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken) =>
        Ok((await _service.PaymentMethodsAsync(BuyerId(), cancellationToken)).Select(Map).ToList());

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await _service.DeletePaymentMethodAsync(paymentMethodId, BuyerId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(ReconciliationResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        CancellationToken cancellationToken) =>
        Ok(await _service.ReconcileAsync(from, to, cancellationToken));

    private string BuyerId() => User.Identity?.Name ??
        throw new PaymentOperationException(401, "identity_missing",
            "The bearer token does not contain a shopper identity.");

    private static OrderResponse Map(Order order)
    {
        decimal? refundable = order.CapturedAmount is { } captured
            ? Math.Max(0, captured - order.RefundedAmount)
            : null;
        return new OrderResponse(
            order.Id, order.OrderDate, order.PaymentStatus.ToString(), order.Total(), order.Currency,
            order.PayPalOrderId, order.AuthorizationId, order.AuthorizationStatus,
            order.AuthorizationExpiresAt, order.CaptureId, order.CaptureStatus,
            order.CapturedAmount, order.PayPalFee, order.NetProceeds, order.RefundedAmount,
            refundable,
            order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(),
            order.Refunds.Select(x => new RefundResponse(x.PayPalRefundId, x.Status, x.Amount,
                x.Currency, x.CreatedAt)).ToList());
    }

    private static PaymentMethodResponse Map(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.LastDigits, method.Expiry,
            method.CardholderName, method.CreatedAt);
}
