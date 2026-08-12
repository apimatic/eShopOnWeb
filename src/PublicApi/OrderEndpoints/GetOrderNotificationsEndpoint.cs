using System;
using System.Collections.Generic;
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

public class OrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string? BuyerId { get; set; }
    public bool IsAdministrator { get; set; }
}

public class OrderNotificationsResponse : BaseResponse
{
    public OrderNotificationsResponse(Guid correlationId) : base(correlationId) { }
    public OrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public IReadOnlyList<NotificationView> Notifications { get; set; } = new List<NotificationView>();
}

/// <summary>
/// What was sent for one order and what became of each message. Each entry carries its own
/// notificationId — the identifier the operator endpoints act on. Visible to the order's owner and to
/// operators.
/// </summary>
public class GetOrderNotificationsEndpoint : IEndpoint<IResult, OrderNotificationsRequest, IOrderQueryService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderQueryService service, ClaimsPrincipal user) =>
            {
                var request = new OrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = CallerIdentity.GetUserName(user),
                    IsAdministrator = CallerIdentity.IsAdministrator(user)
                };
                return await HandleAsync(request, service);
            })
            .Produces<OrderNotificationsResponse>()
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderNotificationsRequest request, IOrderQueryService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.GetOrderNotificationsAsync(request.OrderId, request.BuyerId, request.IsAdministrator);
        return result.Outcome switch
        {
            ActionOutcome.Success => Results.Ok(new OrderNotificationsResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Notifications = result.Notifications
            }),
            ActionOutcome.NotFound => Results.NotFound(),
            _ => Results.StatusCode(StatusCodes.Status403Forbidden)
        };
    }
}
