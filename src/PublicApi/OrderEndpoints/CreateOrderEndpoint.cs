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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities for the signed-in shopper,
/// and notifies the shopper by SMS that the order was placed.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, HttpContext httpContext)
    {
        // Scoped services are resolved per request: endpoint instances are captured by
        // the route lambda, so constructor injection would capture a stale DbContext.
        var orderService = httpContext.RequestServices.GetRequiredService<IOrderService>();
        var notificationService = httpContext.RequestServices.GetRequiredService<IOrderNotificationService>();

        var buyerId = httpContext.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("At least one item is required.");
        }

        var address = new Address(request.Street, request.City, request.State, request.Country, request.ZipCode);
        var items = request.Items.Select(i => new OrderItemEntry { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity }).ToList();

        Order order;
        try
        {
            order = await orderService.CreateOrderAsync(buyerId, address, items);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }

        // Never fails the order: notification failures are handled inside the service.
        await notificationService.NotifyOrderPlacedAsync(order, httpContext.RequestAborted);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
