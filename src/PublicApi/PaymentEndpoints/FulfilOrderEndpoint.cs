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

public record FulfilOrderCommand(int OrderId, CancellationToken Ct);

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled; that is when the money is
/// captured. A stale authorization is renewed first. Administrator-only.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IPaymentApplicationService service) =>
                await HandleAsync(new FulfilOrderCommand(orderId, http.RequestAborted), service))
            .Produces<PaymentStateDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(FulfilOrderCommand command, IPaymentApplicationService service)
    {
        var payment = await service.FulfilAsync(command.OrderId, command.Ct);
        return Results.Ok(PaymentMapper.ToDto(payment));
    }
}
