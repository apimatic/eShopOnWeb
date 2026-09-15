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
/// Operator action: marks an order fulfilled, which is when the held authorization is captured and the money
/// is actually taken. A stale authorization is renewed rather than failing the fulfilment. Administrator only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentOrchestrationService service, CancellationToken ct) =>
                await ExecuteAsync(new FulfilOrderRequest(orderId), service, ct))
            .Produces<OrderPaymentView>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(FulfilOrderRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.FulfilAsync(request.OrderId, ct);
        return result.ToHttpResult(Results.Ok);
    }
}
