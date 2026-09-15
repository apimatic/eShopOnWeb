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
/// Operator action: cancels the order before fulfilment, releasing the shopper's held funds so no money moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId }, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        var payment = await service.CancelAsync(request.OrderId);
        return Results.Ok(new OrderPaymentResponse(request.CorrelationId()) { Payment = PaymentMappers.ToDto(payment) });
    }
}
