using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest
{
    public int ContactNumberId { get; set; }
    public string CallerId { get; set; } = string.Empty;
    public CancellationToken Ct { get; set; }
}

/// <summary>
/// Remove one of the caller's numbers. Afterwards it no longer appears among the caller's numbers
/// and nothing is ever sent to it again. A number owned by another shopper is treated as not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (callerId is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    CallerId = callerId,
                    Ct = ct
                }, service);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        var removed = await service.DeleteAsync(request.CallerId, request.ContactNumberId, request.Ct);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
