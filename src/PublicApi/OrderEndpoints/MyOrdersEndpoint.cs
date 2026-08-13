using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId) { }

    public IReadOnlyList<OrderNotificationsView> Orders { get; set; } = new List<OrderNotificationsView>();
}

/// <summary>The caller's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, ClaimsPrincipal, IOrderNotificationService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service, CancellationToken cancellationToken) =>
                await HandleAsync(user, service, cancellationToken))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, IOrderNotificationService service, CancellationToken cancellationToken)
    {
        var orders = await service.GetMyOrdersAsync(user.GetBuyerId(), cancellationToken);
        return Results.Ok(new MyOrdersResponse(Guid.NewGuid()) { Orders = orders });
    }
}
