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

public class CancelOrderRequest : BaseRequest { }

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the shopper's held funds
/// (voids the authorization). No money ever moves.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                var payment = await service.CancelAsync(orderId, ct);
                return Results.Ok(PaymentStateResponse.From(payment, System.Guid.NewGuid()));
            })
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.Empty as IResult);
}
