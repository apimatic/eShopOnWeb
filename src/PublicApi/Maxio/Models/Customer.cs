using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// Maxio Advanced Billing Customer, per the OpenAPI spec schema <c>Customer</c>.
/// </summary>
public class Customer
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? CcEmails { get; set; }
    public string? Organization { get; set; }
    public string? Reference { get; set; }
    public int Id { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Address { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? StateName { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? CountryName { get; set; }
    public string? Phone { get; set; }
    public bool? Verified { get; set; }
    public DateTime? PortalCustomerCreatedAt { get; set; }
    public DateTime? PortalInviteLastSentAt { get; set; }
    public DateTime? PortalInviteLastAcceptedAt { get; set; }
    public bool TaxExempt { get; set; }
    public string? VatNumber { get; set; }
    public int? ParentId { get; set; }
    public string? Locale { get; set; }
    public string? SalesforceId { get; set; }
    public string? TaxExemptReason { get; set; }
    public int? DefaultAutoRenewalProfileId { get; set; }
}
