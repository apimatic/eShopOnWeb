using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// POST /api/orders — places an order for the signed-in shopper from catalog item ids + quantities, reusing
/// the app's existing order/order-item model. The shopper is told their order was placed.
/// </summary>
public class CreateOrderEndpoint
    : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderNotificationService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request?.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one order item is required." });
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShipToAddress is null
            ? null
            : new ShippingAddress(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var result = await service.PlaceOrderAsync(buyerId, lines, address, http.RequestAborted);
        if (!result.Success)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        return Results.Created($"api/orders/{result.OrderId}", new CreateOrderResponse { OrderId = result.OrderId });
    }
}
