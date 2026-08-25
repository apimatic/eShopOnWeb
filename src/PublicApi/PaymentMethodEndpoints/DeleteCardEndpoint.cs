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
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeleteCardRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public class DeleteCardEndpoint : IEndpoint<IResult, DeleteCardRequest, IPayPalPaymentService>
{
    private readonly IRepository<SavedCard> _cardRepository;

    public DeleteCardEndpoint(IRepository<SavedCard> cardRepository)
    {
        _cardRepository = cardRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ClaimsPrincipal user, IPayPalPaymentService paymentService) =>
            {
                var request = new DeleteCardRequest
                {
                    PaymentMethodId = paymentMethodId,
                    BuyerId = user.FindFirstValue(ClaimTypes.Name) ?? string.Empty
                };
                return await HandleAsync(request, paymentService);
            })
            .Produces(204)
            .Produces(404)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteCardRequest request, IPayPalPaymentService paymentService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var spec = new SavedCardByIdAndBuyerSpec(request.PaymentMethodId, request.BuyerId);
        var card = await _cardRepository.FirstOrDefaultAsync(spec);
        if (card is null) return Results.NotFound(new { error = "Payment method not found." });

        try
        {
            await paymentService.DeleteVaultTokenAsync(card.VaultId, CancellationToken.None);
        }
        catch (PayPalException ex)
        {
            return Results.Problem(ex.Message, statusCode: ex.StatusCode);
        }

        await _cardRepository.DeleteAsync(card);
        return Results.NoContent();
    }
}
