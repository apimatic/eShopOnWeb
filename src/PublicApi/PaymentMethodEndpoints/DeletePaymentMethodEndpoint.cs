using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public bool Deleted { get; set; }
}

/// <summary>
/// Removes one of the caller's saved cards, both locally and from PayPal's vault.
/// Afterwards it can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId,
             ClaimsPrincipal user,
             IRepository<SavedPaymentMethod> paymentMethodRepository,
             IPayPalClient payPalClient) =>
            {
                return await HandleAsync(paymentMethodId, user, paymentMethodRepository, payPalClient);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, ClaimsPrincipal user,
        IRepository<SavedPaymentMethod> paymentMethodRepository, IPayPalClient payPalClient)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var savedCard = await paymentMethodRepository.GetByIdAsync(paymentMethodId);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            return Results.NotFound(new { message = $"Payment method {paymentMethodId} not found." });
        }

        try
        {
            await payPalClient.DeletePaymentTokenAsync(savedCard.VaultTokenId);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; still remove it locally.
        }

        await paymentMethodRepository.DeleteAsync(savedCard);

        return Results.Ok(new DeletePaymentMethodResponse { PaymentMethodId = paymentMethodId, Deleted = true });
    }
}
