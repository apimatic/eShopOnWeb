using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Lists the signed-in shopper's registered contact numbers.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult, ListContactNumbersRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public ListContactNumbersEndpoint(IRepository<ContactNumber> contactNumberRepository)
    {
        _contactNumberRepository = contactNumberRepository;
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
        var ownerId = user.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var numbers = await _contactNumberRepository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));

        var response = new ListContactNumbersResponse(request.CorrelationId());
        response.ContactNumbers = numbers.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            IsVerified = c.IsVerified,
            CreatedAt = c.CreatedAt
        }).ToList();

        return Results.Ok(response);
    }
}

public class ListContactNumbersRequest : BaseRequest
{
}

public class ListContactNumbersResponse : BaseResponse
{
    public ListContactNumbersResponse(Guid correlationId) : base(correlationId) {}
    public ListContactNumbersResponse() {}

    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public bool IsVerified { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
