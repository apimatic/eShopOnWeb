using System.Linq;
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
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any form the provider can parse (E.164 recommended).</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The provider validates the number first: one it
/// does not consider a usable destination is rejected here, and what gets stored is the provider's own
/// canonical form — not whatever the caller typed.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest>
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly ISmsNotificationProvider _provider;
    private readonly IHttpContextAccessor _http;

    public RegisterContactNumberEndpoint(
        IRepository<ContactNumber> contactNumbers,
        ISmsNotificationProvider provider,
        IHttpContextAccessor http)
    {
        _contactNumbers = contactNumbers;
        _provider = provider;
        _http = http;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request) => await HandleAsync(request))
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request)
    {
        var ct = _http.HttpContext!.RequestAborted;
        var ownerId = NotificationPresentation.CallerId(_http.HttpContext!.User);

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        var validation = await _provider.ValidateNumberAsync(request.PhoneNumber.Trim(), ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new { message = "The provider does not consider this a usable destination number." });
        }

        // Do not register the same canonical number twice for one shopper — that would double every message.
        var existing = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (already != null)
        {
            return Results.Ok(new RegisterContactNumberResponse
            {
                ContactNumberId = already.Id,
                PhoneNumber = already.PhoneNumber
            });
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        await _contactNumbers.AddAsync(contactNumber, ct);

        var response = new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
