using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods, HttpContext httpContext) =>
            {
                return await HandleAsync(request, paymentMethods, httpContext);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService paymentMethods) =>
        HandleAsync(request, paymentMethods, null!);

    private async Task<IResult> HandleAsync(
        CreatePaymentMethodRequest request,
        ISavedPaymentMethodService paymentMethods,
        HttpContext httpContext)
    {
        var buyerId = httpContext.GetRequiredUserName();
        if (request.Card == null)
        {
            return Results.BadRequest(new { message = "Card details are required." });
        }

        var saved = await paymentMethods.SaveCardAsync(buyerId, PaymentMapping.ToCardSource(request.Card));
        var response = PaymentMapping.ToPaymentMethodResponse(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", response);
    }
}

public class CreatePaymentMethodRequest
{
    public CardDetailsRequest? Card { get; set; }
}
