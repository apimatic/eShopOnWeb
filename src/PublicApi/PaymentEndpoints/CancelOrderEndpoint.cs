using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders/{orderId}/cancel — operator voids the hold before fulfilment. Admin only.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, EmptyRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _http;

    public CancelOrderEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await PaymentEndpointSupport.ExecuteAsync(async () =>
                {
                    var ct = PaymentEndpointSupport.RequestAborted(_http);
                    var view = await service.CancelAsync(orderId, ct);
                    return Results.Ok(view);
                }))
            .WithTags("PaymentOrderEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.BadRequest());
}
