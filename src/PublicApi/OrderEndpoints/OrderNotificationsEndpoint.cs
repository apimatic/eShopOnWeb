using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// What was sent for one of the signed-in shopper's orders, and what became of each message. Each
/// entry carries its own notificationId — the id the operator endpoints act on. Scoped to the owner:
/// another shopper's order is not found here.
/// </summary>
public class OrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service) =>
            {
                var request = new OrderNotificationsRequest { OrderId = orderId, BuyerId = http.User.Identity?.Name };
                return await HandleAsync(request, service, http.RequestAborted);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await service.GetOrderNotificationsForOwnerAsync(request.OrderId, request.BuyerId, ct);
        if (notifications is null)
        {
            return Results.NotFound();
        }

        var response = new OrderNotificationsResponse(request.CorrelationId()) { OrderId = request.OrderId };
        response.Notifications.AddRange(notifications.Select(NotificationDto.From));
        return Results.Ok(response);
    }
}

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
