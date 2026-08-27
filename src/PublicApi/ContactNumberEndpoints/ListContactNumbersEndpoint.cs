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
/// Lists the signed-in shopper's registered mobile numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IContactNumberService contactNumberService) =>
            {
                return await HandleAsync(new ListContactNumbersRequest { OwnerId = user.Identity!.Name! }, contactNumberService);
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ListContactNumbersRequest request, IContactNumberService contactNumberService)
    {
        var response = new ListContactNumbersResponse(request.CorrelationId());

        var contactNumbers = await contactNumberService.ListAsync(request.OwnerId);
        response.ContactNumbers.AddRange(contactNumbers.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            CreatedAt = c.CreatedAt
        }));

        return Results.Ok(response);
    }
}
