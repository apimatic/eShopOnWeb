using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, ICheckoutPaymentService checkout, ClaimsPrincipal user) =>
            {
                CardPaymentSource? card = request.Card == null ? null : CardRequestMapping.ToPaymentSource(request.Card);
                var order = await checkout.PayAsync(
                    orderId,
                    OrderEndpointHelpers.GetBuyerId(user),
                    card,
                    request.PaymentMethodId);
                return Results.Ok(new OrderActionResponse
                {
                    OrderId = order.Id,
                    Order = OrderEndpointHelpers.ToDto(order)
                });
            })
            .Produces<OrderActionResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutPaymentService checkout) =>
        Task.FromResult(Results.BadRequest());
}
