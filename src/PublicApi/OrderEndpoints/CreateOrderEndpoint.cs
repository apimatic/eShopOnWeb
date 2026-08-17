using System;
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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities, reusing the app's
/// existing order model. The shopper is told their order was placed (best-effort — a failed message
/// never fails the order).
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service) =>
                await HandleAsync(request, user, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "An order must contain at least one item." });
        }

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var address = BuildAddress(request.ShipToAddress);

        try
        {
            var order = await service.PlaceOrderAsync(buyerId, lines, address);
            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        // The notification flow does not depend on a real address; default the fields the order model
        // requires so a shopper can place an order with item ids alone.
        return new Address(
            Fallback(dto?.Street),
            Fallback(dto?.City),
            Fallback(dto?.State),
            Fallback(dto?.Country),
            Fallback(dto?.ZipCode));
    }

    private static string Fallback(string? value) => string.IsNullOrWhiteSpace(value) ? "N/A" : value;
}
