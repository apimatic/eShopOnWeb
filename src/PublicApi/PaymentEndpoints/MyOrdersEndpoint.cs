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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class MyOrdersRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CancellationToken CancellationToken { get; set; }
}

public record MyOrdersResponse(IReadOnlyList<OrderDto> Orders);

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, HttpContext ctx) =>
                await HandleAsync(new MyOrdersRequest
                {
                    BuyerId = CallerIdentity.BuyerId(user),
                    CancellationToken = ctx.RequestAborted
                }, service))
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service)
    {
        var orders = await service.GetMyOrdersAsync(request.BuyerId, request.CancellationToken);
        return Results.Ok(new MyOrdersResponse(orders.Select(PaymentMappings.ToDto).ToList()));
    }
}
