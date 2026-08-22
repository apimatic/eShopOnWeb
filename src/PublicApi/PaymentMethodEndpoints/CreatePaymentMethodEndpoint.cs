using System.Security.Claims;
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

    [System.Text.Json.Serialization.JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ISavedPaymentMethodService service, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.RequireBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedPaymentMethodService service)
    {
        var saved = await service.SaveCardAsync(request.BuyerId, new CardPaymentDetails
        {
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            Name = request.Card.Name,
            BillingAddress = request.Card.BillingAddress is null ? null : new BillingAddressDetails
            {
                AddressLine1 = request.Card.BillingAddress.AddressLine1,
                AddressLine2 = request.Card.BillingAddress.AddressLine2,
                AdminArea2 = request.Card.BillingAddress.AdminArea2,
                AdminArea1 = request.Card.BillingAddress.AdminArea1,
                PostalCode = request.Card.BillingAddress.PostalCode,
                CountryCode = request.Card.BillingAddress.CountryCode
            }
        });

        var dto = PaymentResponseMapper.ToDto(saved);
        return Results.Created($"api/payment-methods/{saved.Id}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            PaymentMethod = dto
        });
    }
}
