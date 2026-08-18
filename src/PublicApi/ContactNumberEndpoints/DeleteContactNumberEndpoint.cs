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
/// DELETE /api/contact-numbers/{contactNumberId} — remove one of the caller's own numbers. Scoped to the
/// owner: a shopper can never delete another's. Afterwards it no longer appears among the caller's numbers and
/// is never sent to again (sends only ever read currently-registered numbers).
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>>
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
                await HandleAsync(new DeleteContactNumberRequest(contactNumberId), repository))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> repository)
    {
        var owner = EndpointUser.Name(_httpContextAccessor);
        if (string.IsNullOrEmpty(owner))
            return Results.Unauthorized();

        // Scope by owner so one shopper can never resolve — let alone delete — another's number.
        var number = await repository.FirstOrDefaultAsync(
            new ContactNumberByIdAndOwnerSpecification(request.ContactNumberId, owner));
        if (number is null)
            return Results.NotFound();

        await repository.DeleteAsync(number);
        return Results.NoContent();
    }
}
