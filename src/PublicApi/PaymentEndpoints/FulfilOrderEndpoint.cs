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

public class FulfilOrderRequest
{
    public int OrderId { get; set; }
    public FulfilOrderRequest(int orderId) => OrderId = orderId;
}

/// <summary>
/// Operator action: fulfils the order and captures the held funds. Renews a stale authorization before
/// capture rather than failing outright; an authorization that can no longer be renewed is reported clearly.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new FulfilOrderRequest(orderId), service, ct);
            })
            .Produces<PaymentView>()
            .WithTags("OrderPaymentEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service, CancellationToken ct)
    {
        var payment = await service.FulfilAsync(request.OrderId, ct);
        return Results.Ok(payment);
    }
}
