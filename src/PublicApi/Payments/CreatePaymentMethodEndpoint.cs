using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, IPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreatePaymentMethodEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, IPaymentService paymentService) =>
            {
                return await HandleAsync(request, paymentService);
            })
            .Produces<SavedCardResult>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var card = new CardDetails(
            request.Card.Number,
            request.Card.ExpirationMonth,
            request.Card.ExpirationYear,
            request.Card.SecurityCode,
            request.Card.Name,
            request.Card.BillingAddress is null
                ? null
                : new BillingAddressDetails(
                    request.Card.BillingAddress.Street,
                    request.Card.BillingAddress.City,
                    request.Card.BillingAddress.State,
                    request.Card.BillingAddress.Country,
                    request.Card.BillingAddress.ZipCode));

        var result = await paymentService.SaveCardAsync(
            buyerId,
            buyerId,
            card,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Created($"api/payment-methods/{result.PaymentMethodId}", result);
    }
}
