using System.Linq;
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

/// <summary>
/// Lists the signed-in shopper's registered contact numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, ClaimsPrincipal>
{
    private readonly IContactNumberService _contactNumberService;

    public ListContactNumbersEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user) =>
            {
                return await HandleAsync(new ListContactNumbersRequest(), user);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.FindFirstValue(ClaimTypes.Name);
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var contactNumbers = await _contactNumberService.ListAsync(buyerId);
        var response = new ListContactNumbersResponse(request.CorrelationId())
        {
            ContactNumbers = contactNumbers.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
