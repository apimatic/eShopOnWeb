using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// Operator action: marks an order fulfilled and captures the money. The response shows what PayPal
/// reported — the captured amount, PayPal's fee, and the net proceeds. A stale hold is renewed first.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentService paymentService) =>
                await HandleAsync(new OrderActionRequest(orderId), paymentService))
            .Produces<OrderResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IPaymentService paymentService)
    {
        var result = await paymentService.FulfilAsync(request.OrderId);
        return result.IsSuccess ? Results.Ok(result.Value.ToResponse()) : result.ToProblem();
    }
}
