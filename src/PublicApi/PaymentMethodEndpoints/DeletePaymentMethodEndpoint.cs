using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the shopper's saved cards. Afterwards it no longer appears in their list and can
/// no longer be used to pay. Scoped to the owner, so one shopper cannot delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;

    public DeletePaymentMethodEndpoint(IRepository<SavedPaymentMethod> savedCardRepository)
    {
        _savedCardRepository = savedCardRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, HttpContext http) => await HandleAsync(paymentMethodId, http))
            .Produces<DeletePaymentMethodResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(int paymentMethodId, HttpContext http)
    {
        var buyerId = http.User.GetBuyerId();

        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpecification(paymentMethodId, buyerId), http.RequestAborted);
        if (savedCard is null)
        {
            return Results.NotFound();
        }

        await _savedCardRepository.DeleteAsync(savedCard, http.RequestAborted);

        return Results.Ok(new DeletePaymentMethodResponse());
    }
}
