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

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the
/// app's existing Order/OrderItem model. The shopper is told their order was placed.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class PlaceOrderEndpoint : EndpointBaseAsync
    .WithRequest<PlaceOrderRequest>
    .WithActionResult<PlaceOrderResponse>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public PlaceOrderEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    [HttpPost("api/orders")]
    [SwaggerOperation(
        Summary = "Places an order for the signed-in shopper",
        Description = "Places an order from catalog item ids and quantities",
        OperationId = "orders.place",
        Tags = new[] { "OrderEndpoints" })]
    public override async Task<ActionResult<PlaceOrderResponse>> HandleAsync(
        [FromBody] PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return BadRequest("An order must contain at least one item.");
        }

        var lines = request.Items.Select(i => new OrderLineItem(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is null
            ? new Address("1 Microsoft Way", "Redmond", "WA", "USA", "98052")
            : new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        try
        {
            var orderId = await _orderNotificationService.PlaceOrderAsync(buyerId, lines, address, cancellationToken);
            return Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
        }
        catch (System.ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
