using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items (ids + quantities). Reuses the app's existing
/// order/order-item model; the caller's identity comes from the token. The shopper is told the order
/// was placed. Returns the new order's id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = (request.Items ?? new List<PlaceOrderLine>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        var result = await service.PlaceOrderAsync(request.BuyerId, lines, address);

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            Order = NotificationMapping.ToDto(result)
        };
        return Results.Created($"api/orders/{result.Order.Id}", response);
    }

    private static Address BuildAddress(ShipToAddressDto? dto) => new(
        string.IsNullOrWhiteSpace(dto?.Street) ? "N/A" : dto!.Street!,
        string.IsNullOrWhiteSpace(dto?.City) ? "N/A" : dto!.City!,
        string.IsNullOrWhiteSpace(dto?.State) ? "N/A" : dto!.State!,
        string.IsNullOrWhiteSpace(dto?.Country) ? "N/A" : dto!.Country!,
        string.IsNullOrWhiteSpace(dto?.ZipCode) ? "00000" : dto!.ZipCode!);
}
