using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

/// <summary>Saves a card for the authenticated shopper by vaulting it at PayPal.</summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, HttpContext http)
    {
        var buyerId = CallerIdentity.GetBuyerId(http.User);
        var paymentMethodService = http.RequestServices.GetRequiredService<IPaymentMethodService>();

        if (request.Card is null)
        {
            return Results.BadRequest(new { message = "A 'card' is required." });
        }

        var saved = await paymentMethodService.SaveCardAsync(
            buyerId, request.Card.ToDomain(), request.Alias, http.RequestAborted);

        var response = new CreatePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.PaymentMethodId,
            Alias = saved.Alias,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            Expiry = saved.Expiry
        };

        return Results.Created($"api/payment-methods/{saved.PaymentMethodId}", response);
    }
}
