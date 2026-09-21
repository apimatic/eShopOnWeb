using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : PaymentEndpointBase, IEndpoint<IResult, IPaymentService>
{
    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IPaymentService service) => await HandleAsync(service))
            .Produces<IReadOnlyList<OrderPaymentView>>()
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService service)
    {
        var orders = await service.GetMyOrdersAsync(BuyerId, RequestAborted);
        return Results.Ok(orders);
    }
}
