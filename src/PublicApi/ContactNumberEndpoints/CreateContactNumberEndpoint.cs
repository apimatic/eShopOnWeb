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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    /// <summary>Identifier of the created (or already-registered) number — a top-level field so callers can drive the flow.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form of the number that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here (not when a later message fails), and what gets stored is the provider's
/// own canonical form of the number, not whatever the caller typed.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext http)
    {
        var ownerId = http.User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId)) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { message = "A phone number is required." });

        var ct = http.RequestAborted;
        var sms = http.RequestServices.GetRequiredService<ISmsProvider>();
        var repository = http.RequestServices.GetRequiredService<IRepository<ContactNumber>>();

        // Reject an unusable destination now, and store the provider's canonical form.
        var validation = await sms.ValidateNumberAsync(request.PhoneNumber, ct);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalE164))
            return Results.BadRequest(new { message = validation.Reason ?? "The number is not a usable destination." });

        var canonical = validation.CanonicalE164!;

        // If this shopper already registered this number, return the existing record rather than duplicating.
        var existing = (await repository.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct))
            .FirstOrDefault(c => c.E164Number == canonical);
        if (existing is not null)
        {
            return Results.Ok(new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = existing.Id,
                PhoneNumber = existing.E164Number
            });
        }

        var contactNumber = new ContactNumber(ownerId, canonical);
        contactNumber = await repository.AddAsync(contactNumber, ct);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.E164Number
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
