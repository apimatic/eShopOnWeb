using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ISavedPaymentMethodService saved, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                request.BuyerId = buyerId;
                return await HandleAsync(request, saved);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService saved)
    {
        var card = new CardPaymentDetails
        {
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            Name = request.Card.Name,
            BillingAddress = request.Card.BillingAddress is null ? null : new CardBillingAddress
            {
                AddressLine1 = request.Card.BillingAddress.AddressLine1,
                AddressLine2 = request.Card.BillingAddress.AddressLine2,
                AdminArea2 = request.Card.BillingAddress.AdminArea2,
                AdminArea1 = request.Card.BillingAddress.AdminArea1,
                PostalCode = request.Card.BillingAddress.PostalCode,
                CountryCode = request.Card.BillingAddress.CountryCode ?? "US"
            }
        };

        var method = await saved.SaveCardAsync(request.BuyerId!, card, request.Alias, default);
        var response = new CreatePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            PaymentMethod = PaymentMethodDto.From(method)
        };

        return Results.Created($"api/payment-methods/{method.Id}", response);
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public string? Alias { get; set; }
    public PayCardDetailsRequest Card { get; set; } = new();
    internal string? BuyerId { get; set; }
}

public class PayCardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public PayCardBillingAddressRequest? BillingAddress { get; set; }
}

public class PayCardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}
