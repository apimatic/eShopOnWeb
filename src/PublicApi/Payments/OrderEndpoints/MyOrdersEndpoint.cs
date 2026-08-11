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

namespace Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;

public class MyOrdersResponse
{
    public List<OrderWithPaymentDto> Orders { get; set; } = new();
}

/// <summary>
/// GET /api/my-orders — the caller's own orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var views = await service.GetOrdersForBuyerAsync(buyerId, ct);
                var response = new MyOrdersResponse
                {
                    Orders = views.Select(v => PaymentMapping.ToOrderWithPaymentDto(v.Order, v.Payment)).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderPaymentEndpoints");
    }
}
