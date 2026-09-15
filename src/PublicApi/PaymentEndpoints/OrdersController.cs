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
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class OrdersController : ControllerBase
{
    private readonly IPaymentService _payments;

    public OrdersController(IPaymentService payments) => _payments = payments;

    [HttpPost("api/orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var lines = request.Items.ConvertAll(x =>
            new OrderLineInput(x.CatalogItemId, x.Quantity));
        var address = new ShippingAddressInput(
            request.ShippingAddress.Street,
            request.ShippingAddress.City,
            request.ShippingAddress.State,
            request.ShippingAddress.Country,
            request.ShippingAddress.ZipCode);
        var order = await _payments.PlaceOrderAsync(BuyerId(), lines, address,
            cancellationToken);
        return Created("/api/my-orders", new CreateOrderResponse(order.OrderId, order));
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    [ProducesResponseType(typeof(OrderView), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderView>> Pay(int orderId, [FromBody] PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        CardInput? card = request.Card is null ? null : ToCard(request.Card);
        return Ok(await _payments.PayAsync(BuyerId(), orderId,
            new PayOrderInput(card, request.PaymentMethodId), cancellationToken));
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderView), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderView>> Fulfil(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _payments.FulfilAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(typeof(OrderView), StatusCodes.Status200OK)]
    public async Task<ActionResult<OrderView>> Cancel(int orderId,
        CancellationToken cancellationToken) =>
        Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("api/orders/{orderId:int}/refunds")]
    [ProducesResponseType(typeof(CreateRefundResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateRefundResponse>> Refund(int orderId,
        [FromBody] RefundRequest request, CancellationToken cancellationToken)
    {
        var refund = await _payments.RefundAsync(BuyerId(), orderId, request.Amount,
            request.IdempotencyKey, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new CreateRefundResponse(refund.RefundId, refund));
    }

    [HttpGet("api/my-orders")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderView>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<OrderView>>> MyOrders(
        CancellationToken cancellationToken) =>
        Ok(await _payments.GetOrdersAsync(BuyerId(), cancellationToken));

    private string BuyerId() => User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    internal static CardInput ToCard(CardRequest card) => new(
        card.Name,
        card.Number,
        card.Expiry,
        card.SecurityCode,
        new BillingAddressInput(
            card.BillingAddress.AddressLine1,
            card.BillingAddress.AddressLine2,
            card.BillingAddress.City,
            card.BillingAddress.State,
            card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));
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
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; set; } = new();
}

public sealed class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed class RefundRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, OrderView Order);
public sealed record CreateRefundResponse(string RefundId, RefundResult Refund);
