using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IReadRepository<SavedPaymentMethod> paymentMethodRepository, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var cards = await paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), ct);

                var response = new ListPaymentMethodsResponse
                {
                    PaymentMethods = cards.Select(pm => new SavedCardDto
                    {
                        PaymentMethodId = pm.Id,
                        Brand = pm.CardBrand,
                        Last4 = pm.LastFourDigits,
                        Expiry = pm.Expiry,
                        Alias = pm.Alias
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>(StatusCodes.Status200OK)
            .WithTags("PaymentMethodEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Lists the caller's saved cards", "Returns the signed-in shopper's saved cards."));
    }
}
