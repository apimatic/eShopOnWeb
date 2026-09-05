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

/// <summary>The saved cards belonging to the signed-in shopper, and only those.</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal caller, IPaymentProcessingService payments) =>
            {
                // A GET has no body, so the caller is taken from the token and put on the request here.
                return await HandleAsync(new ListPaymentMethodsRequest { Actor = RequestActor.From(caller) }, payments);
            })
            .Produces<PaymentMethodListResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsRequest request, IPaymentProcessingService payments)
    {
        var actor = request.RequireActor();
        var response = new PaymentMethodListResponse(request.CorrelationId());

        var cards = await payments.GetSavedCardsAsync(actor.BuyerId);
        foreach (var card in cards)
        {
            response.PaymentMethods.Add(PaymentMethodDto.From(card));
        }

        return Results.Ok(response);
    }
}
