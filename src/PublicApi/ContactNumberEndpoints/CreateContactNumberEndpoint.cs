using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with
/// the provider up front and stored in the provider's canonical (E.164) form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest>
{
    private readonly ITwilioLookupClient _lookupClient;
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public CreateContactNumberEndpoint(ITwilioLookupClient lookupClient, IRepository<ContactNumber> contactNumberRepository)
    {
        _lookupClient = lookupClient;
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, ClaimsPrincipal user) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request);
            })
            .Produces<CreateContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        ApplicationCore.Models.TwilioPhoneNumberLookup lookup;
        try
        {
            lookup = await _lookupClient.LookupPhoneNumberAsync(request.PhoneNumber);
        }
        catch (TwilioApiException)
        {
            return Results.Problem("The phone number validation provider could not be reached.", statusCode: StatusCodes.Status502BadGateway);
        }

        if (!lookup.IsValid || string.IsNullOrWhiteSpace(lookup.CanonicalPhoneNumber))
        {
            return Results.BadRequest(new
            {
                message = "The phone number is not a usable destination.",
                validationErrors = lookup.ValidationErrors
            });
        }

        var existingSpec = new ContactNumberByBuyerAndNumberSpecification(request.BuyerId, lookup.CanonicalPhoneNumber);
        if (await _contactNumberRepository.FirstOrDefaultAsync(existingSpec) is not null)
        {
            throw new DuplicateException("This phone number is already registered.");
        }

        var contactNumber = await _contactNumberRepository.AddAsync(
            new ContactNumber(request.BuyerId, lookup.CanonicalPhoneNumber));

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber,
            CreatedAt = contactNumber.CreatedAt
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
