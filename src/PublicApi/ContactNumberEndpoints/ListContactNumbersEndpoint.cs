using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Lists the caller's registered contact numbers.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListContactNumbersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<List<ContactNumberDto>>
{
    private readonly IRepository<ContactNumber> _contactNumbers;

    public ListContactNumbersEndpoint(IRepository<ContactNumber> contactNumbers)
    {
        _contactNumbers = contactNumbers;
    }

    [HttpGet("api/contact-numbers")]
    [SwaggerOperation(Summary = "Lists the caller's contact numbers", Tags = new[] { "ContactNumberEndpoints" })]
    public override async Task<ActionResult<List<ContactNumberDto>>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var buyerId = User.GetBuyerId();
        if (buyerId is null) return Unauthorized();

        var numbers = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        return numbers.Select(c => new ContactNumberDto
        {
            ContactNumberId = c.Id,
            PhoneNumber = c.PhoneNumber,
            CreatedAt = c.CreatedAt
        }).ToList();
    }
}
