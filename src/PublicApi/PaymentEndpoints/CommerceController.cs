using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class CommerceController : ControllerBase
{
    private readonly ICommercePaymentService _service;
    public CommerceController(ICommercePaymentService service) => _service = service;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _service.CreateOrderAsync(BuyerId(), request.Items.ConvertAll(x =>
            new OrderLineInput(x.CatalogItemId, x.Quantity)), request.ShippingAddress.ToInput(), cancellationToken);
        return Created($"/api/my-orders", new CreateOrderResponse(order.OrderId, order));
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderView>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => Ok(await _service.PayAsync(orderId, BuyerId(),
            request.Card?.ToCard(), request.PaymentMethodId, cancellationToken));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderView>> Fulfil(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderView>> Cancel(int orderId, CancellationToken cancellationToken) =>
        Ok(await _service.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(CreateRefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateRefundResponse>> Refund(int orderId, RefundRequest request,
        CancellationToken cancellationToken)
    {
        var refund = await _service.RefundAsync(orderId, BuyerId(), request.Amount,
            request.IdempotencyKey, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new CreateRefundResponse(refund.RefundId, refund));
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderView>>> MyOrders(CancellationToken cancellationToken) =>
        Ok(await _service.GetOrdersAsync(BuyerId(), cancellationToken));

    [HttpPost("payment-methods")]
    [ProducesResponseType(typeof(CreatePaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatePaymentMethodResponse>> SavePaymentMethod(CardRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _service.SavePaymentMethodAsync(BuyerId(), request.ToCard(), cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new CreatePaymentMethodResponse(method.PaymentMethodId, method));
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodView>>> PaymentMethods(
        CancellationToken cancellationToken) => Ok(await _service.GetPaymentMethodsAsync(BuyerId(), cancellationToken));

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
    public async Task<ActionResult<ReconciliationView>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken) =>
        Ok(await _service.ReconcileAsync(from, to, cancellationToken));

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name)
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");
}

public sealed class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}
public sealed class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
    public ShippingAddressInput ToInput() => new(Street, City, State, Country, ZipCode);
}
public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}
public sealed class CardRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; set; } = new();
    public CardDetails ToCard() => new(Number, Expiry, SecurityCode, Name, BillingAddress.ToInput());
}
public sealed class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public BillingAddress ToInput() => new(AddressLine1, AddressLine2, City, State, PostalCode, CountryCode);
}
public sealed class RefundRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
public sealed record CreateOrderResponse(int OrderId, OrderView Order);
public sealed record CreatePaymentMethodResponse(int PaymentMethodId, PaymentMethodView PaymentMethod);
public sealed record CreateRefundResponse(int RefundId, RefundView Refund);
