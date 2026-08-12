using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Scoped to the caller: a number that is not
/// the caller's own is treated as not found. Afterwards the number no longer appears among the caller's
/// numbers and nothing is ever sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IHttpContextAccessor _http;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumbers, IHttpContextAccessor http)
    {
        _contactNumbers = contactNumbers;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId) => await HandleAsync(contactNumberId))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId)
    {
        var ct = _http.HttpContext!.RequestAborted;
        var ownerId = NotificationPresentation.CallerId(_http.HttpContext!.User);

        var contactNumber = await _contactNumbers.FirstOrDefaultAsync(
            new ContactNumberByIdForOwnerSpecification(contactNumberId, ownerId), ct);
        if (contactNumber == null)
        {
            return Results.NotFound();
        }

        await _contactNumbers.DeleteAsync(contactNumber, ct);
        return Results.NoContent();
    }
}
