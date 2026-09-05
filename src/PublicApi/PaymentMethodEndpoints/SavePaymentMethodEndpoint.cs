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
/// Saves a card for the signed-in shopper so a later order can be paid for without entering it again.
/// The response says which card it is in terms the shopper recognises; a card number is never stored
/// here and never comes back.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ClaimsPrincipal caller, IPaymentProcessingService payments) =>
            {
                request.Actor = RequestActor.From(caller);
                return await HandleAsync(request, payments);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentProcessingService payments)
    {
        var actor = request.RequireActor();

        if (request.Card is null)
        {
            return Results.BadRequest(new { message = "Card details are required to save a card." });
        }

        var card = actor.ToCardDetails(request.Card)
            ?? throw new ArgumentException("Card details are required to save a card.", nameof(request));

        var saved = await payments.SaveCardAsync(actor.BuyerId, card, request.Nickname);

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMethodDto.From(saved)
        };

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
