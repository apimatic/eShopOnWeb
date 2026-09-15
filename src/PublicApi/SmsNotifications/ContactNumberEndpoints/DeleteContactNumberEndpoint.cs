using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications.ContactNumberEndpoints;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} &ndash; remove one of the caller's numbers. Afterwards
/// it no longer appears among the caller's numbers and nothing is ever sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, string, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService contactNumberService) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                return await HandleAsync(contactNumberId, buyerId, contactNumberService);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, string buyerId, IContactNumberService contactNumberService)
    {
        var removed = await contactNumberService.DeleteAsync(buyerId, contactNumberId);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
