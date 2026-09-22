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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The signed-in shopper's orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint
    : IEndpoint<IResult, MyOrdersRequest, IShopperOrderService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IShopperOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { BuyerId = user.Identity?.Name }, service, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IShopperOrderService service,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(request.BuyerId!, ct);
        return Results.Ok(new MyOrdersResponse { Orders = orders });
    }
}

public class MyOrdersRequest : BaseRequest
{
    public string? BuyerId { get; set; }
}

public class MyOrdersResponse : BaseResponse
{
    public IReadOnlyList<OrderNotificationsView> Orders { get; set; } = new List<OrderNotificationsView>();
}
