using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) {}
    public CreateContactNumberResponse() {}

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedOn { get; set; }
}

/// <summary>
/// Registers a mobile contact number for the signed-in shopper. The number is
/// validated with the messaging provider and stored in the provider's canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public CreateContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository,
        IPhoneNumberValidator phoneNumberValidator)
    {
        _contactNumberRepository = contactNumberRepository;
        _phoneNumberValidator = phoneNumberValidator;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new CreateContactNumberResponse(request.CorrelationId()));
        }

        PhoneNumberValidationResult validation;
        try
        {
            validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber);
        }
        catch (Exception)
        {
            return Results.Json(new { message = "The phone number could not be validated with the messaging provider." }, statusCode: 502);
        }

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                message = "The phone number is not a usable destination.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existing = await _contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId));
        var duplicate = existing.FirstOrDefault(c => c.PhoneNumber == validation.CanonicalNumber);
        if (duplicate != null)
        {
            return Results.Ok(new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = duplicate.Id,
                PhoneNumber = duplicate.PhoneNumber,
                CreatedOn = duplicate.CreatedOn
            });
        }

        var contactNumber = await _contactNumberRepository.AddAsync(new ContactNumber(buyerId, validation.CanonicalNumber));

        return Results.Created($"api/contact-numbers/{contactNumber.Id}", new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedOn = contactNumber.CreatedOn
        });
    }
}
