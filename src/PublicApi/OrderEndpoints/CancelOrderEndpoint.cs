using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderEndpoint.Request, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
                [Authorize(Roles = Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
                async (int orderId, [Microsoft.AspNetCore.Mvc.FromServices] IOrderPaymentService service) =>
                    await HandleAsync(new Request { OrderId = orderId }, service))
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, IOrderPaymentService service)
    {
        var response = await service.CancelOrderAsync(request.OrderId, default);
        return Results.Ok(response);
    }

    public class Request
    {
        public int OrderId { get; set; }
    }
}
