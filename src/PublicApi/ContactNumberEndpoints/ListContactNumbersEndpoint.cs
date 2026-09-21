using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            // ClaimsPrincipal param keeps this off the RequestDelegate overload (identity is read from HttpContext.User).
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, System.Security.Claims.ClaimsPrincipal user) => await HandleAsync(http))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        var ownerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();

        var repository = http.RequestServices.GetRequiredService<IRepository<ContactNumber>>();
        var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), http.RequestAborted);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(c => new ContactNumberDto { ContactNumberId = c.Id, PhoneNumber = c.E164Number })
                .ToList()
        };
        return Results.Ok(response);
    }
}
