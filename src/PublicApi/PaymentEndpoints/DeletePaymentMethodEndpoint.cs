using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record DeletePaymentMethodCommand(string BuyerId, int PaymentMethodId);

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among their cards and can no
/// longer be used to pay. One shopper can never delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodCommand, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService service) =>
            {
                return await HandleAsync(new DeletePaymentMethodCommand(user.GetBuyerId(), paymentMethodId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodCommand command, IPaymentMethodService service)
    {
        var removed = await service.DeleteAsync(command.BuyerId, command.PaymentMethodId);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
