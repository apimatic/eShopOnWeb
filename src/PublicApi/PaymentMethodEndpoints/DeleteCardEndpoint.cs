using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeleteCardEndpoint : IEndpoint
{
    private readonly IRepository<SavedCard> _cardRepo;
    private readonly IPayPalGateway _paypal;

    public DeleteCardEndpoint(IRepository<SavedCard> cardRepo, IPayPalGateway paypal)
    {
        _cardRepo = cardRepo;
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx, int paymentMethodId) =>
            {
                var buyerId = ctx.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(paymentMethodId, buyerId, ctx.RequestAborted);
            })
            .Produces(200)
            .ProducesProblem(404)
            .WithTags("PaymentMethodEndpoints");
    }

    private async Task<IResult> HandleAsync(int paymentMethodId, string buyerId, System.Threading.CancellationToken ct)
    {
        var card = await _cardRepo.GetByIdAsync(paymentMethodId, ct);
        if (card == null || card.BuyerId != buyerId) return Results.NotFound();

        try
        {
            await _paypal.DeleteVaultedCardAsync(card.PayPalVaultId, ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem($"Failed to delete card from PayPal: {ex.Message}", statusCode: 502);
        }

        await _cardRepo.DeleteAsync(card, ct);

        return Results.Ok(new { message = "Payment method deleted." });
    }
}
