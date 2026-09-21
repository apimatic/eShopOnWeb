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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's registered contact numbers.</summary>
public class ListContactNumbersEndpoint
    : IEndpoint<IResult, string, IContactNumberService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(buyerId, service, ct);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IContactNumberService service, CancellationToken ct)
    {
        var numbers = await service.ListAsync(buyerId, ct);
        var response = new ListContactNumbersResponse(
            numbers.Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.CreatedAt)).ToList());
        return Results.Ok(response);
    }
}
