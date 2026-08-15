using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record ListPaymentMethodsResponse(IReadOnlyList<SavedCardView> PaymentMethods);

/// <summary>Lists the caller's saved cards (safe descriptors only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "List the caller's saved cards", Tags = new[] { "PaymentMethodEndpoints" })]
            async (ISavedCardService savedCardService, HttpContext http, CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                var cards = await savedCardService.ListCardsAsync(buyerId, ct);
                var views = cards.Select(c => new SavedCardView(c.Id, c.Brand, c.Last4, c.Expiry, c.Label)).ToList();
                return Results.Ok(new ListPaymentMethodsResponse(views));
            })
            .Produces<ListPaymentMethodsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }
}
