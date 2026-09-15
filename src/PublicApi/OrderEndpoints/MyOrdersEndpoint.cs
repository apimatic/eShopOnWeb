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

/// <summary>Lists the caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(service, user);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service, ClaimsPrincipal user)
    {
        var ownerId = user.GetUserName();
        if (string.IsNullOrEmpty(ownerId))
            return Results.Unauthorized();

        var orders = await service.GetMyOrdersAsync(ownerId);
        return Results.Ok(new MyOrdersResponse { Orders = orders });
    }
}

public class MyOrdersResponse : BaseResponse
{
    public IReadOnlyList<OrderNotificationView> Orders { get; set; } = new List<OrderNotificationView>();
}
