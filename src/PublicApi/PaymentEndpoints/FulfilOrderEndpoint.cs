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
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures the
/// money; a stale hold is renewed first. Restricted to the administrator role.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderOperatorCommand, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
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
        var order = await service.FulfilAsync(command.OrderId, command.Ct);
        return Results.Ok(PaymentApiMapper.ToOrderSummaryDto(order));
    }
}

/// <summary>An operator command against a single order (fulfil/cancel).</summary>
public record OrderOperatorCommand(int OrderId, CancellationToken Ct);
