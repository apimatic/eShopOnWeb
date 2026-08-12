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

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IContactNumberService service, CancellationToken cancellationToken) =>
            {
                var owner = CurrentUser.GetUserName(user);
                if (owner is null)
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListForOwnerAsync(owner, cancellationToken);

                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers
                        .Select(n => new ContactNumberDto
                        {
                            ContactNumberId = n.Id,
                            PhoneNumber = n.E164Number,
                            RegisteredAt = n.RegisteredAt
                        })
                        .ToList()
                };

                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}
