using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the caller's saved cards, both from PayPal's vault and from
/// this application. Afterwards it can no longer be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest>
{
    private readonly IPayPalClient _payPalClient;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly ICurrentUser _currentUser;

    public DeletePaymentMethodEndpoint(IPayPalClient payPalClient,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        ICurrentUser currentUser)
    {
        _payPalClient = payPalClient;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _currentUser = currentUser;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest { PaymentMethodId = paymentMethodId });
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request)
    {
        var savedMethod = await _savedPaymentMethodRepository.GetByIdAsync(request.PaymentMethodId);

        // Deliberately indistinguishable from "does not exist" so one shopper
        // cannot probe another shopper's saved cards.
        if (savedMethod is null || savedMethod.BuyerId != _currentUser.BuyerId)
        {
            throw new ArgumentException($"Saved payment method {request.PaymentMethodId} does not exist.");
        }

        try
        {
            await _payPalClient.DeleteVaultedCardAsync(savedMethod.VaultTokenId);
        }
        catch (PayPalApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone from PayPal's vault; still remove the local record.
        }

        await _savedPaymentMethodRepository.DeleteAsync(savedMethod);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}
