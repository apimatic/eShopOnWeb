using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, IContactNumberService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(http.User);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListAsync(buyerId, ct);
                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers.Select(n => new ContactNumberItem
                    {
                        ContactNumberId = n.ContactNumberId,
                        PhoneNumber = n.PhoneNumber,
                        CountryCode = n.CountryCode,
                        RegisteredAtUtc = n.RegisteredAtUtc
                    }).ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}
