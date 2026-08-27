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
/// Removes one of the signed-in shopper's contact numbers. Once removed, nothing
/// is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, int>
{
    private readonly IContactNumberService _contactNumberService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DeleteContactNumberEndpoint(IContactNumberService contactNumberService, IHttpContextAccessor httpContextAccessor)
    {
        _contactNumberService = contactNumberService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId) =>
            {
                return await HandleAsync(contactNumberId);
            })
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var deleted = await _contactNumberService.DeleteAsync(buyerId, contactNumberId);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
