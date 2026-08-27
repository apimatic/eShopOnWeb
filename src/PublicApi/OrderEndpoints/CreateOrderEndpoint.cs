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
using Microsoft.eShopWeb.ApplicationCore.Models;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper and notifies them by SMS
/// that the order was placed. A notification failure never fails the order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal>
{
    private readonly IOrderService _orderService;
    private readonly IOrderNotificationService _notificationService;

    public CreateOrderEndpoint(IOrderService orderService, IOrderNotificationService notificationService)
    {
        _orderService = orderService;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal claimsPrincipal) =>
            {
                return await HandleAsync(request, claimsPrincipal);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal claimsPrincipal)
    {
        var orderService = _orderService;
        var notificationService = _notificationService;
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items == null || !request.Items.Any())
        {
            return Results.BadRequest(new { error = "An order requires at least one item." });
        }

        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { error = "Item quantities must be positive." });
        }

        var address = new Address(request.ShipToStreet, request.ShipToCity, request.ShipToState, request.ShipToCountry, request.ShipToZipCode);

        Order order;
        try
        {
            order = await orderService.CreateOrderFromItemsAsync(buyerId, address,
                request.Items.Select(i => new OrderItemRequest { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity }).ToList());
        }
        catch (System.ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        await notificationService.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            OrderDate = order.OrderDate
        };

        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}
