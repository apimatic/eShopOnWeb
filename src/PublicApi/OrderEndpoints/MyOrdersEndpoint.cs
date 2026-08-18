using System.Collections.Generic;
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

/// <summary>The caller's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderNotificationService service, CancellationToken ct) =>
            {
                var caller = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(caller))
                {
                    return Results.Unauthorized();
                }

                var orders = await service.GetMyOrdersAsync(caller, ct);
                return Results.Ok(new MyOrdersResponse { Orders = orders });
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service) => Task.FromResult<IResult>(Results.Empty);
}

public class MyOrdersResponse : BaseResponse
{
    public IReadOnlyList<MyOrderView> Orders { get; set; } = new List<MyOrderView>();
}
