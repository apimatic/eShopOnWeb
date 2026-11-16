using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public sealed class MyOrdersResponse
{
    public IReadOnlyList<OrderInfo> Orders { get; set; } = System.Array.Empty<OrderInfo>();
}

/// <summary>
/// Returns the caller's orders with their current payment state.
/// </summary>
public sealed class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointHelpers.GetBuyerId(httpContext);
                    var orders = await paymentService.GetMyOrdersAsync(buyerId, cancellationToken);

                    return Results.Ok(new MyOrdersResponse { Orders = orders });
                });
            })
            .Produces<MyOrdersResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("OrderEndpoints");
    }
}
