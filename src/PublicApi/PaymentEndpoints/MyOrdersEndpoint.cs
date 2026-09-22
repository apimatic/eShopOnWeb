using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, EmptyRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public MyOrdersEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderPaymentService service) =>
                await PaymentEndpointSupport.ExecuteAsync(async () =>
                {
                    var buyerId = PaymentEndpointSupport.RequireUserName(_http);
                    var ct = PaymentEndpointSupport.RequestAborted(_http);
                    var orders = await service.GetMyOrdersAsync(buyerId, ct);
                    return Results.Ok(orders);
                }))
            .WithTags("PaymentOrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.BadRequest());
}
