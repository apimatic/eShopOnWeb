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

public record FulfilOrderRequest(int OrderId, CancellationToken Ct);

/// <summary>POST /api/orders/{orderId}/fulfil — operator marks the order fulfilled and the money is captured.</summary>
public class OrdersFulfilEndpoint : IEndpoint<IResult, FulfilOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(new FulfilOrderRequest(orderId, ct), service))
            .Produces<OrderPaymentView>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, IOrderPaymentService service)
    {
        var view = await service.FulfilAsync(request.OrderId, request.Ct);
        return Results.Ok(view);
    }
}
