using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class PlaceOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
}

/// <summary>Places an order from catalog items for the signed-in shopper and notifies "order placed".</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderNotificationService service, System.Threading.CancellationToken ct) =>
            {
                var buyerId = CallerContext.GetUserName(http);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var lines = (request?.Items ?? new List<OrderLineDto>())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                try
                {
                    var orderId = await service.PlaceOrderAsync(buyerId, lines, ct);
                    return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId });
                }
                catch (NotificationValidationException ex)
                {
                    return Results.BadRequest(ex.Message);
                }
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderNotificationEndpoints");
    }
}
