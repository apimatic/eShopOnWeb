using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext http) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service, http);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service)
        => HandleAsync(request, service, http: null!);

    private async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service, HttpContext http)
    {
        var order = await service.CancelAsync(request.OrderId, http.RequestAborted);
        var mapped = OrderResponseMapper.Map(order);
        return Results.Ok(new CancelOrderResponse { OrderId = mapped.OrderId, Order = mapped });
    }
}
