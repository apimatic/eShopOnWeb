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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Payments.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

/// <summary>
/// GET /api/payment-methods — the caller's own saved cards (safe descriptors only).
/// </summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, ISavedCardService service, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var cards = await service.GetCardsForBuyerAsync(buyerId, ct);
                var response = new ListPaymentMethodsResponse
                {
                    PaymentMethods = cards.Select(saved => new SavedCardDto
                    {
                        PaymentMethodId = saved.Id,
                        Brand = saved.Brand,
                        Last4 = saved.Last4,
                        Expiry = saved.Expiry,
                        CardholderName = saved.CardholderName,
                        Label = saved.Label
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
