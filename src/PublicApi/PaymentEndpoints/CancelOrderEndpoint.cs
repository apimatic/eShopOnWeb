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
/// POST /api/orders/{orderId}/cancel — operator action. Cancels before fulfilment and releases the
/// held funds (voids the authorization). Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderOperatorCommand, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderOperatorCommand(orderId, ct), service);
            })
            .Produces<OrderSummaryDto>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(OrderOperatorCommand command, IOrderPaymentService service)
    {
        var order = await service.CancelAsync(command.OrderId, command.Ct);
        return Results.Ok(PaymentApiMapper.ToOrderSummaryDto(order));
    }
}
