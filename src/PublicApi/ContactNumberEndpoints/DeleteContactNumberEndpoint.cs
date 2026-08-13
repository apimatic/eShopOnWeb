using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's own contact numbers. A number belonging to another shopper is
/// treated as not found. Afterwards the number no longer appears among the caller's numbers and
/// nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var buyerId = user.GetUserName();
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                return await HandleAsync(new DeleteContactNumberRequest { ContactNumberId = contactNumberId, BuyerId = buyerId }, repository);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> repository)
    {
        var contactNumber = await repository.GetByIdAsync(request.ContactNumberId);

        // Not found, or owned by another shopper — do not reveal the difference.
        if (contactNumber is null || contactNumber.BuyerId != request.BuyerId)
            return Results.NotFound();

        await repository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }
    internal string? BuyerId { get; set; }
}
