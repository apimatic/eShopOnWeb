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

/// <summary>Operator: fulfils an order, capturing the held funds (renewing a stale hold if needed).</summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderReference, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service) =>
                await HandleAsync(new OrderReference(orderId), service))
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderReference request, IOrderPaymentService service)
    {
        var view = await service.FulfilAsync(request.OrderId, CancellationToken.None);
        return Results.Ok(OrderPaymentResponse.From(view));
    }
}
