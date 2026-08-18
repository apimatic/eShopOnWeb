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

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// The caller's own orders, each showing its dispatch/cancel state and where its notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                return await HandleAsync(user, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct)
    {
        var ownerId = user.GetUserId();
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var views = await service.GetMyOrdersAsync(ownerId, ct);
        var response = new MyOrdersResponse
        {
            Orders = views.Select(v => new MyOrderDto
            {
                OrderId = v.Delivery.OrderId,
                State = v.Delivery.State.ToString(),
                DispatchedAt = v.Delivery.DispatchedAt,
                CancelledAt = v.Delivery.CancelledAt,
                Total = v.Order?.Total() ?? 0m,
                Notifications = v.Notifications
                    .OrderBy(n => n.CreatedAt)
                    .Select(NotificationDto.From)
                    .ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}

public class MyOrdersResponse
{
    public List<MyOrderDto> Orders { get; set; } = new();
}

public class MyOrderDto
{
    public int OrderId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
