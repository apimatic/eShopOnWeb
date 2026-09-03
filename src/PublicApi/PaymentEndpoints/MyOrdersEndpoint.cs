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

public class MyOrdersResponse
{
    public IReadOnlyList<OrderPaymentView> Orders { get; set; } = new List<OrderPaymentView>();
}

/// <summary>The caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public MyOrdersEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IPaymentService service) => await HandleAsync(service))
            .Produces<MyOrdersResponse>()
            .WithTags("Payments");
    }

    public async Task<IResult> HandleAsync(IPaymentService service)
    {
        var ctx = _http.HttpContext!;
        var orders = await service.GetMyOrdersAsync(ctx.User.BuyerId(), ctx.RequestAborted);
        return Results.Ok(new MyOrdersResponse { Orders = orders });
    }
}
