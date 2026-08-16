using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils the order, which is when the money is actually taken (captured). A hold
/// that has gone stale is renewed first; one that can no longer be renewed is reported so an
/// operator can act on it.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service, ct);
            })
            .Produces<PaymentDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
        => HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var payment = await service.FulfilAsync(request.OrderId, ct);
        return Results.Ok(PaymentDto.From(payment));
    }
}
