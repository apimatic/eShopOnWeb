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

public class DeleteContactNumberRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public int ContactNumberId { get; set; }
}

/// <summary>
/// Removes one of the caller's numbers. Afterwards it no longer appears among the caller's numbers and
/// nothing may be sent to it again. A number that is not the caller's is treated as not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, ISmsNotificationService service) =>
                await HandleAsync(new DeleteContactNumberRequest { BuyerId = user.GetBuyerId(), ContactNumberId = contactNumberId }, service))
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ISmsNotificationService service)
    {
        var removed = await service.DeleteContactNumberAsync(request.BuyerId, request.ContactNumberId);
        return removed ? Results.NoContent() : Results.NotFound();
    }
}
