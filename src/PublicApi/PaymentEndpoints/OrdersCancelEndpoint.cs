using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record CancelOrderRequest(int OrderId, CancellationToken Ct);

/// <summary>POST /api/orders/{orderId}/cancel — operator cancels before fulfilment, releasing the held funds.</summary>
public class OrdersCancelEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(new CancelOrderRequest(orderId, ct), service))
            .Produces<OrderPaymentView>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service)
    {
        var view = await service.CancelAsync(request.OrderId, request.Ct);
        return Results.Ok(view);
    }
}
