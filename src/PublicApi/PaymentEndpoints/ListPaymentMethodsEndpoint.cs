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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record ListPaymentMethodsQuery(string BuyerId, CancellationToken Ct);

/// <summary>GET /api/payment-methods — the caller's saved cards (safe descriptions only).</summary>
public class ListPaymentMethodsEndpoint : IEndpoint<IResult, ListPaymentMethodsQuery, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new ListPaymentMethodsQuery(buyerId, http.RequestAborted), service);
            })
            .Produces<IReadOnlyList<SavedCardDto>>()
            .WithTags("PaymentMethods");
    }

    public async Task<IResult> HandleAsync(ListPaymentMethodsQuery query, IPaymentApplicationService service)
    {
        var cards = await service.GetCardsForBuyerAsync(query.BuyerId, query.Ct);
        return Results.Ok(cards.OrderByDescending(c => c.CreatedAt).Select(PaymentMapper.ToDto).ToList());
    }
}
