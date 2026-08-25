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
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListCardsResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}

public class SavedCardDto
{
    public int PaymentMethodId { get; set; }
    public string LastFour { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Expiry { get; set; } = "";
    public System.DateTimeOffset AddedAt { get; set; }
}

public class ListCardsEndpoint : IEndpoint<IResult, object, IRepository<SavedCard>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/payment-methods",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IRepository<SavedCard> cardRepo, HttpContext ctx, CancellationToken ct) =>
            {
                var buyerId = ctx.User.FindFirst(ClaimTypes.Name)?.Value;
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                var spec = new SavedCardsByBuyerSpec(buyerId);
                var cards = await cardRepo.ListAsync(spec, ct);

                return Results.Ok(new ListCardsResponse
                {
                    PaymentMethods = cards.Select(c => new SavedCardDto
                    {
                        PaymentMethodId = c.Id,
                        LastFour = c.LastFour,
                        Brand = c.Brand,
                        Expiry = c.Expiry,
                        AddedAt = c.CreatedAt
                    }).ToList()
                });
            })
            .Produces<ListCardsResponse>()
            .WithTags("PaymentMethodEndpoints");
    }

    public Task<IResult> HandleAsync(object request, IRepository<SavedCard> repository)
        => Task.FromResult(Results.Ok() as IResult);
}
