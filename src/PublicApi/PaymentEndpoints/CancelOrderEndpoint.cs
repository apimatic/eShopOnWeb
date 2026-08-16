using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CancelOrderRequest
{
    public int OrderId { get; set; }
    public CancelOrderRequest(int orderId) => OrderId = orderId;
}

/// <summary>
/// Operator action: cancels an order before fulfilment, releasing (voiding) the held funds so no money moved.
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new CancelOrderRequest(orderId), service, ct);
            })
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CancelOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var payment = await service.CancelAsync(request.OrderId, ct);
        return Results.Ok(payment);
    }
}
