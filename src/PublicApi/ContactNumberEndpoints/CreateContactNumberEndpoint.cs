using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Populated from the JWT, never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse() { }
    public CreateContactNumberResponse(System.Guid correlationId) : base(correlationId) { }

    public int ContactNumberId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is
/// validated with the messaging provider and stored in the provider's
/// canonical form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IRepository<ContactNumber>>
{
    private readonly IPhoneNumberValidator _phoneNumberValidator;

    public CreateContactNumberEndpoint(IPhoneNumberValidator phoneNumberValidator)
    {
        _phoneNumberValidator = phoneNumberValidator;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, System.Security.Claims.ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository) =>
            {
                request.BuyerId = user.Identity!.Name!;
                return await HandleAsync(request, contactNumberRepository);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        // Reject unusable destinations at registration time, not at send time.
        var validation = await _phoneNumberValidator.ValidateAsync(request.PhoneNumber);
        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
        {
            return Results.BadRequest(new
            {
                error = "The phone number is not a usable destination.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(request.BuyerId));
        foreach (var number in existing)
        {
            if (number.PhoneNumber == validation.CanonicalNumber)
            {
                return Results.Ok(new CreateContactNumberResponse(request.CorrelationId())
                {
                    ContactNumberId = number.Id,
                    PhoneNumber = number.PhoneNumber
                });
            }
        }

        var contactNumber = new ContactNumber(request.BuyerId, validation.CanonicalNumber);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
