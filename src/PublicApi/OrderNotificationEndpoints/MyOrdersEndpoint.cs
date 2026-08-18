using System;
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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record OrderItemDto(int CatalogItemId, string ProductName, decimal UnitPrice, int Units);
public record MyOrderDto(int OrderId, DateTimeOffset OrderDate, decimal Total, IReadOnlyList<OrderItemDto> Items, IReadOnlyList<NotificationView> Notifications);
public record MyOrdersResponse(IReadOnlyList<MyOrderDto> Orders);

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (callerId is null)
                {
                    return Results.Unauthorized();
                }

                var (orders, notifications) = await service.GetMyOrdersAsync(callerId, ct);
                var byOrder = notifications
                    .GroupBy(n => n.OrderId)
                    .ToDictionary(g => g.Key, g => g.Select(NotificationMapping.ToView).ToList());

                var dtos = orders.Select(o => new MyOrderDto(
                    o.Id,
                    o.OrderDate,
                    o.Total(),
                    o.OrderItems.Select(i => new OrderItemDto(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units)).ToList(),
                    byOrder.TryGetValue(o.Id, out var ns) ? ns : new List<NotificationView>()))
                    .ToList();

                return Results.Ok(new MyOrdersResponse(dtos));
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult(Results.Ok());
}
