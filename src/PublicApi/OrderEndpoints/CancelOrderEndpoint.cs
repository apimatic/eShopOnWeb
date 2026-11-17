using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: cancel an order before fulfilment and release the card hold.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService paymentService) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), paymentService);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, PaymentService paymentService)
    {
        try
        {
            var payment = await paymentService.CancelOrderAsync(request.OrderId);
            return Results.Ok(new CancelOrderResponse
            {
                OrderId = payment.OrderId,
                Order = OrderPaymentDto.From(payment)
            });
        }
        catch (PaymentOperationException ex)
        {
            return EndpointHelpers.ToResult(ex);
        }
    }
}
