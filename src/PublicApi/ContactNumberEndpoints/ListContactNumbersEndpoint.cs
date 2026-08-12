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
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

/// <summary>Lists the signed-in shopper's own registered contact numbers.</summary>
public class ListContactNumbersEndpoint : IEndpoint<IResult>
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IHttpContextAccessor _http;

    public ListContactNumbersEndpoint(IRepository<ContactNumber> contactNumbers, IHttpContextAccessor http)
    {
        _contactNumbers = contactNumbers;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            () => await HandleAsync())
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync()
    {
        var ct = _http.HttpContext!.RequestAborted;
        var ownerId = NotificationPresentation.CallerId(_http.HttpContext!.User);

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber
            }).ToList()
        };
        return Results.Ok(response);
    }
}
