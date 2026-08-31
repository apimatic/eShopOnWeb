using System.Linq;
using System.Threading;
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
/// Registers a mobile number for the signed-in shopper. The number is validated
/// by the messaging provider first; what is stored is the provider's canonical
/// form, not what the caller typed.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, HttpContext>
{
    private readonly ISmsProvider _smsProvider;
    private readonly IRepository<ContactNumber> _contactNumbers;

    public CreateContactNumberEndpoint(ISmsProvider smsProvider, IRepository<ContactNumber> contactNumbers)
    {
        _smsProvider = smsProvider;
        _contactNumbers = contactNumbers;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateContactNumberRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "A phone number is required." });
        }

        PhoneNumberValidationResult validation;
        try
        {
            validation = await _smsProvider.ValidatePhoneNumberAsync(request.PhoneNumber, httpContext.RequestAborted);
        }
        catch (SmsProviderException ex)
        {
            return ProviderErrorResults.Map(ex);
        }

        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            return Results.BadRequest(new
            {
                message = "The number is not a usable destination for text messages.",
                validationErrors = validation.ValidationErrors
            });
        }

        var existing = await _contactNumbers.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), httpContext.RequestAborted);
        if (existing.Any(c => c.PhoneNumber == validation.CanonicalNumber))
        {
            throw new DuplicateException("This number is already registered.");
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber);
        await _contactNumbers.AddAsync(contactNumber, httpContext.RequestAborted);

        var response = new CreateContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumber.Id,
            PhoneNumber = contactNumber.PhoneNumber
        };
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
