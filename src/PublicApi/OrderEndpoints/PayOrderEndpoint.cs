using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                return await HandleAsync(orderId, request, user, service);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(0, request, new ClaimsPrincipal(), service);

    private async Task<IResult> HandleAsync(
        int orderId,
        PayOrderRequest request,
        ClaimsPrincipal user,
        IOrderPaymentService service)
    {
        var result = await service.PayAsync(
            orderId,
            user.RequireBuyerId(),
            request.PaymentMethodId,
            request.Card?.ToDetails());
        return Results.Ok(OrderResponse.From(result, request.CorrelationId()));
    }
}
