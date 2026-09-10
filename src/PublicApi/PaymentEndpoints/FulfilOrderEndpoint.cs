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

public class FulfilOrderRequest : BaseRequest { }

/// <summary>
/// Operator action: marks the order fulfilled, which is when the held money is actually
/// captured. A stale authorization is renewed first; one that cannot be renewed is reported
/// in operator-actionable terms.
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
                var payment = await service.FulfilAsync(orderId, ct);
                return Results.Ok(PaymentStateResponse.From(payment, System.Guid.NewGuid()));
            })
            .Produces<PaymentStateResponse>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service) =>
        Task.FromResult(Results.Empty as IResult);
}
