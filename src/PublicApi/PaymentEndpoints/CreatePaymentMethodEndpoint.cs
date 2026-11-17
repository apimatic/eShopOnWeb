using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Saves a card for the signed-in shopper. Only Finix token metadata is stored.
/// </summary>
public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CardRequest, HttpContext>
{
    private readonly IPaymentService _paymentService;

    public CreatePaymentMethodEndpoint(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CardRequest request, HttpContext httpContext) =>
                await HandleAsync(request, httpContext))
            .Produces<SavedCardDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CardRequest request, HttpContext httpContext)
    {
        var card = await _paymentService.SavePaymentMethodAsync(
            CallerIdentity.GetBuyerId(httpContext),
            request.ToCardInput(),
            httpContext.RequestAborted);

        return Results.Created($"api/payment-methods/{card.PaymentMethodId}", card);
    }
}
