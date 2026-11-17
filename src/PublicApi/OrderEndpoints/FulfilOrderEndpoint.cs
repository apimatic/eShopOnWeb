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
/// Operator action: capture the previously authorized payment and mark the order fulfilled.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderActionRequest, PaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PaymentService paymentService) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), paymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, PaymentService paymentService)
    {
        try
        {
            var payment = await paymentService.FulfilOrderAsync(request.OrderId);
            return Results.Ok(new FulfilOrderResponse
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
