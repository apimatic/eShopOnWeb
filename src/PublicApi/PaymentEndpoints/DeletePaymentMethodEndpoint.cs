using System.Security.Claims;
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
/// DELETE /api/payment-methods/{paymentMethodId} — remove one of the caller's saved cards. Afterwards
/// it no longer appears in the caller's list and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeleteCardCommand, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service, CancellationToken ct) =>
            {
                return await HandleAsync(
                    new DeleteCardCommand(PaymentUser.BuyerId(user), paymentMethodId, ct), service);
            })
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(DeleteCardCommand command, IPaymentMethodService service)
    {
        await service.DeleteCardAsync(command.BuyerId, command.PaymentMethodId, command.Ct);
        return Results.NoContent();
    }
}

public record DeleteCardCommand(string BuyerId, int PaymentMethodId, CancellationToken Ct);
