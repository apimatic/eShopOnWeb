using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }
    public OrderDto Order { get; set; } = new();
}

/// <summary>
/// POST /api/orders/{orderId}/cancel — operator action: cancel before fulfilment, releasing the
/// held funds so no money moves. Restricted to the administrator role.
/// </summary>
public class CancelOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(orderId, service))
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    private static async Task<IResult> HandleAsync(int orderId, IOrderPaymentService service)
    {
        try
        {
            var order = await service.CancelAsync(orderId);
            return Results.Ok(new CancelOrderResponse { Order = OrderDto.From(order) });
        }
        catch (Exception ex) when (PaymentErrorMapper.TryMap(ex, out var result))
        {
            return result;
        }
    }
}
