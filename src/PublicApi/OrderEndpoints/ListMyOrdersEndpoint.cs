using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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

public class ListMyOrdersEndpoint : IEndpoint<IResult, IShopperOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IShopperOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                var orders = await service.ListMyOrdersAsync(buyerId, cancellationToken);
                var dtos = new List<MyOrderDto>();
                foreach (var order in orders)
                {
                    var notifications = await service.ListNotificationsForShopperOrderAsync(buyerId, order.Id, cancellationToken);
                    dtos.Add(new MyOrderDto
                    {
                        OrderId = order.Id,
                        Status = order.FulfillmentStatus.ToString(),
                        OrderDate = order.OrderDate,
                        Total = order.Total(),
                        Notifications = notifications.Select(NotificationDto.From).ToList()
                    });
                }

                return Results.Ok(new ListMyOrdersResponse { Orders = dtos });
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopperOrderNotificationService service)
        => Task.FromResult(Results.Ok());
}

public class ListMyOrdersResponse : BaseResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public System.DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
