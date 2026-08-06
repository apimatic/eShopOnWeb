using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items and quantities.
/// The order starts awaiting payment. (Flow 1)
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PlaceOrderEndpoint : EndpointBaseAsync
    .WithRequest<PlaceOrderRequest>
    .WithActionResult<PlaceOrderResponse>
{
    private readonly IOrderService _orderService;

    public PlaceOrderEndpoint(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(
        Summary = "Places an order from catalog items for the signed-in shopper",
        Description = "Creates an order (awaiting payment) using catalog prices in USD. Returns the new orderId.",
        OperationId = "orders.place",
        Tags = new[] { "PaymentEndpoints" })]
    public override async Task<ActionResult<PlaceOrderResponse>> HandleAsync(
        [FromBody] PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest("At least one order line is required.");
        }

        var address = MapAddress(request.ShipToAddress);
        var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity));

        var order = await _orderService.CreateOrderFromItemsAsync(buyerId, address, items);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = order.ToSummary()
        };

        return Created($"api/orders/{order.Id}", response);
    }

    private static Address MapAddress(AddressDto? dto)
    {
        if (dto is null)
        {
            // The existing Order model requires a ship-to address; use a placeholder when the
            // caller does not supply one (this API is payment-focused, not shipping-focused).
            return new Address("N/A", "N/A", "N/A", "US", "00000");
        }

        return new Address(
            street: string.IsNullOrWhiteSpace(dto.Street) ? "N/A" : dto.Street,
            city: string.IsNullOrWhiteSpace(dto.City) ? "N/A" : dto.City,
            state: dto.State,
            country: string.IsNullOrWhiteSpace(dto.Country) ? "US" : dto.Country,
            zipcode: string.IsNullOrWhiteSpace(dto.ZipCode) ? "00000" : dto.ZipCode);
    }
}
