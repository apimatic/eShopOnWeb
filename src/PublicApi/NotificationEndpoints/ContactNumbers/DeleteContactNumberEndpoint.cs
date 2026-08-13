using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints.ContactNumbers;

/// <summary>
/// Removes one of the signed-in shopper's numbers. Scoped to the caller, so one shopper can never
/// delete another's. Afterwards nothing may be sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);
                return await HandleAsync(new DeleteContactNumberRequest(buyerId, contactNumberId), service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        var removed = await service.RemoveAsync(request.BuyerId, request.ContactNumberId);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}

public record DeleteContactNumberRequest(string BuyerId, int ContactNumberId);
