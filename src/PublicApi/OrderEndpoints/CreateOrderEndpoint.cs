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
using Microsoft.eShopWeb.PublicApi.Shared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities for the signed-in shopper, reusing the
/// existing order/order-item model, and tells the shopper their order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(request, user, orderService, notificationService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService, IOrderNotificationService notificationService)
    {
        var buyerId = user.UserId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.Problem("An order must contain at least one item.", statusCode: StatusCodes.Status400BadRequest);
        }

        var items = request.Items.Select(i => new NewOrderItem(i.CatalogItemId, i.Quantity)).ToList();
        var address = (request.ShipToAddress ?? new ShippingAddressDto()).ToAddress();

        var order = await orderService.CreateOrderAsync(buyerId, items, address);

        // The shopper is told their order was placed. This is best-effort and never fails the order.
        var notifications = await notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
