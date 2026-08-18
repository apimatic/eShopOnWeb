using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Lists the signed-in shopper's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ClaimsPrincipal, CancellationToken>
{
    private readonly IReadRepository<ContactNumber> _repository;

    public ListContactNumbersEndpoint(IReadRepository<ContactNumber> repository)
    {
        _repository = repository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) => await HandleAsync(user, ct))
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var buyerId = user.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        var numbers = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto { Id = n.Id, PhoneNumber = n.PhoneNumber })
                .ToList()
        };
        return Results.Ok(response);
    }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
