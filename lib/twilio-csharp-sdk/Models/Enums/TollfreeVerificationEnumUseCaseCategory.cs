using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<TollfreeVerificationEnumUseCaseCategory>))]
public sealed record TollfreeVerificationEnumUseCaseCategory : StringEnum<TollfreeVerificationEnumUseCaseCategory>
{
    private TollfreeVerificationEnumUseCaseCategory(string value) : base(value)
    {
    }

    public static readonly TollfreeVerificationEnumUseCaseCategory TwoFactorAuthentication = new("TWO_FACTOR_AUTHENTICATION");

    public static readonly TollfreeVerificationEnumUseCaseCategory AccountNotifications = new("ACCOUNT_NOTIFICATIONS");

    public static readonly TollfreeVerificationEnumUseCaseCategory CustomerCare = new("CUSTOMER_CARE");

    public static readonly TollfreeVerificationEnumUseCaseCategory CharityNonprofit = new("CHARITY_NONPROFIT");

    public static readonly TollfreeVerificationEnumUseCaseCategory DeliveryNotifications = new("DELIVERY_NOTIFICATIONS");

    public static readonly TollfreeVerificationEnumUseCaseCategory FraudAlertMessaging = new("FRAUD_ALERT_MESSAGING");

    public static readonly TollfreeVerificationEnumUseCaseCategory Events = new("EVENTS");

    public static readonly TollfreeVerificationEnumUseCaseCategory HigherEducation = new("HIGHER_EDUCATION");

    public static readonly TollfreeVerificationEnumUseCaseCategory K12 = new("K12");

    public static readonly TollfreeVerificationEnumUseCaseCategory Marketing = new("MARKETING");

    public static readonly TollfreeVerificationEnumUseCaseCategory PollingAndVotingNonPolitical = new("POLLING_AND_VOTING_NON_POLITICAL");

    public static readonly TollfreeVerificationEnumUseCaseCategory PoliticalElectionCampaigns = new("POLITICAL_ELECTION_CAMPAIGNS");

    public static readonly TollfreeVerificationEnumUseCaseCategory PublicServiceAnnouncement = new("PUBLIC_SERVICE_ANNOUNCEMENT");

    public static readonly TollfreeVerificationEnumUseCaseCategory SecurityAlert = new("SECURITY_ALERT");

    public static TollfreeVerificationEnumUseCaseCategory FromValue(string value) => FromValueCore(value);
}
