using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.PublicApi.Configuration;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();

    /// <summary>Optional shipping address; a placeholder is used when omitted (no storefront UI).</summary>
    public AddressDto? ShipToAddress { get; set; }
}

public class CreateOrderResponse
{
    /// <summary>Top-level identifier of the created order, so the flow can be driven end to end.</summary>
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// Places an order from catalog item ids + quantities for the signed-in shopper. The order starts
/// awaiting payment; the caller then drives <c>/pay</c>, <c>/fulfil</c>, etc.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class CreateOrderEndpoint : EndpointBaseAsync
    .WithRequest<CreateOrderRequest>
    .WithActionResult<CreateOrderResponse>
{
    private readonly IOrderPaymentService _orderPaymentService;
    private readonly ApplicationCore.Models.PayPal.PayPalSettings _settings;

    public CreateOrderEndpoint(IOrderPaymentService orderPaymentService,
        ApplicationCore.Models.PayPal.PayPalSettings settings)
    {
        _orderPaymentService = orderPaymentService;
        _settings = settings;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(
        Summary = "Places an order for the signed-in shopper",
        Description = "Creates an order from catalog item ids and quantities; the order awaits payment.",
        OperationId = "orders.create",
        Tags = new[] { "OrderPaymentEndpoints" })]
    public override async Task<ActionResult<CreateOrderResponse>> HandleAsync(
        [FromBody] CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity));

        var address = ToAddress(request.ShipToAddress);
        var orderId = await _orderPaymentService.PlaceOrderAsync(buyerId, lines, address, cancellationToken);

        var orders = await _orderPaymentService.GetOrdersForBuyerAsync(buyerId, cancellationToken);
        var order = orders.First(o => o.Id == orderId);

        var response = new CreateOrderResponse
        {
            OrderId = orderId,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = _settings.Currency
        };
        return Created($"api/orders/{orderId}", response);
    }

    private static Address ToAddress(AddressDto? dto)
    {
        if (dto is null)
        {
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        }
        return new Address(
            string.IsNullOrWhiteSpace(dto.Street) ? "N/A" : dto.Street,
            string.IsNullOrWhiteSpace(dto.City) ? "N/A" : dto.City,
            dto.State ?? "N/A",
            string.IsNullOrWhiteSpace(dto.Country) ? "N/A" : dto.Country,
            string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);
    }
}
