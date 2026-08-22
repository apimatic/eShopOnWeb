using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, IPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, IPaymentMethodService paymentMethods, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                var billing = request.BillingAddress is null
                    ? null
                    : new CardBillingAddress(
                        request.BillingAddress.AddressLine1,
                        request.BillingAddress.AddressLine2,
                        request.BillingAddress.AdminArea2,
                        request.BillingAddress.AdminArea1,
                        request.BillingAddress.PostalCode,
                        request.BillingAddress.CountryCode);

                var card = new CardPayment(
                    request.Number ?? string.Empty,
                    request.Expiry ?? string.Empty,
                    request.SecurityCode ?? string.Empty,
                    request.Name ?? string.Empty,
                    billing);

                var saved = await paymentMethods.SaveCardAsync(buyerId, card);
                var dto = PaymentMethodDto.From(saved);
                return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", dto);
            })
            .Produces<PaymentMethodDto>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, IPaymentMethodService paymentMethods) =>
        throw new System.NotSupportedException("Use the route handler.");
}

public class CreatePaymentMethodRequest
{
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public PaymentMethodBillingAddressRequest? BillingAddress { get; set; }

    public override string ToString() => "[card redacted]";
}

public class PaymentMethodBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? Last4 { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
    public string? DisplayName { get; set; }

    public static PaymentMethodDto From(PaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            Last4 = method.Last4,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName,
            DisplayName = method.Alias
        };
}
