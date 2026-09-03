using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service, HttpContext http) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service, http.RequestAborted);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, default);

    private async Task<IResult> HandleAsync(
        CancelOrderRequest request,
        IShopperOrderService service,
        System.Threading.CancellationToken cancellationToken)
    {
        var result = await service.CancelAsync(request.OrderId, cancellationToken);
        var dto = OrderDto.From(result);
        return Results.Ok(new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = dto.OrderId,
            Order = dto
        });
    }
}

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
