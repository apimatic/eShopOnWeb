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

/// <summary>
/// Operator action: fulfils an order, capturing (taking) the held money. Renews a stale hold first.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, OrderOperationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new OrderOperationRequest { OrderId = orderId, Cancellation = ct }, service);
            })
            .Produces<OrderPaymentResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderOperationRequest request, IOrderPaymentService service)
    {
        var payment = await service.FulfilAsync(request.OrderId, request.Cancellation);
        return Results.Ok(OrderPaymentResponse.From(payment));
    }
}
