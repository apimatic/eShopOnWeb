using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Lists the caller's registered contact numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(user, contactNumberRepository, cancellationToken);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    private async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, CancellationToken cancellationToken)
    {
        var buyerId = user.Identity?.Name ?? string.Empty;
        var response = new ListContactNumbersResponse();

        var numbers = (await contactNumberRepository.ListAsync(cancellationToken))
            .Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber,
                CreatedAt = c.CreatedAt
            });

        response.ContactNumbers.AddRange(numbers);
        return Results.Ok(response);
    }
}
