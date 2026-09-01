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

/// <summary>
/// Removes one of the caller's saved cards, both locally and from PayPal's vault.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IOrderPaymentService _orderPaymentService;

    public DeletePaymentMethodEndpoint(IOrderPaymentService orderPaymentService)
    {
        _orderPaymentService = orderPaymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(paymentMethodId, user);
            })
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user)
    {
        var buyerId = user.Identity!.Name!;
        await _orderPaymentService.DeleteSavedCardAsync(buyerId, paymentMethodId);
        return Results.NoContent();
    }
}
