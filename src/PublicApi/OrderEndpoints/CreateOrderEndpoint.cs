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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(IOrderService orderService, IOrderNotificationService notifications)
    {
        _orderService = orderService;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user) =>
                await HandleAsync(request, user))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user)
    {
        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item is required." });
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Quantities must be greater than zero." });
        }

        var buyerId = user.GetRequiredBuyerId();
        var lines = request.Items.Select(i => (i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = new Address("123 Main St.", "Kent", "OH", "United States", "44240");

        Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order order;
        try
        {
            order = await _orderService.CreateOrderFromCatalogAsync(buyerId, lines, shipTo);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        await _notifications.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString()
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
