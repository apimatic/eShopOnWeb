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

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, CreatePaymentMethodRequest, ISavedCardService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreatePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces<CreatePaymentMethodResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CreatePaymentMethodRequest request, ISavedCardService service, ClaimsPrincipal user)
    {
        var buyerId = EndpointUser.RequireBuyerId(user);
        var saved = await service.SaveAsync(buyerId, new CardPaymentRequest
        {
            Name = request.Card.Name,
            Number = request.Card.Number,
            Expiry = request.Card.Expiry,
            SecurityCode = request.Card.SecurityCode,
            BillingAddress = new BillingAddressRequest
            {
                AddressLine1 = request.Card.BillingAddress.AddressLine1,
                AddressLine2 = request.Card.BillingAddress.AddressLine2,
                AdminArea2 = request.Card.BillingAddress.AdminArea2,
                AdminArea1 = request.Card.BillingAddress.AdminArea1,
                PostalCode = request.Card.BillingAddress.PostalCode,
                CountryCode = request.Card.BillingAddress.CountryCode
            }
        });

        var dto = SavedCardDto.From(saved);
        return Results.Created($"api/payment-methods/{dto.PaymentMethodId}", new CreatePaymentMethodResponse
        {
            PaymentMethodId = dto.PaymentMethodId,
            PaymentMethod = dto
        });
    }
}

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CreatePaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public SavedCardDto PaymentMethod { get; set; } = new();
}
