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
/// POST /api/orders/{orderId}/cancel — operator action: cancel before fulfilment by voiding the hold, so the
/// shopper's funds are released and no money ever moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService, CancellationToken cancellationToken) =>
                await HandleAsync(new OrderOperationRequest { OrderId = orderId }, paymentService, cancellationToken))
            .Produces<OrderOperationResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService paymentService,
        CancellationToken cancellationToken)
    {
        var payment = await paymentService.CancelAsync(request.OrderId, cancellationToken);
        return Results.Ok(new OrderOperationResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = PaymentMapping.ToDto(payment)
        });
    }
}
