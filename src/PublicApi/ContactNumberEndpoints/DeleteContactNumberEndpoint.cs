using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's registered contact numbers. Nothing may be sent
/// to the number afterwards.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ClaimsPrincipal, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal claimsPrincipal, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), claimsPrincipal, contactNumberService);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal claimsPrincipal, IContactNumberService contactNumberService)
    {
        var buyerId = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        // Returns false both when the number does not exist and when it belongs to another
        // shopper — either way the caller must not be able to act on it.
        var deleted = await contactNumberService.DeleteAsync(buyerId, request.ContactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
