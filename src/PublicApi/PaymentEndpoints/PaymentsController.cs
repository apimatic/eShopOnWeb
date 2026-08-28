using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.Infrastructure.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Route("api")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private readonly IPaymentApplicationService _payments;

    public PaymentsController(IPaymentApplicationService payments) => _payments = payments;

    [HttpPost("orders")]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreateOrderResponse>> CreateOrder(CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await _payments.CreateOrderAsync(BuyerId(),
            request.Items.Select(x => new CreateOrderLine(x.CatalogItemId, x.Quantity)).ToList(),
            new ShippingAddressData(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode),
            cancellationToken);
        return Created($"/api/orders/{order.OrderId}", new CreateOrderResponse(order.OrderId, order));
    }

    [HttpPost("orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderView>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
        => Ok(await _payments.PayAsync(BuyerId(), orderId, request.Card?.ToData(),
            request.PaymentMethodId, cancellationToken));

    [HttpPost("orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderView>> Fulfil(int orderId, CancellationToken cancellationToken)
        => Ok(await _payments.FulfilAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderView>> Cancel(int orderId, CancellationToken cancellationToken)
        => Ok(await _payments.CancelAsync(orderId, cancellationToken));

    [HttpPost("orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundResultView>> Refund(int orderId, RefundRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _payments.RefundAsync(BuyerId(), orderId, request.Amount,
            request.IdempotencyKey, cancellationToken);
        return Ok(result);
    }

    [HttpGet("my-orders")]
    public async Task<ActionResult<IReadOnlyList<OrderView>>> MyOrders(CancellationToken cancellationToken)
        => Ok(await _payments.GetOrdersAsync(BuyerId(), cancellationToken));

    [HttpPost("payment-methods")]
    [ProducesResponseType(typeof(CreatePaymentMethodResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatePaymentMethodResponse>> SavePaymentMethod(CardRequest request,
        CancellationToken cancellationToken)
    {
        var method = await _payments.SavePaymentMethodAsync(BuyerId(), request.ToData(), cancellationToken);
        return Created($"/api/payment-methods/{method.PaymentMethodId}",
            new CreatePaymentMethodResponse(method.PaymentMethodId, method.Brand, method.Last4, method.Expiry));
    }

    [HttpGet("payment-methods")]
    public async Task<ActionResult<IReadOnlyList<PaymentMethodView>>> PaymentMethods(CancellationToken cancellationToken)
        => Ok(await _payments.GetPaymentMethodsAsync(BuyerId(), cancellationToken));

    [HttpDelete("payment-methods/{paymentMethodId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        await _payments.DeletePaymentMethodAsync(BuyerId(), paymentMethodId, cancellationToken);
        return NoContent();
    }

    [HttpGet("reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationView>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
        => Ok(await _payments.ReconcileAsync(from, to, cancellationToken));

    private string BuyerId() => User.Identity?.Name
        ?? throw new UnauthorizedAccessException("The bearer token does not contain a name claim.");
}

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    [Required]
    public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required, MaxLength(180)] public string Street { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; set; } = string.Empty;
    [MaxLength(60)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(90)] public string Country { get; set; } = string.Empty;
    [Required, MaxLength(18)] public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class CardRequest
{
    [Required, RegularExpression("^[0-9 ]{13,23}$")] public string Number { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{4}-(0[1-9]|1[0-2])$")] public string Expiry { get; set; } = string.Empty;
    [Required, RegularExpression("^[0-9]{3,4}$")] public string SecurityCode { get; set; } = string.Empty;
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required] public BillingAddressRequest BillingAddress { get; set; } = new();

    public PaymentCardData ToData() => new(Number, Expiry, SecurityCode, Name,
        new BillingAddressData(BillingAddress.AddressLine1, BillingAddress.AddressLine2,
            BillingAddress.City, BillingAddress.State, BillingAddress.PostalCode,
            BillingAddress.CountryCode.ToUpperInvariant()));
}

public sealed class BillingAddressRequest
{
    [Required, MaxLength(300)] public string AddressLine1 { get; set; } = string.Empty;
    [MaxLength(300)] public string? AddressLine2 { get; set; }
    [Required, MaxLength(120)] public string City { get; set; } = string.Empty;
    [MaxLength(300)] public string State { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z]{2}$")] public string CountryCode { get; set; } = string.Empty;
}

public sealed class RefundRequest
{
    [Range(typeof(decimal), "0.01", "999999999.99")] public decimal? Amount { get; set; }
    [Required, StringLength(108, MinimumLength = 1), RegularExpression("^[A-Za-z0-9._-]+$")]
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed record CreateOrderResponse(int OrderId, OrderView Order);
public sealed record CreatePaymentMethodResponse(int PaymentMethodId, string Brand, string Last4, string Expiry);
