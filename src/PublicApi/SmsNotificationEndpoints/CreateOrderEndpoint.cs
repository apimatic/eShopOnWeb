using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities (reusing the app's order
/// model), and tells the shopper (best-effort) that the order was placed. Returns the new order's id.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderMessagingService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var lines = (request?.Items ?? new())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                Address? address = request?.ShipToAddress is { } a
                    ? new Address(a.Street, a.City, a.State ?? string.Empty, a.Country, a.ZipCode)
                    : null;

                var order = await service.PlaceOrderAsync(buyerId, lines, address, ct);
                var response = new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total());
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotificationEndpoints");
    }
}
