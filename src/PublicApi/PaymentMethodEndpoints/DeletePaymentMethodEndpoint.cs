using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>
/// Removes one of the shopper's saved cards. Afterwards it is gone from their list and can no longer
/// be used to pay.
/// </summary>
public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int paymentMethodId, ClaimsPrincipal caller, IPaymentProcessingService payments) =>
            {
                return await HandleAsync(new DeletePaymentMethodRequest(paymentMethodId)
                {
                    Actor = RequestActor.From(caller)
                }, payments);
            })
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, IPaymentProcessingService payments)
    {
        await payments.DeleteSavedCardAsync(request.RequireActor().BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
