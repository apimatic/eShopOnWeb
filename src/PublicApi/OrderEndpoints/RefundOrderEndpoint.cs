using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderEndpoint : IEndpoint<IResult, RefundOrderEndpoint.Request, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/refunds",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, RefundOrderRequest body, [Microsoft.AspNetCore.Mvc.FromServices] IOrderPaymentService service) =>
                    await HandleAsync(new Request { OrderId = orderId, Body = body }, service))
            .Produces<RefundOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPaymentService service)
    {
        var response = await service.RefundOrderAsync(request.OrderId, request.Body, default);
        return Results.Ok(response);
    }

    public class Request
    {
        public int OrderId { get; set; }
        public RefundOrderRequest Body { get; set; } = new();
    }
}
