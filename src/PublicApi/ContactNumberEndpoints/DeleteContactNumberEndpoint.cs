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
/// Removes one of the signed-in shopper's registered numbers.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ClaimsPrincipal, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), user, contactNumberRepository);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository)
    {
        var response = new DeleteContactNumberResponse(request.CorrelationId());
        var buyerId = user.Identity!.Name!;

        var contactNumber = await contactNumberRepository.GetByIdAsync(request.ContactNumberId);

        // A shopper must never see or delete another shopper's number.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await contactNumberRepository.DeleteAsync(contactNumber);
        return Results.Ok(response);
    }
}
