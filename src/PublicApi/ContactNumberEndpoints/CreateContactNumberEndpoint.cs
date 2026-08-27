using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class CreateContactNumberRequest : BaseRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
}

public class CreateContactNumberResponse : BaseResponse
{
    public CreateContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public CreateContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NationalFormat { get; set; }
    public string[] ValidationErrors { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the
/// provider up front and stored in the provider's canonical (E.164) form.
/// </summary>
public class CreateContactNumberEndpoint : IEndpoint<IResult, CreateContactNumberRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        // Services are lambda parameters so they are resolved per request; constructor
        // injection would capture scoped services (repositories/DbContext) for the app's lifetime.
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository,
                ISmsGateway smsGateway, HttpContext httpContext, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, contactNumberRepository, smsGateway, httpContext, cancellationToken);
            })
            .Produces<CreateContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository)
        => throw new NotSupportedException("Use the routed overload with HttpContext.");

    private async Task<IResult> HandleAsync(CreateContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository,
        ISmsGateway smsGateway, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var response = new CreateContactNumberResponse(request.CorrelationId());

        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            response.ValidationErrors = new[] { "A phone number is required." };
            return Results.BadRequest(response);
        }

        var validation = await smsGateway.ValidatePhoneNumberAsync(request.PhoneNumber, cancellationToken);
        if (!validation.IsValid || validation.CanonicalNumber is null)
        {
            response.ValidationErrors = validation.ValidationErrors.Count > 0
                ? validation.ValidationErrors.ToArray()
                : new[] { "The messaging provider does not consider this a usable destination." };
            return Results.BadRequest(response);
        }

        var existing = await contactNumberRepository.ListAsync(new ContactNumbersByBuyerSpecification(buyerId), cancellationToken);
        if (existing.Any(c => c.PhoneNumber == validation.CanonicalNumber))
        {
            var duplicate = existing.First(c => c.PhoneNumber == validation.CanonicalNumber);
            response.ContactNumberId = duplicate.Id;
            response.PhoneNumber = duplicate.PhoneNumber;
            response.NationalFormat = duplicate.NationalFormat;
            return Results.Ok(response);
        }

        var contactNumber = new ContactNumber(buyerId, validation.CanonicalNumber, validation.NationalFormat);
        contactNumber = await contactNumberRepository.AddAsync(contactNumber, cancellationToken);

        response.ContactNumberId = contactNumber.Id;
        response.PhoneNumber = contactNumber.PhoneNumber;
        response.NationalFormat = contactNumber.NationalFormat;
        return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
    }
}
