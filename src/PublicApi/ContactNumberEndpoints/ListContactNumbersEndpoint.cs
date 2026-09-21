using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's registered numbers (only the caller's own).</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IOrderNotificationService service) =>
                await HandleAsync(http, service, http.RequestAborted))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http, IOrderNotificationService service, CancellationToken ct)
    {
        var buyerId = http.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var numbers = await service.GetContactNumbersAsync(buyerId, ct);
        return Results.Ok(new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto
                {
                    ContactNumberId = n.Id,
                    Number = n.E164Number,
                    CreatedDate = n.CreatedDate
                })
                .ToList()
        });
    }
}
