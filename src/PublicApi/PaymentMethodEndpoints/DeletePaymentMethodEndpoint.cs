using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedPaymentMethodService service, ClaimsPrincipal user) =>
                await HandleAsync(paymentMethodId, service, user))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService service) =>
        Task.FromResult(Results.BadRequest());

    private static async Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService service, ClaimsPrincipal user)
    {
        await service.DeleteAsync(paymentMethodId, user.GetRequiredUserName());
        return Results.NoContent();
    }
}
