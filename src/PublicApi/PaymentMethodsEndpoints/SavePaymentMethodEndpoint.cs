using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodsEndpoints;

/// <summary>
/// Saves a card to the provider vault for the signed-in shopper.
/// </summary>
public class SavePaymentMethodEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public SavePaymentMethodEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SavePaymentMethodRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<SavePaymentMethodResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodsEndpoints");
    }

    public async Task<IResult> HandleAsync(SavePaymentMethodRequest request, ClaimsPrincipal user)
    {
        var buyerId = AuthenticatedUser.RequireIdentity(user);

        var saved = await _payments.SaveCardAsync(buyerId, request.Card.ToCredential());

        var response = new SavePaymentMethodResponse(request.CorrelationId())
        {
            PaymentMethodId = saved.ExternalId,
            Brand = NullIfEmpty(saved.Brand),
            Last4 = NullIfEmpty(saved.Last4),
            Expiry = NullIfEmpty(saved.Expiry),
            CardholderName = NullIfEmpty(saved.CardholderName),
            CreatedAt = saved.CreatedAt
        };

        return Results.Created($"/api/payment-methods", response);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
