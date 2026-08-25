using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IRepository<SavedCard>>
{
    private readonly IPayPalService _paypal;

    public DeletePaymentMethodEndpoint(IPayPalService paypal) => _paypal = paypal;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, IRepository<SavedCard> cardRepo) =>
            {
                return await HandleAsync(
                    new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId, BuyerId = user.Identity?.Name ?? "" },
                    cardRepo);
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IRepository<SavedCard> cardRepo)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new SavedCardByIdAndBuyerSpec(request.PaymentMethodId, request.BuyerId);
        var card = await cardRepo.FirstOrDefaultAsync(spec);

        if (card == null || card.IsDeleted)
            return Results.NotFound();

        try
        {
            await _paypal.DeleteVaultedCardAsync(card.PaymentTokenId, CancellationToken.None);
        }
        catch (PayPalException)
        {
            // Even if PayPal deletion fails, mark locally as deleted so it can't be used
        }

        card.Delete();
        await cardRepo.UpdateAsync(card);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = "";
}
