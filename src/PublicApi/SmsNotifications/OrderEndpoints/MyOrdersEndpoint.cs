using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.OrderEndpoints;

public class MyOrdersRequest : BaseRequest
{
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public MyOrdersResponse() { }

    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>GET /api/my-orders &ndash; the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IReadRepository<Order>, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(new MyOrdersRequest { BuyerId = buyerId }, orderRepository, notificationService);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IReadRepository<Order> orderRepository, IOrderNotificationService notificationService)
    {
        var orders = await orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(request.BuyerId));
        var notificationsByOrder = await notificationService.GetForOrdersAsync(orders.Select(o => o.Id));

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Status = o.Status.ToString(),
                Total = o.Total(),
                Notifications = (notificationsByOrder.TryGetValue(o.Id, out var ns) ? ns : new List<OrderNotification>())
                    .Select(NotificationDto.From).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
