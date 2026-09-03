using System.Security.Claims;
using System.Threading;
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
/// Removes one of the caller's registered numbers. Afterwards it no longer appears among the caller's
/// numbers and nothing is ever sent to it again. A shopper can only delete their own.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var ownerId = user.ShopperId();
                if (string.IsNullOrEmpty(ownerId))
                    return Results.Unauthorized();

                return await ExecuteAsync(new DeleteContactNumberRequest { OwnerId = ownerId, ContactNumberId = contactNumberId }, service, ct);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IOrderNotificationService service)
        => ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(DeleteContactNumberRequest request, IOrderNotificationService service, CancellationToken ct)
    {
        var removed = await service.DeleteNumberAsync(request.OwnerId, request.ContactNumberId, ct);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
