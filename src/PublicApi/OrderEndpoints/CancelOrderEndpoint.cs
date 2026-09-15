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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, ICatalogOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ICatalogOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, ICatalogOrderService service)
        => HandleAsync(request, service, CancellationToken.None);

    private async Task<IResult> HandleAsync(CancelOrderRequest request, ICatalogOrderService service, CancellationToken ct)
    {
        await service.CancelAsync(request.OrderId, ct);
        var order = await service.GetByIdAsync(request.OrderId, ct);
        var response = new CancelOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.FulfillmentStatus.ToString()
        };
        return Results.Ok(response);
    }
}
