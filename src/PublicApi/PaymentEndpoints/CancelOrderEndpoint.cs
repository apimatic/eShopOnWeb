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
/// Operator action (administrator). Cancels an order before fulfilment, releasing any held funds so
/// no money moves. Idempotent.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdCommand, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new OrderIdCommand(orderId), paymentService))
            .Produces<CancelResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdCommand command, IPaymentService paymentService)
    {
        var payment = await paymentService.CancelOrderAsync(command.OrderId);

        var response = new CancelResponse
        {
            OrderId = command.OrderId,
            OrderStatus = ApplicationCore.Entities.OrderAggregate.OrderStatus.Cancelled.ToString(),
            PaymentStatus = payment?.Status.ToString(),
            Message = payment is null
                ? "Order cancelled; no payment had been made."
                : "Order cancelled; the authorization hold was released."
        };
        return Results.Ok(response);
    }
}
