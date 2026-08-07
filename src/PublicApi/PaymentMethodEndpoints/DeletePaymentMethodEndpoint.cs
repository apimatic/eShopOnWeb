using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Logging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards. After this the card no longer appears among the caller's
/// saved cards and can no longer be used to pay; the vault token is also removed at PayPal (best effort).
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodEndpoint.DeleteRouteRequest, ClaimsPrincipal>
{
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly ILogger<DeletePaymentMethodEndpoint> _logger;

    public DeletePaymentMethodEndpoint(
        IRepository<Buyer> buyerRepository,
        IPayPalPaymentGateway payPal,
        ILogger<DeletePaymentMethodEndpoint> logger)
    {
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _logger = logger;
    }

    public class DeleteRouteRequest : BaseRequest
    {
        public int PaymentMethodId { get; set; }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
                await HandleAsync(new DeleteRouteRequest { PaymentMethodId = paymentMethodId }, user))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteRouteRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.GetBuyerId();
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId));

        // Scoping through the caller's own buyer guarantees one shopper can't delete another's card.
        var paymentMethod = buyer?.FindPaymentMethod(request.PaymentMethodId);
        if (buyer == null || paymentMethod == null)
        {
            return Results.NotFound(new { message = $"Saved payment method {request.PaymentMethodId} was not found." });
        }

        var vaultId = paymentMethod.VaultId;
        buyer.RemovePaymentMethod(request.PaymentMethodId);
        await _buyerRepository.UpdateAsync(buyer);

        // Best-effort cleanup at PayPal so no orphaned vault token is left behind. The card is already
        // unusable through this application once removed above.
        try
        {
            await _payPal.DeleteVaultedCardAsync(vaultId);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Removed saved card {PaymentMethodId} locally but failed to delete the PayPal vault token.",
                request.PaymentMethodId);
        }

        return Results.NoContent();
    }
}
