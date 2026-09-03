using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreatePaymentMethodRequest request, ClaimsPrincipal user, ISavedPaymentMethodService methods, CancellationToken ct) =>
            {
                var card = request.Card;
                var saved = await methods.SaveAsync(
                    CallerIdentity.BuyerId(user),
                    new CardPaymentDetails(
                        card.Number,
                        card.Expiry,
                        card.SecurityCode,
                        card.Name,
                        card.BillingAddress is null
                            ? null
                            : new CardBillingAddress(
                                card.BillingAddress.CountryCode,
                                card.BillingAddress.AddressLine1,
                                card.BillingAddress.AdminArea1,
                                card.BillingAddress.AdminArea2,
                                card.BillingAddress.PostalCode)),
                    ct);

                return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
                {
                    PaymentMethodId = saved.Id,
                    LastDigits = saved.LastDigits,
                    Brand = saved.Brand,
                    Expiry = saved.Expiry,
                    CardholderName = saved.CardholderName
                });
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService methods) =>
        Task.FromResult(Results.BadRequest());
}
