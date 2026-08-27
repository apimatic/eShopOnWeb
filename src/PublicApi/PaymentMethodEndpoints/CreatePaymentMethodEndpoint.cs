using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodResponse : BaseResponse
{
    public CreatePaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public CreatePaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

/// <summary>
/// Saves a card for the signed-in shopper. Full card details go straight to the
/// payment provider's vault; only safe display data is kept.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            ([FromBody] CreatePaymentMethodRequest request, HttpContext httpContext,
                ISavedCardService savedCardService, CancellationToken cancellationToken) =>
            {
                var buyerId = httpContext.User.GetBuyerId();
                var savedCard = await savedCardService.SaveCardAsync(buyerId, request.Card.ToModel(), cancellationToken);

                var response = new CreatePaymentMethodResponse(request.CorrelationId())
                {
                    PaymentMethodId = savedCard.Id,
                    LastDigits = savedCard.LastDigits,
                    Brand = savedCard.Brand,
                    Expiry = savedCard.Expiry
                };
                return Results.Created($"api/payment-methods/{savedCard.Id}", response);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }
}
