using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public string Status { get; set; } = "Deleted";
}

/// <summary>
/// Removes one of the signed-in shopper's registered mobile numbers.
/// Numbers belonging to someone else are not visible here (404).
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository,
             CancellationToken cancellationToken) =>
            {
                return await HandleAsync(contactNumberId, user, contactNumberRepository, cancellationToken);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user,
        IRepository<ContactNumber> contactNumberRepository, CancellationToken cancellationToken)
    {
        var contactNumber = await contactNumberRepository.GetByIdAsync(contactNumberId, cancellationToken);
        if (contactNumber is null || contactNumber.OwnerId != user.Identity!.Name)
        {
            return Results.NotFound();
        }

        await contactNumberRepository.DeleteAsync(contactNumber, cancellationToken);
        return Results.Ok(new DeleteContactNumberResponse());
    }
}
