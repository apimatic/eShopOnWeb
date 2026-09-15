using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest, ICatalogOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ICatalogOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new DispatchOrderRequest(orderId), service, ct);
            })
            .Produces<DispatchOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(DispatchOrderRequest request, ICatalogOrderService service)
        => HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(DispatchOrderRequest request, ICatalogOrderService service, CancellationToken ct)
    {
        await service.DispatchAsync(request.OrderId, ct);
        var order = await service.GetByIdAsync(request.OrderId, ct);
        var response = new DispatchOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.FulfillmentStatus.ToString()
        };
        return Results.Ok(response);
    }
}
