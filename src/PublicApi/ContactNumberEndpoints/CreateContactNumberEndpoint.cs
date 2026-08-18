using System.Linq;
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
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider
/// first — an unusable destination is rejected here rather than when a later message fails — and it
/// is the provider's canonical form that is stored, not whatever the caller typed.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, HttpContext>
{
    private readonly ISmsGateway _smsGateway;
    private readonly IRepository<ContactNumber> _repository;

    public CreateContactNumberEndpoint(ISmsGateway smsGateway, IRepository<ContactNumber> repository)
    {
        _smsGateway = smsGateway;
        _repository = repository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext http) => await HandleAsync(request, http))
            .Produces<CreateContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext http)
    {
        var buyerId = http.User.GetUserName();
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest("A phone number is required.");

        var ct = http.RequestAborted;

        PhoneValidationResult validation;
        try
        {
            validation = await _smsGateway.ValidateNumberAsync(request.PhoneNumber, ct);
        }
        catch (SmsGatewayException)
        {
            // Could not reach the provider to validate; do not store an unvalidated number.
            return Results.Problem("The messaging provider could not be reached to validate the number.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (!validation.IsValid || string.IsNullOrEmpty(validation.CanonicalNumber))
            return Results.BadRequest($"The number is not a usable destination. {validation.Reason}".Trim());

        var canonical = validation.CanonicalNumber;

        // Deduplicate: the same canonical number for the same shopper resolves to the existing record.
        var existing = await _repository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), ct);
        var already = existing.FirstOrDefault(c => c.PhoneNumber == canonical);
        if (already is not null)
        {
            var dup = new CreateContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = already.Id,
                PhoneNumber = already.PhoneNumber
            };
            return Results.Ok(dup);
        }

        var contactNumber = new ContactNumber(buyerId, canonical);
        contactNumber = await _repository.AddAsync(contactNumber, ct);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
