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

public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), service, user, http.RequestAborted);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service) =>
        HandleAsync(request, service, new ClaimsPrincipal(), default);

    private async Task<IResult> HandleAsync(
        DeleteContactNumberRequest request,
        IContactNumberService service,
        ClaimsPrincipal user,
        System.Threading.CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        await service.DeleteAsync(buyerId, request.ContactNumberId, cancellationToken);
        return Results.NoContent();
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; init; }

    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }
}
