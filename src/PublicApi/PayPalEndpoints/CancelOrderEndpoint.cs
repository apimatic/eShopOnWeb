using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PayPalEndpoints;

/// <summary>Operator action: cancels before fulfilment, releasing the held funds so no money moved.</summary>
public class CancelOrderEndpoint : IEndpoint<IResult, OrderRouteRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderRouteRequest { OrderId = orderId, Ct = ct }, service);
            })
            .Produces<PaymentStateResponse>()
            .WithTags("PayPalPayments");
    }

    public async Task<IResult> HandleAsync(OrderRouteRequest request, IOrderPaymentService service)
    {
        var view = await service.CancelAsync(request.OrderId, request.Ct);
        return Results.Ok(PaymentMapper.ToResponse(view, request.CorrelationId()));
    }
}
