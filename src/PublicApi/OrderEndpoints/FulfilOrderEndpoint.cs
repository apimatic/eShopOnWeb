using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator: marks the order fulfilled and captures the held money. The response carries
/// what PayPal reported: captured amount, PayPal's fee and the net proceeds. A stale
/// authorization is renewed automatically; one that cannot be renewed fails with an
/// operator-actionable 409.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService orderPaymentService) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, orderPaymentService);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService orderPaymentService)
    {
        var payment = await orderPaymentService.FulfilOrderAsync(request.OrderId);

        var response = new FulfilOrderResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            OrderStatus = "Fulfilled",
            AuthorizationRenewed = payment.RenewalCount > 0,
            Payment = PaymentDto.FromModel(payment)
        };
        return Results.Ok(response);
    }
}
