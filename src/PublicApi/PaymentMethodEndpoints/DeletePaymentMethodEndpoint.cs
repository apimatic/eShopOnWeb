using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's saved cards. Afterwards it no longer appears among
/// the caller's saved cards and can no longer be used to pay. A card that isn't the caller's
/// yields 404 so it cannot be probed for or deleted.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, int, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, ISavedCardService savedCardService) =>
            {
                var buyerId = user.GetBuyerId();
                var deleted = await savedCardService.DeleteCardAsync(buyerId, paymentMethodId);

                return deleted
                    ? Results.Ok(new DeletePaymentMethodResponse { PaymentMethodId = paymentMethodId })
                    : Results.NotFound();
            })
            .Produces<DeletePaymentMethodResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("PaymentMethodEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Removes a saved card", "Deletes a saved card so it can no longer be used."));
    }

    public Task<IResult> HandleAsync(int paymentMethodId, ISavedCardService savedCardService) =>
        Task.FromResult(Results.Ok());
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public DeletePaymentMethodResponse(Guid correlationId) : base(correlationId) { }

    public DeletePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
}
