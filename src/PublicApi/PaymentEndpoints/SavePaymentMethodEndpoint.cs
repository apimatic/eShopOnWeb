using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Saves a card as a Finix payment instrument for the signed-in shopper.</summary>
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
        var buyerId = user.Identity?.Name;
        var saved = await paymentService.SavePaymentMethodAsync(buyerId!, request);
        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.PaymentMethodId,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear,
            CardholderName = saved.CardholderName
        };

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}
