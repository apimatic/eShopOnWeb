using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
}

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing the held funds so no money moves.
/// Restricted to administrators.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IPaymentProcessingService service) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IPaymentProcessingService service)
    {
        var order = await service.CancelAsync(request.OrderId);
        return Results.Ok(OrderPaymentMapping.ToResponse(order));
    }
}
