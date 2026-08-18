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
/// Removes one of the caller's numbers. Owner-scoped: a shopper can only delete their own number,
/// and afterwards nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, string, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service) =>
            {
                return await HandleAsync(contactNumberId, user.Identity!.Name!, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, string buyerId, IContactNumberService service)
    {
        var result = await service.DeleteAsync(buyerId, contactNumberId);
        return result.IsSuccess ? Results.NoContent() : result.ToFailureResult();
    }
}
