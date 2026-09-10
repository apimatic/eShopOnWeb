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

public record CancelOrderCommand(int OrderId, CancellationToken Ct);

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator cancels before fulfilment; the shopper's held funds are
/// released so no money ever moved. Administrator-only.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderCommand, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IPaymentApplicationService service) =>
                await HandleAsync(new CancelOrderCommand(orderId, http.RequestAborted), service))
            .Produces<PaymentStateDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(CancelOrderCommand command, IPaymentApplicationService service)
    {
        var payment = await service.CancelAsync(null, command.OrderId, command.Ct);
        return Results.Ok(PaymentMapper.ToDto(payment));
    }
}
