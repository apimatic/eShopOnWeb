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

/// <summary>
/// Save a card for the signed-in shopper. Finix tokenizes the card; this application
/// stores only the Finix payment-instrument id and safe display metadata.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IOrderPaymentService, ClaimsPrincipal>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IOrderPaymentService paymentService, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, paymentService, user);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IOrderPaymentService paymentService,
        ClaimsPrincipal user)
    {
        var card = await paymentService.SaveCardAsync(user.GetUserName(), request.ToCardDetails());

        var response = new SavePaymentMethodResponse
        {
            PaymentMethodId = card.Id,
            PaymentMethod = PaymentMethodDto.From(card)
        };

        return Results.Created($"api/payment-methods/{card.Id}", response);
    }
}
