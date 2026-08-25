using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeleteCardRequest
{
    public int PaymentMethodId { get; set; }
}

public class DeleteCardEndpoint : IEndpoint<IResult, DeleteCardRequest, IRepository<SavedCard>>
{
    private readonly IPayPalClient _paypal;

    public DeleteCardEndpoint(IPayPalClient paypal)
    {
        _paypal = paypal;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, IRepository<SavedCard> cardRepo,
                   HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                return await HandleAsync(new DeleteCardRequest { PaymentMethodId = paymentMethodId },
                    cardRepo, buyerId, ct);
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteCardRequest request, IRepository<SavedCard> repository)
        => HandleAsync(request, repository, null);

    private async Task<IResult> HandleAsync(DeleteCardRequest request, IRepository<SavedCard> cardRepo,
        string? buyerId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var spec = new SavedCardByIdSpec(request.PaymentMethodId, buyerId);
        var card = await cardRepo.FirstOrDefaultAsync(spec, ct);
        if (card == null)
            return Results.NotFound();

        try
        {
            await _paypal.DeletePaymentTokenAsync(card.VaultId, ct);
        }
        catch (PayPalException ex)
        {
            // Log and continue — if PayPal says not found, local delete is still correct
            if (ex.PayPalErrorName != "RESOURCE_NOT_FOUND")
                return Results.BadRequest(new { error = ex.Message, code = ex.PayPalErrorName });
        }

        card.SoftDelete();
        await cardRepo.UpdateAsync(card, ct);

        return Results.NoContent();
    }
}
