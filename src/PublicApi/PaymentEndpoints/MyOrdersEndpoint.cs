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

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public MyOrdersEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service) => await HandleAsync(new EmptyRequest(), service))
            .Produces<IReadOnlyList<OrderPaymentSummary>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, IOrderPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var buyerId = CallerIdentity.BuyerId(ctx.User);
        var orders = await service.GetMyOrdersAsync(buyerId, ctx.RequestAborted);
        return Results.Ok(orders);
    }
}
