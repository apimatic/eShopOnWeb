using System;
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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotificationEndpoints;

/// <summary>
/// POST /api/orders — places an order for the signed-in shopper from catalog item ids + quantities,
/// reusing the existing order model, and texts them that the order was placed. Returns orderId.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                if (request?.Items == null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "An order must contain at least one item." });
                }

                var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();

                try
                {
                    var order = await service.PlaceOrderAsync(buyerId, lines, cancellationToken);
                    var items = order.OrderItems
                        .Select(oi => new OrderLineDto(oi.ItemOrdered.CatalogItemId, oi.ItemOrdered.ProductName, oi.UnitPrice, oi.Units))
                        .ToList();
                    var response = new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total(), items);
                    return Results.Created($"api/orders/{order.Id}", response);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderNotificationEndpoints");
    }
}
