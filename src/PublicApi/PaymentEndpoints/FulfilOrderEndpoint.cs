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
/// POST /api/orders/{orderId}/fulfil — operator action: mark the order fulfilled and capture the held funds.
/// A stale authorization is renewed first; one that can no longer be renewed reports an actionable message.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
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
        var payment = await paymentService.FulfilAsync(request.OrderId, cancellationToken);
        return Results.Ok(new OrderOperationResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Payment = PaymentMapping.ToDto(payment)
        });
    }
}
