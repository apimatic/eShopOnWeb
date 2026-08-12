using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// DELETE /api/contact-numbers/{contactNumberId} — removes one of the caller's own numbers.
/// Afterwards it no longer appears among the caller's numbers and nothing is sent to it again.
/// A number owned by another shopper is treated as not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                int contactNumberId,
                ClaimsPrincipal user,
                IRepository<ContactNumber> repository,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();
                if (string.IsNullOrWhiteSpace(buyerId))
                    return Results.Unauthorized();

                var contactNumber = await repository.GetByIdAsync(contactNumberId, cancellationToken);

                // Scope strictly to the caller: another shopper's number is indistinguishable from absent.
                if (contactNumber is null || contactNumber.BuyerId != buyerId)
                    return Results.NotFound();

                await repository.DeleteAsync(contactNumber, cancellationToken);
                return Results.NoContent();
            })
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }
}
