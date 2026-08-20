using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService service, ClaimsPrincipal user) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var card = request.Card is null ? null : PaymentEndpointHelpers.ToCard(request.Card);
                var order = await service.PayAsync(orderId, buyerId, card, request.PaymentMethodId);
                return Results.Ok(PaymentEndpointHelpers.ToResponse(order));
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service)
        => Task.FromResult(Results.BadRequest());
}
