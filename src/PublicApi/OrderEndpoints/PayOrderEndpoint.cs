using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderApiRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, PayOrderApiRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.OrderId = orderId;
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<OrderApiResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderApiRequest request, IOrderPaymentService service)
    {
        var card = request.Card == null ? null : OrderDtoMapper.ToCard(request.Card);
        var order = await service.PayAsync(new PayOrderRequest(request.OrderId, request.BuyerId!, card, request.PaymentMethodId));
        return Results.Ok(new OrderApiResponse { Order = OrderDtoMapper.ToDto(order) });
    }
}
