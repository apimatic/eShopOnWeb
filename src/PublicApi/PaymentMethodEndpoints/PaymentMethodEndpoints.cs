using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodApiRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CardPaymentRequest request, ISavedCardService cards, ClaimsPrincipal user) =>
            {
                var apiRequest = new SavePaymentMethodApiRequest
                {
                    BuyerId = BuyerIdentity.GetRequiredBuyerId(user),
                    Card = request
                };
                return await HandleAsync(apiRequest, cards);
            })
            .Produces<SavedCardDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodApiRequest request, ISavedCardService cards)
    {
        var saved = await cards.SaveCardAsync(request.BuyerId, new CardPaymentCommand
        {
            Number = request.Card.Number ?? string.Empty,
            Expiry = request.Card.Expiry ?? string.Empty,
            SecurityCode = request.Card.SecurityCode,
            Name = request.Card.Name,
            BillingAddress = request.Card.BillingAddress == null
                ? null
                : new BillingAddressCommand
                {
                    AddressLine1 = request.Card.BillingAddress.AddressLine1,
                    AddressLine2 = request.Card.BillingAddress.AddressLine2,
                    AdminArea1 = request.Card.BillingAddress.AdminArea1,
                    AdminArea2 = request.Card.BillingAddress.AdminArea2,
                    PostalCode = request.Card.BillingAddress.PostalCode,
                    CountryCode = request.Card.BillingAddress.CountryCode
                }
        });

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", saved);
    }
}

public class SavePaymentMethodApiRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardPaymentRequest Card { get; set; } = new();
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, string, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ISavedCardService cards, ClaimsPrincipal user) =>
                await HandleAsync(BuyerIdentity.GetRequiredBuyerId(user), cards))
            .Produces<IReadOnlyList<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, ISavedCardService cards)
    {
        var result = await cards.ListAsync(buyerId);
        return Results.Ok(result);
    }
}

public class DeletePaymentMethodRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int PaymentMethodId { get; set; }
}

public class DeletePaymentMethodEndpoint : IEndpoint<IResult, DeletePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/payment-methods/{paymentMethodId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int paymentMethodId, ISavedCardService cards, ClaimsPrincipal user) =>
                await HandleAsync(new DeletePaymentMethodRequest
                {
                    BuyerId = BuyerIdentity.GetRequiredBuyerId(user),
                    PaymentMethodId = paymentMethodId
                }, cards))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(DeletePaymentMethodRequest request, ISavedCardService cards)
    {
        await cards.DeleteAsync(request.BuyerId, request.PaymentMethodId);
        return Results.NoContent();
    }
}
