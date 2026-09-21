using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items (reusing the existing order model),
/// and tells them it was placed. The identity comes from the token.
/// </summary>
public class PlaceOrderEndpoint
    : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service,
             ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        PlaceOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        var lines = request.Items
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, ct);

        var response = new PlaceOrderResponse(order.Id, order.Total(), order.OrderItems.Count);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
