using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly PaymentWorkflowService _workflow;
    public PaymentsController(PaymentWorkflowService workflow) => _workflow = workflow;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _workflow.CreateOrderAsync(OwnerId(), request, cancellationToken);
        var response = new CreateOrderResponse(order.Id, ToResponse(order));
        return Created($"/api/orders/{order.Id}", response);
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) =>
        ToResponse(await _workflow.PayAsync(orderId, OwnerId(), request, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        ToResponse(await _workflow.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken) =>
        ToResponse(await _workflow.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(RefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<RefundResponse>> Refund(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var (refund, currency) = await _workflow.RefundAsync(orderId, OwnerId(), request, cancellationToken);
        return Created($"/api/orders/{orderId}/refunds/{refund.PayPalRefundId}",
            new RefundResponse(refund.PayPalRefundId, refund.Status, refund.Amount, currency));
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyCollection<OrderResponse>>> MyOrders(CancellationToken cancellationToken) =>
        (await _workflow.GetMyOrdersAsync(OwnerId(), cancellationToken)).Select(ToResponse).ToArray();

    [HttpPost("api/payment-methods")]
    [ProducesResponseType(typeof(SavePaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<SavePaymentMethodResponse>> SavePaymentMethod(SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var saved = await _workflow.SavePaymentMethodAsync(OwnerId(), request, cancellationToken);
        var response = new SavePaymentMethodResponse(saved.Id, ToResponse(saved));
        return Created($"/api/payment-methods/{saved.Id}", response);
    }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken) =>
        (await _workflow.GetPaymentMethodsAsync(OwnerId(), cancellationToken)).Select(ToResponse).ToArray();

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _workflow.DeletePaymentMethodAsync(paymentMethodId, OwnerId(), cancellationToken);
        return NoContent();
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public Task<ReconciliationResponse> Reconciliation([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from is null || to is null) throw ApiOperationException.BadRequest("Both from and to are required ISO-8601 date-times.");
        return _workflow.ReconcileAsync(from.Value, to.Value, cancellationToken);
    }

    private string OwnerId() => (User.FindFirstValue(ClaimTypes.Name)
        ?? throw ApiOperationException.BadRequest("The access token does not contain a user identity."))
        .Trim().ToUpperInvariant();

    private static PaymentMethodResponse ToResponse(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.CreatedAt);

    private static OrderResponse ToResponse(Order order)
    {
        var items = order.OrderItems.Select(x => new OrderLineResponse(x.ItemOrdered.CatalogItemId,
            x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray();
        PaymentView? payment = null;
        if (order.Payment is { } p)
        {
            payment = new PaymentView(p.Status.ToString(), p.Amount, p.Currency, p.PayPalOrderId,
                p.PayPalAuthorizationId, p.PayPalAuthorizationStatus, p.AuthorizationExpiresAt,
                p.PayPalCaptureId, p.PayPalCaptureStatus, p.CapturedAmount, p.PayPalFee, p.NetProceeds,
                p.RefundedAmount, p.RefundableAmount, p.Refunds.Select(x => new RefundView(x.PayPalRefundId,
                    x.Status, x.Amount, x.CreatedAt)).ToArray());
        }
        return new OrderResponse(order.Id, order.ExternalId, order.OrderDate, order.FulfilmentStatus.ToString(),
            order.Total(), items, payment);
    }
}
