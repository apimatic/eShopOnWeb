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

/// <summary>
/// Operator action: cancels an order before fulfilment by voiding the authorization, releasing the held
/// funds so no money ever moves. Administrator only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentOrchestrationService service, CancellationToken ct) =>
                await ExecuteAsync(new CancelOrderRequest(orderId), service, ct))
            .Produces<OrderPaymentView>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(CancelOrderRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.CancelAsync(request.OrderId, ct);
        return result.ToHttpResult(Results.Ok);
    }
}
