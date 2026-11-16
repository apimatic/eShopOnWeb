using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. Card details are sent to Finix and never persisted.
/// </summary>
public class CreatePaymentMethodEndpoint
    : IEndpoint<IResult, SavePaymentMethodRequest, ClaimsPrincipal, IPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (SavePaymentMethodRequest request, ClaimsPrincipal user, IPaymentService paymentService) =>
                    await HandleAsync(request, user, paymentService))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user,
        IPaymentService paymentService)
    {
        var card = request.ResolveCard()
                   ?? throw PaymentException.BadRequest("Card details are required.");

        var saved = await paymentService.SavePaymentMethodAsync(user.GetBuyerId(), card);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = PaymentMapping.ToSavedPaymentMethodResponse(saved)
        };

        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}
