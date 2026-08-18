using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items, reusing the app's existing
/// order model, then messages the shopper that it was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderCommand, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var buyerId = user.UserName();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                if (request?.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "An order must contain at least one item." });
                }
                return await HandleAsync(new PlaceOrderCommand(buyerId, request.Items, request.ShippingAddress), service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderCommand request, IOrderNotificationService service)
    {
        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ShippingAddress is null
            ? null
            : new ShippingAddressRequest(request.ShippingAddress.Street, request.ShippingAddress.City,
                request.ShippingAddress.State, request.ShippingAddress.Country, request.ShippingAddress.ZipCode);

        try
        {
            var orderId = await service.PlaceOrderAsync(request.BuyerId, lines, address);
            var response = new PlaceOrderResponse { OrderId = orderId, Message = "Order placed." };
            return Results.Created($"api/orders/{orderId}", response);
        }
        catch (OrderPlacementException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
