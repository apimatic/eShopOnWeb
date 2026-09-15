using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user) =>
                await HandleAsync(orderId, request, payments, user))
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService payments)
        => HandleAsync(0, request, payments, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IOrderPaymentService payments, ClaimsPrincipal user)
    {
        CardPaymentDetails? card = request.Card == null ? null : PaymentApiMapper.ToCard(request.Card);
        var order = await payments.PayAsync(orderId, PaymentApiMapper.BuyerId(user), card, request.PaymentMethodId);
        return Results.Ok(PaymentApiMapper.FromOrder(order));
    }
}
