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
/// Operator action: marks the order fulfilled and captures (takes) the held funds. Afterwards
/// the payment shows PayPal's captured amount, fee and net proceeds. A stale authorization is
/// renewed before capture; one that can no longer be renewed is reported so an operator can act.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int orderId,
                IPaymentService paymentService,
                IPaymentSettings settings,
                CancellationToken ct) =>
            {
                var result = await paymentService.FulfilOrderAsync(orderId, ct);
                if (!result.IsSuccess) return result.ToProblem();

                var dto = result.Value.ToDto(settings.Currency);
                return Results.Ok(new { orderId = result.Value.Id, order = dto });
            })
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .WithTags("OrderEndpoints");
    }
}
