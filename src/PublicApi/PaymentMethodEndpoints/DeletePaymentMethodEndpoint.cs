using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// DELETE /api/payment-methods/{paymentMethodId} — removes one of the caller's saved cards.
/// Afterwards it no longer appears among the caller's cards and can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedPaymentMethodService>
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
            (int paymentMethodId, ISavedPaymentMethodService service) =>
                await HandleAsync(paymentMethodId, service))
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ISavedPaymentMethodService service)
    {
        var buyerId = _httpContextAccessor.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var deleted = await service.DeleteAsync(paymentMethodId, buyerId);
        return deleted
            ? Results.NoContent()
            : Results.NotFound(new { message = $"Saved card {paymentMethodId} was not found." });
    }
}
