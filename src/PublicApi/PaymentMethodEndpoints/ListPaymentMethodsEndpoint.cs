using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string? Last4 { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

public class ListPaymentMethodsEndpoint : IEndpoint<IResult, EmptyRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext ctx) =>
            {
                return await HandleAsync(new EmptyRequest(), ctx);
            })
            .Produces<List<SavedCardDto>>()
            .WithTags("PaymentMethodEndpoints");
    }

    public async Task<IResult> HandleAsync(EmptyRequest request, HttpContext ctx)
    {
        var buyerId = ctx.User.FindFirstValue(ClaimTypes.Name)!;
        var sp = ctx.RequestServices;
        var savedCardRepo = sp.GetRequiredService<IReadRepository<SavedCard>>();
        var ct = ctx.RequestAborted;

        var spec = new SavedCardsByBuyerSpec(buyerId);
        var cards = await savedCardRepo.ListAsync(spec, ct);

        var result = new List<SavedCardDto>();
        foreach (var card in cards)
        {
            result.Add(new SavedCardDto
            {
                PaymentMethodId = card.Id,
                Last4 = card.Last4,
                Brand = card.Brand,
                Expiry = card.Expiry
            });
        }

        return Results.Ok(result);
    }
}
