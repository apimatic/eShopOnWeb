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
            async (int orderId, PayOrderRequest request, ICheckoutPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, service, http);
            })
            .Produces<OrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutPaymentService service) =>
        HandleAsync(request, service, null!);

    private async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutPaymentService service, HttpContext http)
    {
        var buyerId = EndpointIdentity.RequireUserName(http);
        CardPaymentDetails? card = request.Card == null ? null : EndpointIdentity.ToCard(request.Card);
        var order = await service.PayAsync(request.OrderId, buyerId, card, request.PaymentMethodId, http.RequestAborted);
        return Results.Ok(OrderResponseMapper.From(order));
    }
}
