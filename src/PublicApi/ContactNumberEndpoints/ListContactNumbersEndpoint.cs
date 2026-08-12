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
using Microsoft.eShopWeb.PublicApi.SmsNotifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Lists the signed-in shopper's own registered numbers. A shopper never sees another's.
/// </summary>
public class ListContactNumbersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IRepository<ContactNumber> repository) =>
            {
                var ownerId = user.GetOwnerId();
                if (string.IsNullOrEmpty(ownerId))
                {
                    return Results.Unauthorized();
                }

                var numbers = await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId));
                return Results.Ok(new ListContactNumbersResponse
                {
                    ContactNumbers = numbers.Select(c => new ContactNumberDto
                    {
                        ContactNumberId = c.Id,
                        PhoneNumber = c.PhoneNumber,
                        RegisteredAt = c.RegisteredAt
                    }).ToList()
                });
            })
            .Produces<ListContactNumbersResponse>()
            .WithTags("ContactNumberEndpoints");
    }
}

public class ListContactNumbersResponse
{
    public List<ContactNumberDto> ContactNumbers { get; set; } = new();
}

public class ContactNumberDto
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}
