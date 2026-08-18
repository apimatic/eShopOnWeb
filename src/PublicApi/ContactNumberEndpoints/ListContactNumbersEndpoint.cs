using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(System.Guid correlationId) : base(correlationId) { }

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>GET /api/contact-numbers — the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, HttpContext http) => await HandleAsync(http))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var buyerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var service = http.RequestServices.GetRequiredService<IContactNumberService>();
        var numbers = await service.ListAsync(buyerId, http.RequestAborted);

        var response = new ListContactNumbersResponse(System.Guid.NewGuid())
        {
            ContactNumbers = numbers.Select(ContactNumberDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
