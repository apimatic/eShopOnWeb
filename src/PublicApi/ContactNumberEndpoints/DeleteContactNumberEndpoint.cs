using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the caller's registered numbers. Afterwards it no longer appears among the caller's numbers,
/// and nothing is sent to it again. A shopper can only delete their own number.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ClaimsPrincipal>
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
            (int contactNumberId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), user);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.CallerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _repository.GetByIdAsync(request.ContactNumberId);

        // Not found, or not the caller's — either way, don't reveal it exists.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _repository.DeleteAsync(contactNumber);
        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}
