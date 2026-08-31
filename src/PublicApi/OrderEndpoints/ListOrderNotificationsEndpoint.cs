using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// What was sent for one of the signed-in shopper's orders, and what became of each message.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _orderNotificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService orderNotificationService)
    {
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), user);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var notifications = await _orderNotificationService.ListOrderNotificationsAsync(buyerId, request.OrderId);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.FromEntity).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
