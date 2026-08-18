using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>GET /api/contact-numbers — the caller's own registered numbers.</summary>
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
            (IRepository<ContactNumber> repository) => await HandleAsync(repository))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IRepository<ContactNumber> repository)
    {
        var owner = EndpointUser.Name(_httpContextAccessor);
        if (string.IsNullOrEmpty(owner))
            return Results.Unauthorized();

        var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(owner));

        return Results.Ok(new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(c => new ContactNumberDto { ContactNumberId = c.Id, PhoneNumber = c.PhoneNumber })
                .ToList()
        });
    }
}
