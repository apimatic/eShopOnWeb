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
/// Lists the signed-in shopper's registered contact numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult>
{
    private readonly IContactNumberService _contactNumberService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListContactNumbersEndpoint(IContactNumberService contactNumberService, IHttpContextAccessor httpContextAccessor)
    {
        _contactNumberService = contactNumberService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync();
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumbers = await _contactNumberService.ListAsync(buyerId);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = contactNumbers.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber,
                NationalFormat = c.NationalFormat,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
        return Results.Ok(response);
    }
}
