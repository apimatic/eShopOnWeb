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

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), service, http.RequestAborted);
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, default);

    private async Task<IResult> HandleAsync(
        DispatchOrderRequest request,
        IShopperOrderService service,
        System.Threading.CancellationToken cancellationToken)
    {
        var result = await service.DispatchAsync(request.OrderId, cancellationToken);
        var dto = OrderDto.From(result);
        return Results.Ok(new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = dto.OrderId,
            Order = dto
        });
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public DispatchOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class DispatchOrderResponse : BaseResponse
{
    public DispatchOrderResponse(System.Guid correlationId) : base(correlationId) { }
    public DispatchOrderResponse() { }

    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
