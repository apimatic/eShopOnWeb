using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IRepository<ContactNumber>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IRepository<ContactNumber> repository) =>
                await HandleAsync(repository))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<ContactNumber> repository)
    {
        var ownerId = _httpContextAccessor.GetCallerId();
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .OrderByDescending(c => c.RegisteredAt)
                .Select(c => new ContactNumberDto
                {
                    ContactNumberId = c.Id,
                    PhoneNumber = c.PhoneNumber,
                    RegisteredAt = c.RegisteredAt
                })
                .ToList()
        };

        return Results.Ok(response);
    }
}
