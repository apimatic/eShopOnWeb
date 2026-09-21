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

/// <summary>Lists the signed-in shopper's registered numbers. One shopper never sees another's.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IContactNumberService service, ClaimsPrincipal user, HttpContext http) =>
            {
                var callerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(callerId))
                {
                    return Results.Unauthorized();
                }

                var numbers = await service.ListAsync(callerId, http.RequestAborted);
                return Results.Ok(new ListContactNumbersResponse
                {
                    ContactNumbers = numbers
                        .Select(n => new ContactNumberDto { ContactNumberId = n.ContactNumberId, E164Number = n.E164Number })
                        .ToList()
                });
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(IContactNumberService service) => Task.FromResult(Results.Empty as IResult);
}
