using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders/{orderId}/fulfil — operator action. Marks the order fulfilled and captures the
/// held funds; a stale hold is renewed first. After this the payment shows PayPal's captured amount,
/// fee and net proceeds. Idempotent.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IPaymentProcessingService service, CancellationToken ct) =>
            {
                var order = await service.FulfilOrderAsync(orderId, ct);
                return Results.Ok(OrderPresentation.ToDto(order));
            })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }
}
