using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderEndpoint.Request, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, PayOrderRequest body, [Microsoft.AspNetCore.Mvc.FromServices] IOrderPaymentService service) =>
                    await HandleAsync(new Request { OrderId = orderId, Body = body }, service))
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPaymentService service)
    {
        var response = await service.PayOrderAsync(request.OrderId, request.Body, default);
        return Results.Ok(response);
    }

    public class Request
    {
        public int OrderId { get; set; }
        public PayOrderRequest Body { get; set; } = new();
    }
}
