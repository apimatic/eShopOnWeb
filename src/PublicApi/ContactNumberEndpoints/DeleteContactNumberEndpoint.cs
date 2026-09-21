using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Afterwards it no longer appears among
/// their numbers and nothing is sent to it again. One shopper can never delete another's number.
/// </summary>
public class DeleteContactNumberEndpoint
    : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null)
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId, buyerId), service, ct);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        var deleted = await service.DeleteAsync(request.BuyerId, request.ContactNumberId, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
