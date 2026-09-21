using System.Collections.Generic;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper, and tells them it was placed.
/// Reuses the app's existing order/order-item model.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user, HttpContext http) =>
            {
                request.CallerId = user.Identity?.Name;
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, default);

    private static async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var items = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipTo is null
            ? null
            : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        // Invalid input surfaces as OrderPlacementException -> 400 via the exception middleware.
        var orderId = await service.PlaceOrderAsync(request.CallerId, items, shipTo, ct);
        return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
    }
}
