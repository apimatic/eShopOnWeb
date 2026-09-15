using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest { OrderId = orderId }, service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderNotificationService service)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = httpContext.GetRequiredBuyerId();
        var order = await service.GetOrderAsync(request.OrderId);
        if (order is null)
        {
            throw new OrderNotFoundException(request.OrderId);
        }

        if (!httpContext.User.IsAdministrator() && order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(request.OrderId);
        }

        var notifications = await service.GetNotificationsForOrderAsync(request.OrderId, refreshFromProvider: true);
        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(ListMyOrdersEndpoint.MapNotification).ToList()
        };
        return Results.Ok(response);
    }
}
