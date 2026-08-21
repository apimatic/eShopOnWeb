using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private readonly IOrderNotificationService _notifications;

    public CreateOrderEndpoint(IOrderNotificationService notifications)
    {
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService, HttpContext httpContext) =>
            {
                request.BuyerId = httpContext.User.GetBuyerId();
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items == null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one catalog item is required." });
        }

        var lines = request.Items.Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var address = request.ToAddress();

        Order order;
        try
        {
            order = await orderService.CreateOrderFromItemsAsync(request.BuyerId, lines, address);
        }
        catch (CatalogItemNotFoundException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
        catch (EmptyBasketOnCheckoutException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var notifications = await _notifications.NotifyOrderPlacedAsync(order);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}
