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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// The caller's own orders, each showing where its notifications got to (delivery outcomes are refreshed
/// from the provider, since there is no inbound webhook).
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var ownerId = user.ShopperId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                return await ExecuteAsync(new MyOrdersRequest { OwnerId = ownerId }, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(MyOrdersRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(60));

        var orders = await service.GetOrdersForOwnerAsync(request.OwnerId, cts.Token);
        var notifications = await service.GetNotificationsForOwnerAsync(request.OwnerId, refreshFromProvider: true, cts.Token);
        var byOrder = notifications.GroupBy(n => n.OrderId).ToDictionary(g => g.Key, g => g.ToList());

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.Id,
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Notifications = byOrder.TryGetValue(o.Id, out var ns)
                    ? ns.Select(NotificationDto.From).ToList()
                    : new List<NotificationDto>()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
