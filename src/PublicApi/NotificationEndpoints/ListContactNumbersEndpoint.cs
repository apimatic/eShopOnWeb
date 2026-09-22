using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.ApiEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Lists the signed-in shopper's registered numbers — only their own.</summary>
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
        Summary = "Lists the signed-in shopper's registered numbers",
        Description = "Lists the signed-in shopper's registered numbers",
        OperationId = "contactNumbers.list",
        Tags = new[] { "ContactNumberEndpoints" })]
    public override async Task<ActionResult<ListContactNumbersResponse>> HandleAsync(CancellationToken cancellationToken = default)
    {
        var ownerId = User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Unauthorized();
        }

        var numbers = await _contactNumberService.ListAsync(ownerId, cancellationToken);
        var response = new ListContactNumbersResponse
        {
            ContactNumbers = numbers
                .Select(n => new ContactNumberDto
                {
                    ContactNumberId = n.Id,
                    PhoneNumber = n.PhoneNumber,
                    RegisteredAt = n.RegisteredAt
                })
                .ToList()
        };
        return Ok(response);
    }
}
