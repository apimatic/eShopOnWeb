using System;
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

/// <summary>
/// Removes one of the caller's saved cards: deletes the PayPal vault token so it can no
/// longer be used to pay, then removes the local record.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ClaimsPrincipal, int>
{
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPaymentGateway _paymentGateway;

    public DeletePaymentMethodEndpoint(IRepository<SavedCard> savedCardRepository, IPaymentGateway paymentGateway)
    {
        _savedCardRepository = savedCardRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(), user, paymentMethodId);
            })
            .Produces<DeletePaymentMethodResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ClaimsPrincipal user, int paymentMethodId)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId);
        if (savedCard == null || savedCard.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        try
        {
            await _paymentGateway.DeletePaymentTokenAsync(savedCard.PayPalPaymentTokenId);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; removing the local record is still correct.
        }

        await _savedCardRepository.DeleteAsync(savedCard);

        return Results.Ok(new DeletePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = paymentMethodId,
            Deleted = true
        });
    }
}
