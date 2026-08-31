using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, CancellationToken>
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly TwilioMessagingService _messaging;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumbers,
        TwilioMessagingService messaging, IHttpContextAccessor httpContextAccessor)
    {
        _contactNumbers = contactNumbers;
        _messaging = messaging;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, CancellationToken ct) =>
            {
                return await HandleAsync(request, ct);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, CancellationToken ct)
    {
        var ownerId = _httpContextAccessor.HttpContext?.User.Identity?.Name;
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        NumberValidationResult validation;
        try
        {
            validation = await _messaging.ValidateNumberAsync(request.PhoneNumber.Trim(), ct);
        }
        catch (MessagingException)
        {
            return Results.Problem("The phone number could not be validated right now.", statusCode: 502);
        }

        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            return Results.BadRequest(new
            {
                message = "The phone number is not a usable destination.",
                errors = validation.ValidationErrors
            });
        }

        var existing = await _contactNumbers.ListAsync(new ContactNumbersByOwnerSpecification(ownerId), ct);
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate is not null)
        {
            return Results.Ok(new CreateContactNumberResponse
            {
                ContactNumberId = duplicate.Id,
                PhoneNumber = duplicate.PhoneNumber,
                CreatedAt = duplicate.CreatedAt
            });
        }

        var contactNumber = new ContactNumber(ownerId, validation.CanonicalNumber);
        await _contactNumbers.AddAsync(contactNumber, ct);

        var response = new CreateContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
