using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record DeletePaymentMethodCommand(int PaymentMethodId);

/// <summary>
/// Removes one of the caller's saved cards. Afterwards it no longer appears among the caller's
/// saved cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodCommand, ISavedCardService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeletePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ISavedCardService savedCardService) =>
                await HandleAsync(new DeletePaymentMethodCommand(paymentMethodId), savedCardService))
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodCommand request, ISavedCardService savedCardService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.BuyerId();
        await savedCardService.DeleteCardAsync(buyerId!, request.PaymentMethodId);
        return Results.NoContent();
    }
}
