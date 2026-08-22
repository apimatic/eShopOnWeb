using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodEndpoint : IEndpoint<IResult, SavePaymentMethodRequest, ISavedPaymentMethodService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SavePaymentMethodRequest request, ISavedPaymentMethodService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService service)
        => HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ISavedPaymentMethodService service, ClaimsPrincipal user)
    {
        CardBillingAddress? address = request.BillingAddress == null
            ? null
            : new CardBillingAddress(
                request.BillingAddress.AddressLine1,
                request.BillingAddress.AddressLine2,
                request.BillingAddress.AdminArea2,
                request.BillingAddress.AdminArea1,
                request.BillingAddress.PostalCode,
                request.BillingAddress.CountryCode);

        var card = new CardPaymentSource(request.Number, request.Expiry, request.SecurityCode, request.Name, address);
        var saved = await service.SaveCardAsync(user.GetBuyerId(), card);
        return Results.Created($"api/payment-methods/{saved.Id}", saved.ToResponse());
    }
}

internal static class SavedPaymentMethodMapping
{
    public static object ToResponse(this SavedPaymentMethod method) => new
    {
        paymentMethodId = method.Id,
        brand = method.Brand,
        last4 = method.Last4,
        expiry = method.Expiry,
        cardholderName = method.CardholderName,
        createdAt = method.CreatedAt
    };
}
