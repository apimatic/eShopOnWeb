using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Removes a saved card for the signed-in shopper. Afterwards it no longer appears among the caller's
/// saved cards and can no longer be used to pay. One shopper can never delete another's card.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal user, IPaymentService paymentService) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId, BuyerIdentity.GetBuyerId(user)), paymentService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentService paymentService)
    {
        await paymentService.DeleteSavedCardAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}

public class DeletePaymentMethodRequest
{
    public DeletePaymentMethodRequest(int paymentMethodId, string buyerId)
    {
        PaymentMethodId = paymentMethodId;
        BuyerId = buyerId;
    }

    public int PaymentMethodId { get; }
    public string BuyerId { get; }
}
