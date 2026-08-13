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
/// Removes one of the signed-in shopper's numbers. Afterwards it no longer appears among their
/// numbers and nothing is sent to it again. Another shopper's number is simply not found (404).
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeleteContactNumberEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId:int}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, IContactNumberService service) =>
                await HandleAsync(contactNumberId, service))
            .Produces(StatusCodes.Status204NoContent)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, IContactNumberService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        await service.RemoveAsync(buyerId, contactNumberId);
        return Results.NoContent();
    }
}
