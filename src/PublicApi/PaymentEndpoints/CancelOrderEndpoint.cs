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
/// Operator action: cancels an order before fulfilment, releasing the held funds (void).
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId, Cancellation = ct }, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        var payment = await service.CancelAsync(request.OrderId, request.Cancellation);
        return Results.Ok(OrderPaymentResponse.From(payment));
    }
}
