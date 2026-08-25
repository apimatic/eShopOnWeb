using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, EmptyRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, HttpContext ctx) =>
            {
                return await HandleAsync(new EmptyRequest(), ctx, paymentMethodId);
            })
            .Produces(204)
            .Produces(403)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(EmptyRequest request, HttpContext ctx)
        => HandleAsync(request, ctx, 0);

    private async Task<IResult> HandleAsync(EmptyRequest _, HttpContext ctx, int paymentMethodId)
    {
        var buyerId = ctx.User.FindFirstValue(ClaimTypes.Name)!;
        var sp = ctx.RequestServices;
        var savedCardRepo = sp.GetRequiredService<IRepository<SavedCard>>();
        var paypalService = sp.GetRequiredService<IPayPalService>();
        var ct = ctx.RequestAborted;

        var card = await savedCardRepo.GetByIdAsync(paymentMethodId, ct);
        if (card is null) return Results.NotFound("Payment method not found.");
        if (card.BuyerId != buyerId) return Results.Forbid();

        try
        {
            await paypalService.DeleteVaultedCardAsync(card.VaultTokenId, ct);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode ?? 422);
        }

        await savedCardRepo.DeleteAsync(card, ct);

        return Results.NoContent();
    }
}
