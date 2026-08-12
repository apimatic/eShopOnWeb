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
    public int ContactNumberId { get; set; }
    public string? BuyerId { get; set; }
}

/// <summary>
/// Removes one of the caller's numbers. Afterwards it no longer appears among the caller's numbers and
/// nothing is sent to it again. A number that is not the caller's yields Not Found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service, ClaimsPrincipal user) =>
            {
                var request = new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = CallerIdentity.GetUserName(user)
                };
                return await HandleAsync(request, service);
            })
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var deleted = await service.DeleteAsync(request.BuyerId, request.ContactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
