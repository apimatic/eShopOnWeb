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

/// <summary>Operator action: voids an order's held authorization before fulfilment. No funds ever move.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService paymentService, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest { OrderId = orderId }, paymentService, ct);
            })
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService paymentService, CancellationToken ct)
    {
        var response = new CancelOrderResponse(request.CorrelationId());

        var order = await paymentService.CancelAsync(request.OrderId, ct);
        if (order is null) return Results.NotFound();

        response.OrderId = order.Id;
        response.Order = OrderMapper.ToDto(order);
        return Results.Ok(response);
    }
}
