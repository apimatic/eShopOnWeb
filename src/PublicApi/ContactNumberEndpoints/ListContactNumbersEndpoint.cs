using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the caller's own registered numbers, and no one else's.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (System.Security.Claims.ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
            {
                var owner = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(owner))
                {
                    return Results.Unauthorized();
                }

                var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(owner));
                var response = new ListContactNumbersResponse
                {
                    ContactNumbers = numbers.Select(n => new ContactNumberDto
                    {
                        ContactNumberId = n.Id,
                        PhoneNumber = n.PhoneNumber,
                        RegisteredAt = n.RegisteredAt
                    }).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    // Satisfies the endpoint contract; the work is done inline above with per-request dependencies.
    public Task<IResult> HandleAsync(IRepository<ContactNumber> repository) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
