using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.NotificationsFeature;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IRepository<ContactNumber> repository) =>
                await HandleAsync(user, repository))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public static async Task<IResult> HandleAsync(ClaimsPrincipal user, IRepository<ContactNumber> repository)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(buyerId));
        var items = numbers
            .Select(n => new ContactNumberDto(n.Id, n.PhoneNumber, n.RegisteredAt))
            .ToList();

        return Results.Ok(new ListContactNumbersResponse(items));
    }
}

public record ContactNumberDto(int ContactNumberId, string PhoneNumber, System.DateTimeOffset RegisteredAt);

public record ListContactNumbersResponse(List<ContactNumberDto> ContactNumbers);
