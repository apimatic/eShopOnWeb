using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardPaymentRequest Card { get; set; } = new();
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
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
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService service) =>
            {
                return await HandleAsync(request, service);
            })
            .Produces<PaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.RequireBuyerId();
        var card = request.Card;
        var saved = await service.SaveCardAsync(buyerId, new CardPaymentSource
        {
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            BillingAddress = card.BillingAddress == null
                ? null
                : new CardBillingAddress
                {
                    AddressLine1 = card.BillingAddress.AddressLine1,
                    AddressLine2 = card.BillingAddress.AddressLine2,
                    AdminArea2 = card.BillingAddress.AdminArea2,
                    AdminArea1 = card.BillingAddress.AdminArea1,
                    PostalCode = card.BillingAddress.PostalCode,
                    CountryCode = card.BillingAddress.CountryCode
                }
        });

        var response = new PaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            LastDigits = saved.LastDigits,
            Expiry = saved.Expiry,
            CardholderName = saved.CardholderName
        };

        return Results.Created($"api/payment-methods/{response.PaymentMethodId}", response);
    }
}
