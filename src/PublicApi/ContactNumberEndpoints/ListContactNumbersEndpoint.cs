using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Lists the signed-in shopper's registered contact numbers.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListContactNumbersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListContactNumbersResponse>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public ListContactNumbersEndpoint(IRepository<ContactNumber> contactNumberRepository)
    {
        _contactNumberRepository = contactNumberRepository;
    }

    [HttpGet("api/contact-numbers")]
    [SwaggerOperation(
        Summary = "Lists the caller's contact numbers",
        Description = "Lists the caller's contact numbers",
        OperationId = "contactNumbers.list",
        Tags = new[] { "ContactNumberEndpoints" })
    ]
    public override async Task<ActionResult<ListContactNumbersResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var numbers = await _contactNumberRepository.ListAsync(
            new ContactNumbersByOwnerSpecification(User.Identity!.Name!), cancellationToken);

        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers.Select(n => new ContactNumberDto
            {
                ContactNumberId = n.Id,
                PhoneNumber = n.PhoneNumber,
                CreatedAt = n.CreatedAt
            }).ToList()
        };
        return response;
    }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
