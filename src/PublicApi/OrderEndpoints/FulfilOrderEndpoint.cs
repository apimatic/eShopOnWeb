using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public OrderResponse Order { get; set; } = new();
}

public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, HttpContext http) =>
            {
                return await HandleAsync(new FulfilOrderRequest { OrderId = orderId }, service, http);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
        => HandleAsync(request, service, http: null!);

    private async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service, HttpContext http)
    {
        var order = await service.FulfilAsync(request.OrderId, http.RequestAborted);
        var mapped = OrderResponseMapper.Map(order);
        return Results.Ok(new FulfilOrderResponse { OrderId = mapped.OrderId, Order = mapped });
    }
}
