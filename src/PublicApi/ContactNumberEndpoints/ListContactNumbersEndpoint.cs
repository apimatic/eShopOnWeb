using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Lists the caller's registered contact numbers.
/// </summary>
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class ListContactNumbersEndpoint : EndpointBaseAsync
    .WithoutRequest
    .WithActionResult<ListContactNumbersResponse>
{
    private readonly IContactNumberService _contactNumberService;

    public ListContactNumbersEndpoint(IContactNumberService contactNumberService)
    {
        _contactNumberService = contactNumberService;
    }

    [HttpGet("api/contact-numbers")]
    [SwaggerOperation(
        Summary = "Lists the caller's contact numbers",
        Description = "Lists the caller's contact numbers",
        OperationId = "contact-numbers.list",
        Tags = new[] { "ContactNumberEndpoints" })
    ]
    public override async Task<ActionResult<ListContactNumbersResponse>> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var buyerId = User.Identity!.Name!;
        var contactNumbers = await _contactNumberService.ListAsync(buyerId, cancellationToken);

        return new ListContactNumbersResponse
        {
            ContactNumbers = contactNumbers.Select(c => new ContactNumberDto
            {
                ContactNumberId = c.Id,
                PhoneNumber = c.PhoneNumber,
                CreatedAt = c.CreatedAt
            }).ToList()
        };
    }
}

public class ListContactNumbersResponse : BaseResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}
