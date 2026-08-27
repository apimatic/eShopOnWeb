using System.Security.Claims;
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

/// <summary>
/// Removes one of the signed-in shopper's contact numbers.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository)
    {
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(contactNumberId, user);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _contactNumberRepository.DeleteAsync(contactNumber);
        return Results.NoContent();
    }
}
