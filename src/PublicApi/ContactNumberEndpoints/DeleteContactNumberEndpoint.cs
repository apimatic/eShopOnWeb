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
/// Removes one of the caller's own registered numbers. Afterwards it no longer appears among the
/// caller's numbers and nothing is ever sent to it again. A number owned by another shopper is
/// treated as not found, so one shopper can never delete another's.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, HttpContext>
{
    private readonly IRepository<ContactNumber> _repository;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> repository)
    {
        _repository = repository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http) => await HandleAsync(contactNumberId, http))
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext http)
    {
        var buyerId = http.User.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var ct = http.RequestAborted;
        var contactNumber = await _repository.GetByIdAsync(contactNumberId, ct);

        // Not found, or owned by someone else — either way, this caller cannot see or delete it.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
            return Results.NotFound();

        await _repository.DeleteAsync(contactNumber, ct);
        return Results.NoContent();
    }
}
