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

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IPaidOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderRequest request, IPaidOrderService service, ClaimsPrincipal user) =>
                await HandleAsync(orderId, request, service, user))
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, IPaidOrderService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IPaidOrderService service, ClaimsPrincipal user)
    {
        var card = request.Card == null ? null : request.Card.ToCardPaymentSource();
        var order = await service.PayAsync(orderId, user.GetRequiredUserName(), card, request.PaymentMethodId);
        return Results.Ok(OrderDtoMapper.ToDto(order));
    }
}
