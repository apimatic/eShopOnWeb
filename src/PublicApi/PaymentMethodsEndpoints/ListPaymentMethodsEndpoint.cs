using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentDtos;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodsEndpoints;

/// <summary>
/// The caller's saved cards (display-safe fields only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    private readonly IOrderPaymentService _payments;

    public ListPaymentMethodsEndpoint(IOrderPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user) =>
            {
                return await HandleAsync(user);
            })
            .Produces<ListPaymentMethodsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("PaymentMethodsEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user)
    {
        var buyerId = AuthenticatedUser.RequireIdentity(user);
        var cards = await _payments.ListCardsAsync(buyerId);

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = cards.Select(card => new SavedCardDto
            {
                PaymentMethodId = card.ExternalId,
                Brand = NullIfEmpty(card.Brand),
                Last4 = NullIfEmpty(card.Last4),
                Expiry = NullIfEmpty(card.Expiry),
                CardholderName = NullIfEmpty(card.CardholderName),
                CreatedAt = card.CreatedAt
            }).ToList()
        };

        return Results.Ok(response);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}
