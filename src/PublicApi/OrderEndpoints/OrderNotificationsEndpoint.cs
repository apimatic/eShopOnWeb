using System;
using System.Collections.Generic;
using System.Linq;
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
/// What was sent for a given order and what became of each message. Each entry carries its own
/// notificationId — the identifier the operator endpoints act on. Scoped to the order's owner.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderProcessingService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderProcessingService orderProcessingService, HttpContext http) =>
            {
                return await HandleAsync(orderId, orderProcessingService, http);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderProcessingService orderProcessingService, HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var view = await orderProcessingService.GetOrderNotificationsForBuyerAsync(orderId, buyerId, http.RequestAborted);

        // A shopper must never see another's order: an order that is not theirs is indistinguishable
        // from one that does not exist.
        if (!view.Found || !view.OwnedByCaller)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = view.Notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
