using System.Linq;
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
/// GET /api/contact-numbers — the caller's own registered numbers. One shopper never sees another's.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(IContactNumberService service)
    {
        var ownerId = EndpointCaller.UserName(_httpContextAccessor);
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await service.ListAsync(ownerId, EndpointCaller.RequestAborted(_httpContextAccessor));
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto
                {
                    ContactNumberId = n.Id,
                    E164Number = n.E164Number,
                    CreatedDate = n.CreatedDate
                })
                .ToList()
        };
        return Results.Ok(response);
    }
}
