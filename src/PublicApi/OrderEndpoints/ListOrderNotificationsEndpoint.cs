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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext http, IOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                var userName = http.GetUserName();
                if (string.IsNullOrEmpty(userName))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new ListOrderNotificationsRequest(orderId), service, userName, cancellationToken);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, string.Empty, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ListOrderNotificationsRequest request,
        IOrderNotificationService service,
        string buyerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var notifications = await service.ListNotificationsForOrderAsync(request.OrderId, buyerId, cancellationToken);
            var response = new ListOrderNotificationsResponse(request.CorrelationId())
            {
                OrderId = request.OrderId,
                Notifications = notifications.Select(NotificationMapper.ToDto).ToList()
            };
            return Results.Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
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
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
