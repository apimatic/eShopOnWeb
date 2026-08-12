using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }

    /// <summary>What was sent for this order, and what became of each message. Each carries its own notificationId.</summary>
    public List<NotificationDto> Notifications { get; set; } = new();
}

/// <summary>
/// Returns what was sent for one of the caller's orders, and what became of each message. Shopper-scoped:
/// an order that is not the caller's is reported as not found.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new OrderNotificationsRequest { OrderId = orderId, BuyerId = buyerId }, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service)
    {
        var result = await service.GetOrderNotificationsAsync(request.OrderId, request.BuyerId);
        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = result.Value.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
