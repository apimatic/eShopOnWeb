using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Payment;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext ctx,
                   IRepository<SavedCard> savedCardRepo,
                   IPayPalService payPalService) =>
            {
                var buyerId = ctx.User.Identity?.Name ?? string.Empty;
                var request = new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = buyerId };
                return await HandleAsync(request, savedCardRepo, payPalService);
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<SavedCard> repo)
        => throw new System.NotSupportedException();

    private static async Task<IResult> HandleAsync(
        DeletePaymentMethodRequest request,
        IRepository<SavedCard> savedCardRepo,
        IPayPalService payPalService)
    {
        if (string.IsNullOrEmpty(request.BuyerId)) return Results.Unauthorized();

        var card = await savedCardRepo.GetByIdAsync(request.PaymentMethodId);
        if (card == null || card.BuyerId != request.BuyerId || card.IsDeleted)
            return Results.NotFound(new { error = "Payment method not found." });

        try
        {
            await payPalService.DeleteVaultedCardAsync(card.VaultTokenId);
        }
        catch (PayPalProviderException)
        {
            // If PayPal already deleted it, proceed with local deletion
        }

        card.MarkDeleted();
        await savedCardRepo.UpdateAsync(card);

        return Results.NoContent();
    }
}
