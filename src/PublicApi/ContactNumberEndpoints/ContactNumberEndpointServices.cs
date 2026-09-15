using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>The scoped services the contact-number endpoints need, aggregated for injection.</summary>
public sealed class ContactNumberEndpointServices
{
    public ContactNumberEndpointServices(
        IHttpContextAccessor httpContextAccessor,
        IPhoneNumberValidator phoneNumberValidator,
        IRepository<ContactNumber> contactNumbers)
    {
        HttpContextAccessor = httpContextAccessor;
        PhoneNumberValidator = phoneNumberValidator;
        ContactNumbers = contactNumbers;
    }

    public IHttpContextAccessor HttpContextAccessor { get; }
    public IPhoneNumberValidator PhoneNumberValidator { get; }
    public IRepository<ContactNumber> ContactNumbers { get; }

    public ClaimsPrincipal? User => HttpContextAccessor.HttpContext?.User;
}
