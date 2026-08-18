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

/// <summary>
/// Removes one of the signed-in shopper's numbers. Afterwards it no longer appears among their numbers,
/// and — because messaging always resolves a currently-registered number — nothing is ever sent to it again.
/// A shopper can only delete their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IRepository<ContactNumber>>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IRepository<ContactNumber> repository) =>
                await HandleAsync(contactNumberId, repository))
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, IRepository<ContactNumber> repository)
    {
        var owner = _httpContextAccessor.GetUserName();
        if (string.IsNullOrEmpty(owner))
        {
            return Results.Unauthorized();
        }

        var ct = _httpContextAccessor.RequestAborted();
        var contactNumber = await repository.FirstOrDefaultAsync(new ContactNumberByIdForOwnerSpecification(contactNumberId, owner), ct);
        if (contactNumber is null)
        {
            return Results.NotFound();
        }

        await repository.DeleteAsync(contactNumber, ct);
        return Results.NoContent();
    }
}
