using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payment;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
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
            (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethods) =>
            {
                return await HandleAsync(request, paymentMethods);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethods)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        var card = new CardPaymentInput(
            request.Number,
            request.Expiry,
            request.SecurityCode,
            request.Name,
            request.BillingAddress is null
                ? null
                : new CardBillingAddress(
                    request.BillingAddress.AddressLine1,
                    request.BillingAddress.AddressLine2,
                    request.BillingAddress.AdminArea2,
                    request.BillingAddress.AdminArea1,
                    request.BillingAddress.PostalCode,
                    request.BillingAddress.CountryCode));

        var saved = await paymentMethods.SaveAsync(buyerId, card, default);
        var dto = new PaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };
        return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = dto
        });
    }
}
