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

/// <summary>Saves a card for the signed-in shopper, so a later order can be paid without re-entering it.</summary>
public class CreatePaymentMethodEndpoint
    : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethodService, HttpContext context) =>
            {
                return await HandleAsync(request, paymentMethodService, context);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request,
        IPaymentMethodService paymentMethodService, HttpContext context)
    {
        var buyerId = context.BuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (request.Card is null)
        {
            return Results.BadRequest(new { message = "Send a 'card' object with the card to save." });
        }

        var saved = await paymentMethodService.SaveCardAsync(buyerId, request.Card.ToCardDetails(),
            context.RequestAborted);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.PaymentMethodId,
            PaymentMethod = saved
        };

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}
