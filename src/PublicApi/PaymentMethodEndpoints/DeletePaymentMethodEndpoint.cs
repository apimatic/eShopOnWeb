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

/// <summary>Removes a saved card. Afterwards it no longer appears in the caller's saved cards
/// and can no longer be used to pay (the vault token is invalidated at PayPal too).</summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentMethodService paymentMethodService) =>
            {
                var request = new DeletePaymentMethodRequest(user.Identity?.Name ?? string.Empty, paymentMethodId);
                return await HandleAsync(request, paymentMethodService);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentMethodService paymentMethodService)
    {
        var response = new DeletePaymentMethodResponse(request.CorrelationId());

        await paymentMethodService.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId);

        response.PaymentMethodId = request.PaymentMethodId;
        return Results.Ok(response);
    }
}
