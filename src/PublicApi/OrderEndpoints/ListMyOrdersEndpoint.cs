using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<MyOrderDto> Orders { get; set; } = new();
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderNotificationService service) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderNotificationService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.GetRequiredBuyerId();
        var orders = await service.GetOrdersForBuyerAsync(buyerId);
        var response = new ListMyOrdersResponse(request.CorrelationId());

        foreach (var order in orders)
        {
            var notifications = await service.GetNotificationsForOrderAsync(order.Id, refreshFromProvider: true);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total(),
                OrderDate = order.OrderDate,
                Notifications = notifications.Select(MapNotification).ToList()
            });
        }

        return Results.Ok(response);
    }

    internal static NotificationDto MapNotification(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.DeliveryStatus,
        ProviderSid = n.ProviderSid,
        Body = n.BodyForDisplay(),
        ContentRedacted = n.ContentRedacted,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        DateSent = n.DateSent,
        CreatedAt = n.CreatedAt,
        ScheduledFor = n.ScheduledFor,
        SourceNotificationId = n.SourceNotificationId
    };
}
