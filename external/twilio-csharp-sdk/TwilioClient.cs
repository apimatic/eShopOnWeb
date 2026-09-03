using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Twilio.Api;
using Twilio.Core;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Core.Logging;
using Twilio.Core.Models;
using Twilio.Core.Request;
using Twilio.Core.Response;
using Twilio.Errors;
using Twilio.Models;

namespace Twilio;

/// <summary>
/// This is the public Twilio REST API., Manage configurations, conversations, participants, and communications. Create configurations to define capture rules and channel settings, then use conversations to group related communications., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the reference API for the rest-proxy server., Insights Domain V3 API.
/// </summary>
public sealed class TwilioClient
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    public TwilioClient(HttpClient httpClient, TwilioClientOptions options)
    {
        _server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "TwilioClient/1.0.0 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "1.0.0"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "TwilioClient");
        _rawClient =
            new RawClient(httpClient,
                urlFactory,
                httpStatusPolicy,
                headersFactory,
                resiliencePipelineFactory,
                httpLogger,
                options.Hooks);
        _auth = new AuthSchemes(options);
        Api20100401Account = new Api20100401Account(_rawClient, _server, _auth);
        Api20100401AddOnResult = new Api20100401AddOnResult(_rawClient, _server, _auth);
        Api20100401Address = new Api20100401Address(_rawClient, _server, _auth);
        Api20100401AllTime = new Api20100401AllTime(_rawClient, _server, _auth);
        Api20100401Application = new Api20100401Application(_rawClient, _server, _auth);
        Api20100401AssignedAddOn = new Api20100401AssignedAddOn(_rawClient, _server, _auth);
        Api20100401AssignedAddOnExtension = new Api20100401AssignedAddOnExtension(_rawClient, _server, _auth);
        Api20100401AuthCallsCredentialListMapping =
            new Api20100401AuthCallsCredentialListMapping(_rawClient, _server, _auth);
        Api20100401AuthCallsIpAccessControlListMapping =
            new Api20100401AuthCallsIpAccessControlListMapping(_rawClient, _server, _auth);
        Api20100401AuthRegistrationsCredentialListMapping =
            new Api20100401AuthRegistrationsCredentialListMapping(_rawClient, _server, _auth);
        Api20100401AuthorizedConnectApp = new Api20100401AuthorizedConnectApp(_rawClient, _server, _auth);
        Api20100401AvailablePhoneNumberCountry = new Api20100401AvailablePhoneNumberCountry(_rawClient, _server, _auth);
        Api20100401Balance = new Api20100401Balance(_rawClient, _server, _auth);
        Api20100401Call = new Api20100401Call(_rawClient, _server, _auth);
        Api20100401CallNotification = new Api20100401CallNotification(_rawClient, _server, _auth);
        Api20100401CallRecording = new Api20100401CallRecording(_rawClient, _server, _auth);
        Api20100401CallTranscription = new Api20100401CallTranscription(_rawClient, _server, _auth);
        Api20100401Conference = new Api20100401Conference(_rawClient, _server, _auth);
        Api20100401ConferenceRecording = new Api20100401ConferenceRecording(_rawClient, _server, _auth);
        Api20100401ConnectApp = new Api20100401ConnectApp(_rawClient, _server, _auth);
        Api20100401Credential = new Api20100401Credential(_rawClient, _server, _auth);
        Api20100401CredentialList = new Api20100401CredentialList(_rawClient, _server, _auth);
        Api20100401CredentialListMapping = new Api20100401CredentialListMapping(_rawClient, _server, _auth);
        Api20100401Daily = new Api20100401Daily(_rawClient, _server, _auth);
        Api20100401Data = new Api20100401Data(_rawClient, _server, _auth);
        Api20100401DependentPhoneNumber = new Api20100401DependentPhoneNumber(_rawClient, _server, _auth);
        Api20100401Domain = new Api20100401Domain(_rawClient, _server, _auth);
        Api20100401Event = new Api20100401Event(_rawClient, _server, _auth);
        Api20100401Feedback = new Api20100401Feedback(_rawClient, _server, _auth);
        Api20100401IncomingPhoneNumber = new Api20100401IncomingPhoneNumber(_rawClient, _server, _auth);
        Api20100401IncomingPhoneNumberLocal = new Api20100401IncomingPhoneNumberLocal(_rawClient, _server, _auth);
        Api20100401IncomingPhoneNumberMobile = new Api20100401IncomingPhoneNumberMobile(_rawClient, _server, _auth);
        Api20100401IncomingPhoneNumberTollFree = new Api20100401IncomingPhoneNumberTollFree(_rawClient, _server, _auth);
        Api20100401IpAccessControlList = new Api20100401IpAccessControlList(_rawClient, _server, _auth);
        Api20100401IpAccessControlListMapping = new Api20100401IpAccessControlListMapping(_rawClient, _server, _auth);
        Api20100401Key = new Api20100401Key(_rawClient, _server, _auth);
        Api20100401LastMonth = new Api20100401LastMonth(_rawClient, _server, _auth);
        Api20100401Local = new Api20100401Local(_rawClient, _server, _auth);
        Api20100401MachineToMachine = new Api20100401MachineToMachine(_rawClient, _server, _auth);
        Api20100401Media = new Api20100401Media(_rawClient, _server, _auth);
        Api20100401MediaInstance = new Api20100401MediaInstance(_rawClient, _server, _auth);
        Api20100401Member = new Api20100401Member(_rawClient, _server, _auth);
        Api20100401Message = new Api20100401Message(_rawClient, _server, _auth);
        Api20100401Mobile = new Api20100401Mobile(_rawClient, _server, _auth);
        Api20100401Monthly = new Api20100401Monthly(_rawClient, _server, _auth);
        Api20100401National = new Api20100401National(_rawClient, _server, _auth);
        Api20100401NewKey = new Api20100401NewKey(_rawClient, _server, _auth);
        Api20100401NewSigningKey = new Api20100401NewSigningKey(_rawClient, _server, _auth);
        Api20100401Notification = new Api20100401Notification(_rawClient, _server, _auth);
        Api20100401OutgoingCallerId = new Api20100401OutgoingCallerId(_rawClient, _server, _auth);
        Api20100401Participant = new Api20100401Participant(_rawClient, _server, _auth);
        Api20100401Payload = new Api20100401Payload(_rawClient, _server, _auth);
        Api20100401Payment = new Api20100401Payment(_rawClient, _server, _auth);
        Api20100401Queue = new Api20100401Queue(_rawClient, _server, _auth);
        Api20100401Record = new Api20100401Record(_rawClient, _server, _auth);
        Api20100401Recording = new Api20100401Recording(_rawClient, _server, _auth);
        Api20100401RecordingTranscription = new Api20100401RecordingTranscription(_rawClient, _server, _auth);
        Api20100401SharedCost = new Api20100401SharedCost(_rawClient, _server, _auth);
        Api20100401ShortCode = new Api20100401ShortCode(_rawClient, _server, _auth);
        Api20100401SigningKey = new Api20100401SigningKey(_rawClient, _server, _auth);
        Api20100401SipIpAddress = new Api20100401SipIpAddress(_rawClient, _server, _auth);
        Api20100401Siprec = new Api20100401Siprec(_rawClient, _server, _auth);
        Api20100401Stream = new Api20100401Stream(_rawClient, _server, _auth);
        Api20100401ThisMonth = new Api20100401ThisMonth(_rawClient, _server, _auth);
        Api20100401Today = new Api20100401Today(_rawClient, _server, _auth);
        Api20100401Token = new Api20100401Token(_rawClient, _server, _auth);
        Api20100401TollFree = new Api20100401TollFree(_rawClient, _server, _auth);
        Api20100401Transcription = new Api20100401Transcription(_rawClient, _server, _auth);
        Api20100401Trigger = new Api20100401Trigger(_rawClient, _server, _auth);
        Api20100401UserDefinedMessage = new Api20100401UserDefinedMessage(_rawClient, _server, _auth);
        Api20100401UserDefinedMessageSubscription =
            new Api20100401UserDefinedMessageSubscription(_rawClient, _server, _auth);
        Api20100401ValidationRequest = new Api20100401ValidationRequest(_rawClient, _server, _auth);
        Api20100401Voip = new Api20100401Voip(_rawClient, _server, _auth);
        Api20100401Yearly = new Api20100401Yearly(_rawClient, _server, _auth);
        Api20100401Yesterday = new Api20100401Yesterday(_rawClient, _server, _auth);
        ContentV2Content = new ContentV2Content(_rawClient, _server, _auth);
        ContentV2ContentAndApprovals = new ContentV2ContentAndApprovals(_rawClient, _server, _auth);
        Contentv1ApprovalCreate = new Contentv1ApprovalCreate(_rawClient, _server, _auth);
        Contentv1ApprovalFetch = new Contentv1ApprovalFetch(_rawClient, _server, _auth);
        Contentv1ContentApi = new Contentv1ContentApi(_rawClient, _server, _auth);
        Contentv1ContentAndApprovalsApi = new Contentv1ContentAndApprovalsApi(_rawClient, _server, _auth);
        Contentv1LegacyContentApi = new Contentv1LegacyContentApi(_rawClient, _server, _auth);
        ConversationsV1AddressConfiguration = new ConversationsV1AddressConfiguration(_rawClient, _server, _auth);
        ConversationsV1Binding = new ConversationsV1Binding(_rawClient, _server, _auth);
        ConversationsV1ConfigurationApi = new ConversationsV1ConfigurationApi(_rawClient, _server, _auth);
        ConversationsV1ConversationApi = new ConversationsV1ConversationApi(_rawClient, _server, _auth);
        ConversationsV1ConversationWithParticipantsApi =
            new ConversationsV1ConversationWithParticipantsApi(_rawClient, _server, _auth);
        ConversationsV1CredentialApi = new ConversationsV1CredentialApi(_rawClient, _server, _auth);
        ConversationsV1DeliveryReceipt = new ConversationsV1DeliveryReceipt(_rawClient, _server, _auth);
        ConversationsV1Message = new ConversationsV1Message(_rawClient, _server, _auth);
        ConversationsV1Notification = new ConversationsV1Notification(_rawClient, _server, _auth);
        ConversationsV1Participant = new ConversationsV1Participant(_rawClient, _server, _auth);
        ConversationsV1ParticipantConversationApi =
            new ConversationsV1ParticipantConversationApi(_rawClient, _server, _auth);
        ConversationsV1RoleApi = new ConversationsV1RoleApi(_rawClient, _server, _auth);
        ConversationsV1ServiceApi = new ConversationsV1ServiceApi(_rawClient, _server, _auth);
        ConversationsV1UserApi = new ConversationsV1UserApi(_rawClient, _server, _auth);
        ConversationsV1UserConversation = new ConversationsV1UserConversation(_rawClient, _server, _auth);
        ConversationsV1Webhook = new ConversationsV1Webhook(_rawClient, _server, _auth);
        ConversationsV2ActionApi = new ConversationsV2ActionApi(_rawClient, _server, _auth);
        ConversationsV2CommunicationApi = new ConversationsV2CommunicationApi(_rawClient, _server, _auth);
        ConversationsV2ConfigurationApi = new ConversationsV2ConfigurationApi(_rawClient, _server, _auth);
        ConversationsV2ConversationApi = new ConversationsV2ConversationApi(_rawClient, _server, _auth);
        ConversationsV2Operation = new ConversationsV2Operation(_rawClient, _server, _auth);
        ConversationsV2ParticipantApi = new ConversationsV2ParticipantApi(_rawClient, _server, _auth);
        FlexV1Assessments = new FlexV1Assessments(_rawClient, _server, _auth);
        FlexV1ChannelApi = new FlexV1ChannelApi(_rawClient, _server, _auth);
        FlexV1ConfigurationApi = new FlexV1ConfigurationApi(_rawClient, _server, _auth);
        FlexV1ConfiguredPlugin = new FlexV1ConfiguredPlugin(_rawClient, _server, _auth);
        FlexV1FlexFlowApi = new FlexV1FlexFlowApi(_rawClient, _server, _auth);
        FlexV1InsightsAssessmentsCommentApi = new FlexV1InsightsAssessmentsCommentApi(_rawClient, _server, _auth);
        FlexV1InsightsConversationsApi = new FlexV1InsightsConversationsApi(_rawClient, _server, _auth);
        FlexV1InsightsQuestionnairesApi = new FlexV1InsightsQuestionnairesApi(_rawClient, _server, _auth);
        FlexV1InsightsQuestionnairesCategoryApi =
            new FlexV1InsightsQuestionnairesCategoryApi(_rawClient, _server, _auth);
        FlexV1InsightsQuestionnairesQuestionApi =
            new FlexV1InsightsQuestionnairesQuestionApi(_rawClient, _server, _auth);
        FlexV1InsightsSegmentsApi = new FlexV1InsightsSegmentsApi(_rawClient, _server, _auth);
        FlexV1InsightsSessionApi = new FlexV1InsightsSessionApi(_rawClient, _server, _auth);
        FlexV1InsightsSettingsAnswerSetsApi = new FlexV1InsightsSettingsAnswerSetsApi(_rawClient, _server, _auth);
        FlexV1InsightsSettingsCommentApi = new FlexV1InsightsSettingsCommentApi(_rawClient, _server, _auth);
        FlexV1InsightsUserRolesApi = new FlexV1InsightsUserRolesApi(_rawClient, _server, _auth);
        FlexV1InteractionApi = new FlexV1InteractionApi(_rawClient, _server, _auth);
        FlexV1InteractionChannel = new FlexV1InteractionChannel(_rawClient, _server, _auth);
        FlexV1InteractionChannelInvite = new FlexV1InteractionChannelInvite(_rawClient, _server, _auth);
        FlexV1InteractionChannelParticipant = new FlexV1InteractionChannelParticipant(_rawClient, _server, _auth);
        FlexV1InteractionTransfer = new FlexV1InteractionTransfer(_rawClient, _server, _auth);
        FlexV1PluginApi = new FlexV1PluginApi(_rawClient, _server, _auth);
        FlexV1PluginArchiveApi = new FlexV1PluginArchiveApi(_rawClient, _server, _auth);
        FlexV1PluginConfigurationApi = new FlexV1PluginConfigurationApi(_rawClient, _server, _auth);
        FlexV1PluginConfigurationArchiveApi = new FlexV1PluginConfigurationArchiveApi(_rawClient, _server, _auth);
        FlexV1PluginReleaseApi = new FlexV1PluginReleaseApi(_rawClient, _server, _auth);
        FlexV1PluginVersionArchiveApi = new FlexV1PluginVersionArchiveApi(_rawClient, _server, _auth);
        FlexV1PluginVersions = new FlexV1PluginVersions(_rawClient, _server, _auth);
        FlexV1ProvisioningStatusApi = new FlexV1ProvisioningStatusApi(_rawClient, _server, _auth);
        FlexV1WebChannelApi = new FlexV1WebChannelApi(_rawClient, _server, _auth);
        FlexV2FlexUserApi = new FlexV2FlexUserApi(_rawClient, _server, _auth);
        FlexV2WebChannels = new FlexV2WebChannels(_rawClient, _server, _auth);
        InsightsV1Annotation = new InsightsV1Annotation(_rawClient, _server, _auth);
        InsightsV1CallApi = new InsightsV1CallApi(_rawClient, _server, _auth);
        InsightsV1CallSummariesApi = new InsightsV1CallSummariesApi(_rawClient, _server, _auth);
        InsightsV1CallSummaryApi = new InsightsV1CallSummaryApi(_rawClient, _server, _auth);
        InsightsV1ConferenceApi = new InsightsV1ConferenceApi(_rawClient, _server, _auth);
        InsightsV1ConferenceParticipant = new InsightsV1ConferenceParticipant(_rawClient, _server, _auth);
        InsightsV1CreateAccountReport = new InsightsV1CreateAccountReport(_rawClient, _server, _auth);
        InsightsV1CreateInboundPhoneNumbersReport =
            new InsightsV1CreateInboundPhoneNumbersReport(_rawClient, _server, _auth);
        InsightsV1CreateOutboundPhoneNumbersReport =
            new InsightsV1CreateOutboundPhoneNumbersReport(_rawClient, _server, _auth);
        InsightsV1Event = new InsightsV1Event(_rawClient, _server, _auth);
        InsightsV1GetAccountReport = new InsightsV1GetAccountReport(_rawClient, _server, _auth);
        InsightsV1GetInboundPhoneNumbersReport = new InsightsV1GetInboundPhoneNumbersReport(_rawClient, _server, _auth);
        InsightsV1GetOutboundPhoneNumbersReport =
            new InsightsV1GetOutboundPhoneNumbersReport(_rawClient, _server, _auth);
        InsightsV1Metric = new InsightsV1Metric(_rawClient, _server, _auth);
        InsightsV1Participant = new InsightsV1Participant(_rawClient, _server, _auth);
        InsightsV1Room = new InsightsV1Room(_rawClient, _server, _auth);
        InsightsV1Setting = new InsightsV1Setting(_rawClient, _server, _auth);
        LookupsV1PhoneNumberApi = new LookupsV1PhoneNumberApi(_rawClient, _server, _auth);
        LookupsV2PhoneNumber = new LookupsV2PhoneNumber(_rawClient, _server, _auth);
        MessagingV1AlphaSender = new MessagingV1AlphaSender(_rawClient, _server, _auth);
        MessagingV1BrandRegistration = new MessagingV1BrandRegistration(_rawClient, _server, _auth);
        MessagingV1BrandRegistrationOtp = new MessagingV1BrandRegistrationOtp(_rawClient, _server, _auth);
        MessagingV1BrandVetting = new MessagingV1BrandVetting(_rawClient, _server, _auth);
        MessagingV1ChannelSender = new MessagingV1ChannelSender(_rawClient, _server, _auth);
        MessagingV1Deactivations = new MessagingV1Deactivations(_rawClient, _server, _auth);
        MessagingV1DestinationAlphaSender = new MessagingV1DestinationAlphaSender(_rawClient, _server, _auth);
        MessagingV1DomainCerts = new MessagingV1DomainCerts(_rawClient, _server, _auth);
        MessagingV1DomainConfigApi = new MessagingV1DomainConfigApi(_rawClient, _server, _auth);
        MessagingV1DomainConfigMessagingServiceApi =
            new MessagingV1DomainConfigMessagingServiceApi(_rawClient, _server, _auth);
        MessagingV1DomainValidateDns = new MessagingV1DomainValidateDns(_rawClient, _server, _auth);
        MessagingV1ExternalCampaignApi = new MessagingV1ExternalCampaignApi(_rawClient, _server, _auth);
        MessagingV1LinkshorteningMessagingServiceApi =
            new MessagingV1LinkshorteningMessagingServiceApi(_rawClient, _server, _auth);
        MessagingV1LinkshorteningMessagingServiceDomainAssociationApi =
            new MessagingV1LinkshorteningMessagingServiceDomainAssociationApi(_rawClient, _server, _auth);
        MessagingV1PhoneNumber = new MessagingV1PhoneNumber(_rawClient, _server, _auth);
        MessagingV1RequestManagedCertApi = new MessagingV1RequestManagedCertApi(_rawClient, _server, _auth);
        MessagingV1ServiceApi = new MessagingV1ServiceApi(_rawClient, _server, _auth);
        MessagingV1ShortCode = new MessagingV1ShortCode(_rawClient, _server, _auth);
        MessagingV1TollfreeVerificationApi = new MessagingV1TollfreeVerificationApi(_rawClient, _server, _auth);
        MessagingV1UsAppToPerson = new MessagingV1UsAppToPerson(_rawClient, _server, _auth);
        MessagingV1UsAppToPersonUsecase = new MessagingV1UsAppToPersonUsecase(_rawClient, _server, _auth);
        MessagingV1UsecaseApi = new MessagingV1UsecaseApi(_rawClient, _server, _auth);
        MessagingV2ChannelsSender = new MessagingV2ChannelsSender(_rawClient, _server, _auth);
        MessagingV2DomainCerts = new MessagingV2DomainCerts(_rawClient, _server, _auth);
        MessagingV2TypingIndicator = new MessagingV2TypingIndicator(_rawClient, _server, _auth);
        MessagingV3TypingIndicator = new MessagingV3TypingIndicator(_rawClient, _server, _auth);
        NumbersV1BulkEligibilityApi = new NumbersV1BulkEligibilityApi(_rawClient, _server, _auth);
        NumbersV1EligibilityApi = new NumbersV1EligibilityApi(_rawClient, _server, _auth);
        NumbersV1PortingPortInApi = new NumbersV1PortingPortInApi(_rawClient, _server, _auth);
        NumbersV1PortingPortInPhoneNumberApi = new NumbersV1PortingPortInPhoneNumberApi(_rawClient, _server, _auth);
        NumbersV1PortingPortabilityApi = new NumbersV1PortingPortabilityApi(_rawClient, _server, _auth);
        NumbersV1PortingWebhookConfigurationApi =
            new NumbersV1PortingWebhookConfigurationApi(_rawClient, _server, _auth);
        NumbersV1PortingWebhookConfigurationDeleteApi =
            new NumbersV1PortingWebhookConfigurationDeleteApi(_rawClient, _server, _auth);
        NumbersV1PortingWebhookConfigurationFetchApi =
            new NumbersV1PortingWebhookConfigurationFetchApi(_rawClient, _server, _auth);
        NumbersV1SenderIdRegistration = new NumbersV1SenderIdRegistration(_rawClient, _server, _auth);
        NumbersV1SenderIdRegistrationEmbeddedSession =
            new NumbersV1SenderIdRegistrationEmbeddedSession(_rawClient, _server, _auth);
        NumbersV1SigningRequestConfigurationApi =
            new NumbersV1SigningRequestConfigurationApi(_rawClient, _server, _auth);
        NumbersV2AuthorizationDocumentApi = new NumbersV2AuthorizationDocumentApi(_rawClient, _server, _auth);
        NumbersV2BulkHostedNumberOrderApi = new NumbersV2BulkHostedNumberOrderApi(_rawClient, _server, _auth);
        NumbersV2Bundle = new NumbersV2Bundle(_rawClient, _server, _auth);
        NumbersV2BundleCloneApi = new NumbersV2BundleCloneApi(_rawClient, _server, _auth);
        NumbersV2BundleCopy = new NumbersV2BundleCopy(_rawClient, _server, _auth);
        NumbersV2DependentHostedNumberOrder = new NumbersV2DependentHostedNumberOrder(_rawClient, _server, _auth);
        NumbersV2EndUser = new NumbersV2EndUser(_rawClient, _server, _auth);
        NumbersV2EndUserType = new NumbersV2EndUserType(_rawClient, _server, _auth);
        NumbersV2Evaluation = new NumbersV2Evaluation(_rawClient, _server, _auth);
        NumbersV2HostedNumberOrderApi = new NumbersV2HostedNumberOrderApi(_rawClient, _server, _auth);
        NumbersV2ItemAssignment = new NumbersV2ItemAssignment(_rawClient, _server, _auth);
        NumbersV2Regulation = new NumbersV2Regulation(_rawClient, _server, _auth);
        NumbersV2ReplaceItems = new NumbersV2ReplaceItems(_rawClient, _server, _auth);
        NumbersV2SupportingDocument = new NumbersV2SupportingDocument(_rawClient, _server, _auth);
        NumbersV2SupportingDocumentType = new NumbersV2SupportingDocumentType(_rawClient, _server, _auth);
        NumbersV3HostedNumbersHostedNumberOrderApi =
            new NumbersV3HostedNumbersHostedNumberOrderApi(_rawClient, _server, _auth);
        ProxyV1Interaction = new ProxyV1Interaction(_rawClient, _server, _auth);
        ProxyV1MessageInteraction = new ProxyV1MessageInteraction(_rawClient, _server, _auth);
        ProxyV1Participant = new ProxyV1Participant(_rawClient, _server, _auth);
        ProxyV1PhoneNumber = new ProxyV1PhoneNumber(_rawClient, _server, _auth);
        ProxyV1ServiceApi = new ProxyV1ServiceApi(_rawClient, _server, _auth);
        ProxyV1Session = new ProxyV1Session(_rawClient, _server, _auth);
        StudioV1Engagement = new StudioV1Engagement(_rawClient, _server, _auth);
        StudioV1EngagementContext = new StudioV1EngagementContext(_rawClient, _server, _auth);
        StudioV1Execution = new StudioV1Execution(_rawClient, _server, _auth);
        StudioV1ExecutionContext = new StudioV1ExecutionContext(_rawClient, _server, _auth);
        StudioV1ExecutionStep = new StudioV1ExecutionStep(_rawClient, _server, _auth);
        StudioV1ExecutionStepContext = new StudioV1ExecutionStepContext(_rawClient, _server, _auth);
        StudioV1FlowApi = new StudioV1FlowApi(_rawClient, _server, _auth);
        StudioV1Step = new StudioV1Step(_rawClient, _server, _auth);
        StudioV1StepContext = new StudioV1StepContext(_rawClient, _server, _auth);
        StudioV2Execution = new StudioV2Execution(_rawClient, _server, _auth);
        StudioV2ExecutionContext = new StudioV2ExecutionContext(_rawClient, _server, _auth);
        StudioV2ExecutionStep = new StudioV2ExecutionStep(_rawClient, _server, _auth);
        StudioV2ExecutionStepContext = new StudioV2ExecutionStepContext(_rawClient, _server, _auth);
        StudioV2FlowApi = new StudioV2FlowApi(_rawClient, _server, _auth);
        StudioV2FlowRevision = new StudioV2FlowRevision(_rawClient, _server, _auth);
        StudioV2FlowTestUserApi = new StudioV2FlowTestUserApi(_rawClient, _server, _auth);
        StudioV2FlowValidateApi = new StudioV2FlowValidateApi(_rawClient, _server, _auth);
        SyncV1Document = new SyncV1Document(_rawClient, _server, _auth);
        SyncV1DocumentPermission = new SyncV1DocumentPermission(_rawClient, _server, _auth);
        SyncV1ServiceApi = new SyncV1ServiceApi(_rawClient, _server, _auth);
        SyncV1StreamMessage = new SyncV1StreamMessage(_rawClient, _server, _auth);
        SyncV1SyncList = new SyncV1SyncList(_rawClient, _server, _auth);
        SyncV1SyncListItem = new SyncV1SyncListItem(_rawClient, _server, _auth);
        SyncV1SyncListPermission = new SyncV1SyncListPermission(_rawClient, _server, _auth);
        SyncV1SyncMap = new SyncV1SyncMap(_rawClient, _server, _auth);
        SyncV1SyncMapItem = new SyncV1SyncMapItem(_rawClient, _server, _auth);
        SyncV1SyncMapPermission = new SyncV1SyncMapPermission(_rawClient, _server, _auth);
        SyncV1SyncStream = new SyncV1SyncStream(_rawClient, _server, _auth);
        TaskrouterV1Activity = new TaskrouterV1Activity(_rawClient, _server, _auth);
        TaskrouterV1Event = new TaskrouterV1Event(_rawClient, _server, _auth);
        TaskrouterV1Task = new TaskrouterV1Task(_rawClient, _server, _auth);
        TaskrouterV1TaskChannel = new TaskrouterV1TaskChannel(_rawClient, _server, _auth);
        TaskrouterV1TaskQueue = new TaskrouterV1TaskQueue(_rawClient, _server, _auth);
        TaskrouterV1TaskQueueBulkRealTimeStatistics =
            new TaskrouterV1TaskQueueBulkRealTimeStatistics(_rawClient, _server, _auth);
        TaskrouterV1TaskQueueCumulativeStatistics =
            new TaskrouterV1TaskQueueCumulativeStatistics(_rawClient, _server, _auth);
        TaskrouterV1TaskQueueRealTimeStatistics =
            new TaskrouterV1TaskQueueRealTimeStatistics(_rawClient, _server, _auth);
        TaskrouterV1TaskQueueStatistics = new TaskrouterV1TaskQueueStatistics(_rawClient, _server, _auth);
        TaskrouterV1TaskQueuesStatistics = new TaskrouterV1TaskQueuesStatistics(_rawClient, _server, _auth);
        TaskrouterV1TaskReservation = new TaskrouterV1TaskReservation(_rawClient, _server, _auth);
        TaskrouterV1Worker = new TaskrouterV1Worker(_rawClient, _server, _auth);
        TaskrouterV1WorkerChannel = new TaskrouterV1WorkerChannel(_rawClient, _server, _auth);
        TaskrouterV1WorkerReservation = new TaskrouterV1WorkerReservation(_rawClient, _server, _auth);
        TaskrouterV1WorkerStatistics = new TaskrouterV1WorkerStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkersCumulativeStatistics =
            new TaskrouterV1WorkersCumulativeStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkersRealTimeStatistics = new TaskrouterV1WorkersRealTimeStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkersStatistics = new TaskrouterV1WorkersStatistics(_rawClient, _server, _auth);
        TaskrouterV1Workflow = new TaskrouterV1Workflow(_rawClient, _server, _auth);
        TaskrouterV1WorkflowCumulativeStatistics =
            new TaskrouterV1WorkflowCumulativeStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkflowRealTimeStatistics = new TaskrouterV1WorkflowRealTimeStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkflowStatistics = new TaskrouterV1WorkflowStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkspaceApi = new TaskrouterV1WorkspaceApi(_rawClient, _server, _auth);
        TaskrouterV1WorkspaceCumulativeStatistics =
            new TaskrouterV1WorkspaceCumulativeStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkspaceRealTimeStatistics =
            new TaskrouterV1WorkspaceRealTimeStatistics(_rawClient, _server, _auth);
        TaskrouterV1WorkspaceStatistics = new TaskrouterV1WorkspaceStatistics(_rawClient, _server, _auth);
        TrusthubV1ComplianceInquiries = new TrusthubV1ComplianceInquiries(_rawClient, _server, _auth);
        TrusthubV1ComplianceRegistrationInquiries =
            new TrusthubV1ComplianceRegistrationInquiries(_rawClient, _server, _auth);
        TrusthubV1ComplianceTollfreeInquiries = new TrusthubV1ComplianceTollfreeInquiries(_rawClient, _server, _auth);
        TrusthubV1CustomerProfiles = new TrusthubV1CustomerProfiles(_rawClient, _server, _auth);
        TrusthubV1CustomerProfilesChannelEndpointAssignment =
            new TrusthubV1CustomerProfilesChannelEndpointAssignment(_rawClient, _server, _auth);
        TrusthubV1CustomerProfilesEntityAssignments =
            new TrusthubV1CustomerProfilesEntityAssignments(_rawClient, _server, _auth);
        TrusthubV1CustomerProfilesEvaluations = new TrusthubV1CustomerProfilesEvaluations(_rawClient, _server, _auth);
        TrusthubV1EndUserApi = new TrusthubV1EndUserApi(_rawClient, _server, _auth);
        TrusthubV1EndUserType = new TrusthubV1EndUserType(_rawClient, _server, _auth);
        TrusthubV1PoliciesApi = new TrusthubV1PoliciesApi(_rawClient, _server, _auth);
        TrusthubV1SupportingDocumentApi = new TrusthubV1SupportingDocumentApi(_rawClient, _server, _auth);
        TrusthubV1SupportingDocumentType = new TrusthubV1SupportingDocumentType(_rawClient, _server, _auth);
        TrusthubV1TrustProducts = new TrusthubV1TrustProducts(_rawClient, _server, _auth);
        TrusthubV1TrustProductsChannelEndpointAssignment =
            new TrusthubV1TrustProductsChannelEndpointAssignment(_rawClient, _server, _auth);
        TrusthubV1TrustProductsEntityAssignments =
            new TrusthubV1TrustProductsEntityAssignments(_rawClient, _server, _auth);
        TrusthubV1TrustProductsEvaluations = new TrusthubV1TrustProductsEvaluations(_rawClient, _server, _auth);
        TwilioInsights = new TwilioInsights(_rawClient, _server, _auth);
        V2ShortCodeApplications = new V2ShortCodeApplications(_rawClient, _server, _auth);
        VerifyV2AccessToken = new VerifyV2AccessToken(_rawClient, _server, _auth);
        VerifyV2Bucket = new VerifyV2Bucket(_rawClient, _server, _auth);
        VerifyV2Challenge = new VerifyV2Challenge(_rawClient, _server, _auth);
        VerifyV2Entity = new VerifyV2Entity(_rawClient, _server, _auth);
        VerifyV2Factor = new VerifyV2Factor(_rawClient, _server, _auth);
        VerifyV2FormApi = new VerifyV2FormApi(_rawClient, _server, _auth);
        VerifyV2MessagingConfiguration = new VerifyV2MessagingConfiguration(_rawClient, _server, _auth);
        VerifyV2NewChallenge = new VerifyV2NewChallenge(_rawClient, _server, _auth);
        VerifyV2NewFactor = new VerifyV2NewFactor(_rawClient, _server, _auth);
        VerifyV2Notification = new VerifyV2Notification(_rawClient, _server, _auth);
        VerifyV2RateLimit = new VerifyV2RateLimit(_rawClient, _server, _auth);
        VerifyV2SafelistApi = new VerifyV2SafelistApi(_rawClient, _server, _auth);
        VerifyV2ServiceApi = new VerifyV2ServiceApi(_rawClient, _server, _auth);
        VerifyV2Template = new VerifyV2Template(_rawClient, _server, _auth);
        VerifyV2Verification = new VerifyV2Verification(_rawClient, _server, _auth);
        VerifyV2VerificationAttemptApi = new VerifyV2VerificationAttemptApi(_rawClient, _server, _auth);
        VerifyV2VerificationAttemptsSummaryApi = new VerifyV2VerificationAttemptsSummaryApi(_rawClient, _server, _auth);
        VerifyV2VerificationCheck = new VerifyV2VerificationCheck(_rawClient, _server, _auth);
        VerifyV2Webhook = new VerifyV2Webhook(_rawClient, _server, _auth);
        VideoV1Anonymize = new VideoV1Anonymize(_rawClient, _server, _auth);
        VideoV1CompositionApi = new VideoV1CompositionApi(_rawClient, _server, _auth);
        VideoV1CompositionHookApi = new VideoV1CompositionHookApi(_rawClient, _server, _auth);
        VideoV1CompositionSettingsApi = new VideoV1CompositionSettingsApi(_rawClient, _server, _auth);
        VideoV1Participant = new VideoV1Participant(_rawClient, _server, _auth);
        VideoV1PublishedTrack = new VideoV1PublishedTrack(_rawClient, _server, _auth);
        VideoV1RecordingApi = new VideoV1RecordingApi(_rawClient, _server, _auth);
        VideoV1RecordingRules = new VideoV1RecordingRules(_rawClient, _server, _auth);
        VideoV1RecordingSettingsApi = new VideoV1RecordingSettingsApi(_rawClient, _server, _auth);
        VideoV1RoomApi = new VideoV1RoomApi(_rawClient, _server, _auth);
        VideoV1RoomRecording = new VideoV1RoomRecording(_rawClient, _server, _auth);
        VideoV1SubscribeRules = new VideoV1SubscribeRules(_rawClient, _server, _auth);
        VideoV1SubscribedTrack = new VideoV1SubscribedTrack(_rawClient, _server, _auth);
        VideoV1Transcriptions = new VideoV1Transcriptions(_rawClient, _server, _auth);
    }

    public Api20100401Account Api20100401Account { get; }

    public Api20100401AddOnResult Api20100401AddOnResult { get; }

    public Api20100401Address Api20100401Address { get; }

    public Api20100401AllTime Api20100401AllTime { get; }

    public Api20100401Application Api20100401Application { get; }

    public Api20100401AssignedAddOn Api20100401AssignedAddOn { get; }

    public Api20100401AssignedAddOnExtension Api20100401AssignedAddOnExtension { get; }

    public Api20100401AuthCallsCredentialListMapping Api20100401AuthCallsCredentialListMapping { get; }

    public Api20100401AuthCallsIpAccessControlListMapping Api20100401AuthCallsIpAccessControlListMapping { get; }

    public Api20100401AuthRegistrationsCredentialListMapping Api20100401AuthRegistrationsCredentialListMapping { get; }

    public Api20100401AuthorizedConnectApp Api20100401AuthorizedConnectApp { get; }

    public Api20100401AvailablePhoneNumberCountry Api20100401AvailablePhoneNumberCountry { get; }

    public Api20100401Balance Api20100401Balance { get; }

    public Api20100401Call Api20100401Call { get; }

    public Api20100401CallNotification Api20100401CallNotification { get; }

    public Api20100401CallRecording Api20100401CallRecording { get; }

    public Api20100401CallTranscription Api20100401CallTranscription { get; }

    public Api20100401Conference Api20100401Conference { get; }

    public Api20100401ConferenceRecording Api20100401ConferenceRecording { get; }

    public Api20100401ConnectApp Api20100401ConnectApp { get; }

    public Api20100401Credential Api20100401Credential { get; }

    public Api20100401CredentialList Api20100401CredentialList { get; }

    public Api20100401CredentialListMapping Api20100401CredentialListMapping { get; }

    public Api20100401Daily Api20100401Daily { get; }

    public Api20100401Data Api20100401Data { get; }

    public Api20100401DependentPhoneNumber Api20100401DependentPhoneNumber { get; }

    public Api20100401Domain Api20100401Domain { get; }

    public Api20100401Event Api20100401Event { get; }

    public Api20100401Feedback Api20100401Feedback { get; }

    public Api20100401IncomingPhoneNumber Api20100401IncomingPhoneNumber { get; }

    public Api20100401IncomingPhoneNumberLocal Api20100401IncomingPhoneNumberLocal { get; }

    public Api20100401IncomingPhoneNumberMobile Api20100401IncomingPhoneNumberMobile { get; }

    public Api20100401IncomingPhoneNumberTollFree Api20100401IncomingPhoneNumberTollFree { get; }

    public Api20100401IpAccessControlList Api20100401IpAccessControlList { get; }

    public Api20100401IpAccessControlListMapping Api20100401IpAccessControlListMapping { get; }

    public Api20100401Key Api20100401Key { get; }

    public Api20100401LastMonth Api20100401LastMonth { get; }

    public Api20100401Local Api20100401Local { get; }

    public Api20100401MachineToMachine Api20100401MachineToMachine { get; }

    public Api20100401Media Api20100401Media { get; }

    public Api20100401MediaInstance Api20100401MediaInstance { get; }

    public Api20100401Member Api20100401Member { get; }

    public Api20100401Message Api20100401Message { get; }

    public Api20100401Mobile Api20100401Mobile { get; }

    public Api20100401Monthly Api20100401Monthly { get; }

    public Api20100401National Api20100401National { get; }

    public Api20100401NewKey Api20100401NewKey { get; }

    public Api20100401NewSigningKey Api20100401NewSigningKey { get; }

    public Api20100401Notification Api20100401Notification { get; }

    public Api20100401OutgoingCallerId Api20100401OutgoingCallerId { get; }

    public Api20100401Participant Api20100401Participant { get; }

    public Api20100401Payload Api20100401Payload { get; }

    public Api20100401Payment Api20100401Payment { get; }

    public Api20100401Queue Api20100401Queue { get; }

    public Api20100401Record Api20100401Record { get; }

    public Api20100401Recording Api20100401Recording { get; }

    public Api20100401RecordingTranscription Api20100401RecordingTranscription { get; }

    public Api20100401SharedCost Api20100401SharedCost { get; }

    public Api20100401ShortCode Api20100401ShortCode { get; }

    public Api20100401SigningKey Api20100401SigningKey { get; }

    public Api20100401SipIpAddress Api20100401SipIpAddress { get; }

    public Api20100401Siprec Api20100401Siprec { get; }

    public Api20100401Stream Api20100401Stream { get; }

    public Api20100401ThisMonth Api20100401ThisMonth { get; }

    public Api20100401Today Api20100401Today { get; }

    public Api20100401Token Api20100401Token { get; }

    public Api20100401TollFree Api20100401TollFree { get; }

    public Api20100401Transcription Api20100401Transcription { get; }

    public Api20100401Trigger Api20100401Trigger { get; }

    public Api20100401UserDefinedMessage Api20100401UserDefinedMessage { get; }

    public Api20100401UserDefinedMessageSubscription Api20100401UserDefinedMessageSubscription { get; }

    public Api20100401ValidationRequest Api20100401ValidationRequest { get; }

    public Api20100401Voip Api20100401Voip { get; }

    public Api20100401Yearly Api20100401Yearly { get; }

    public Api20100401Yesterday Api20100401Yesterday { get; }

    public ContentV2Content ContentV2Content { get; }

    public ContentV2ContentAndApprovals ContentV2ContentAndApprovals { get; }

    public Contentv1ApprovalCreate Contentv1ApprovalCreate { get; }

    public Contentv1ApprovalFetch Contentv1ApprovalFetch { get; }

    public Contentv1ContentApi Contentv1ContentApi { get; }

    public Contentv1ContentAndApprovalsApi Contentv1ContentAndApprovalsApi { get; }

    public Contentv1LegacyContentApi Contentv1LegacyContentApi { get; }

    public ConversationsV1AddressConfiguration ConversationsV1AddressConfiguration { get; }

    public ConversationsV1Binding ConversationsV1Binding { get; }

    public ConversationsV1ConfigurationApi ConversationsV1ConfigurationApi { get; }

    public ConversationsV1ConversationApi ConversationsV1ConversationApi { get; }

    public ConversationsV1ConversationWithParticipantsApi ConversationsV1ConversationWithParticipantsApi { get; }

    public ConversationsV1CredentialApi ConversationsV1CredentialApi { get; }

    public ConversationsV1DeliveryReceipt ConversationsV1DeliveryReceipt { get; }

    public ConversationsV1Message ConversationsV1Message { get; }

    public ConversationsV1Notification ConversationsV1Notification { get; }

    public ConversationsV1Participant ConversationsV1Participant { get; }

    public ConversationsV1ParticipantConversationApi ConversationsV1ParticipantConversationApi { get; }

    public ConversationsV1RoleApi ConversationsV1RoleApi { get; }

    public ConversationsV1ServiceApi ConversationsV1ServiceApi { get; }

    public ConversationsV1UserApi ConversationsV1UserApi { get; }

    public ConversationsV1UserConversation ConversationsV1UserConversation { get; }

    public ConversationsV1Webhook ConversationsV1Webhook { get; }

    /// <summary>
    /// Perform actions within a Conversation. Actions trigger side effects such as sending messages and return 202 Accepted.
    /// </summary>
    public ConversationsV2ActionApi ConversationsV2ActionApi { get; }

    /// <summary>
    /// A communication is the smallest unit of interaction within a conversation. Each communication represents a single event—such as an SMS message or a voice utterance.
    /// </summary>
    public ConversationsV2CommunicationApi ConversationsV2CommunicationApi { get; }

    /// <summary>
    /// A conversation configuration is the top-level object in Conversation Orchestrator. It contains the settings that define how Conversation Orchestrator captures traffic and connects to other services.
    /// </summary>
    public ConversationsV2ConfigurationApi ConversationsV2ConfigurationApi { get; }

    /// <summary>
    /// A conversation is a record of interactions between participants. It's the container for all communications that occur during an interaction, including voice calls, SMS messages, and other supported channels.
    /// </summary>
    public ConversationsV2ConversationApi ConversationsV2ConversationApi { get; }

    /// <summary>
    /// Poll the status of a long-running operation.
    /// </summary>
    public ConversationsV2Operation ConversationsV2Operation { get; }

    /// <summary>
    /// A participant represents an actor involved in a conversation. Conversation Orchestrator assigns each participant a type that identifies their role, such as customer, human agent, or AI agent.
    /// </summary>
    public ConversationsV2ParticipantApi ConversationsV2ParticipantApi { get; }

    public FlexV1Assessments FlexV1Assessments { get; }

    public FlexV1ChannelApi FlexV1ChannelApi { get; }

    public FlexV1ConfigurationApi FlexV1ConfigurationApi { get; }

    public FlexV1ConfiguredPlugin FlexV1ConfiguredPlugin { get; }

    public FlexV1FlexFlowApi FlexV1FlexFlowApi { get; }

    public FlexV1InsightsAssessmentsCommentApi FlexV1InsightsAssessmentsCommentApi { get; }

    public FlexV1InsightsConversationsApi FlexV1InsightsConversationsApi { get; }

    public FlexV1InsightsQuestionnairesApi FlexV1InsightsQuestionnairesApi { get; }

    public FlexV1InsightsQuestionnairesCategoryApi FlexV1InsightsQuestionnairesCategoryApi { get; }

    public FlexV1InsightsQuestionnairesQuestionApi FlexV1InsightsQuestionnairesQuestionApi { get; }

    public FlexV1InsightsSegmentsApi FlexV1InsightsSegmentsApi { get; }

    public FlexV1InsightsSessionApi FlexV1InsightsSessionApi { get; }

    public FlexV1InsightsSettingsAnswerSetsApi FlexV1InsightsSettingsAnswerSetsApi { get; }

    public FlexV1InsightsSettingsCommentApi FlexV1InsightsSettingsCommentApi { get; }

    public FlexV1InsightsUserRolesApi FlexV1InsightsUserRolesApi { get; }

    public FlexV1InteractionApi FlexV1InteractionApi { get; }

    public FlexV1InteractionChannel FlexV1InteractionChannel { get; }

    public FlexV1InteractionChannelInvite FlexV1InteractionChannelInvite { get; }

    public FlexV1InteractionChannelParticipant FlexV1InteractionChannelParticipant { get; }

    public FlexV1InteractionTransfer FlexV1InteractionTransfer { get; }

    public FlexV1PluginApi FlexV1PluginApi { get; }

    public FlexV1PluginArchiveApi FlexV1PluginArchiveApi { get; }

    public FlexV1PluginConfigurationApi FlexV1PluginConfigurationApi { get; }

    public FlexV1PluginConfigurationArchiveApi FlexV1PluginConfigurationArchiveApi { get; }

    public FlexV1PluginReleaseApi FlexV1PluginReleaseApi { get; }

    public FlexV1PluginVersionArchiveApi FlexV1PluginVersionArchiveApi { get; }

    public FlexV1PluginVersions FlexV1PluginVersions { get; }

    public FlexV1ProvisioningStatusApi FlexV1ProvisioningStatusApi { get; }

    public FlexV1WebChannelApi FlexV1WebChannelApi { get; }

    public FlexV2FlexUserApi FlexV2FlexUserApi { get; }

    public FlexV2WebChannels FlexV2WebChannels { get; }

    public InsightsV1Annotation InsightsV1Annotation { get; }

    public InsightsV1CallApi InsightsV1CallApi { get; }

    public InsightsV1CallSummariesApi InsightsV1CallSummariesApi { get; }

    public InsightsV1CallSummaryApi InsightsV1CallSummaryApi { get; }

    public InsightsV1ConferenceApi InsightsV1ConferenceApi { get; }

    public InsightsV1ConferenceParticipant InsightsV1ConferenceParticipant { get; }

    public InsightsV1CreateAccountReport InsightsV1CreateAccountReport { get; }

    public InsightsV1CreateInboundPhoneNumbersReport InsightsV1CreateInboundPhoneNumbersReport { get; }

    public InsightsV1CreateOutboundPhoneNumbersReport InsightsV1CreateOutboundPhoneNumbersReport { get; }

    public InsightsV1Event InsightsV1Event { get; }

    public InsightsV1GetAccountReport InsightsV1GetAccountReport { get; }

    public InsightsV1GetInboundPhoneNumbersReport InsightsV1GetInboundPhoneNumbersReport { get; }

    public InsightsV1GetOutboundPhoneNumbersReport InsightsV1GetOutboundPhoneNumbersReport { get; }

    public InsightsV1Metric InsightsV1Metric { get; }

    public InsightsV1Participant InsightsV1Participant { get; }

    public InsightsV1Room InsightsV1Room { get; }

    public InsightsV1Setting InsightsV1Setting { get; }

    public LookupsV1PhoneNumberApi LookupsV1PhoneNumberApi { get; }

    public LookupsV2PhoneNumber LookupsV2PhoneNumber { get; }

    public MessagingV1AlphaSender MessagingV1AlphaSender { get; }

    public MessagingV1BrandRegistration MessagingV1BrandRegistration { get; }

    public MessagingV1BrandRegistrationOtp MessagingV1BrandRegistrationOtp { get; }

    public MessagingV1BrandVetting MessagingV1BrandVetting { get; }

    public MessagingV1ChannelSender MessagingV1ChannelSender { get; }

    public MessagingV1Deactivations MessagingV1Deactivations { get; }

    public MessagingV1DestinationAlphaSender MessagingV1DestinationAlphaSender { get; }

    public MessagingV1DomainCerts MessagingV1DomainCerts { get; }

    public MessagingV1DomainConfigApi MessagingV1DomainConfigApi { get; }

    public MessagingV1DomainConfigMessagingServiceApi MessagingV1DomainConfigMessagingServiceApi { get; }

    public MessagingV1DomainValidateDns MessagingV1DomainValidateDns { get; }

    public MessagingV1ExternalCampaignApi MessagingV1ExternalCampaignApi { get; }

    public MessagingV1LinkshorteningMessagingServiceApi MessagingV1LinkshorteningMessagingServiceApi { get; }

    public MessagingV1LinkshorteningMessagingServiceDomainAssociationApi MessagingV1LinkshorteningMessagingServiceDomainAssociationApi { get; }

    public MessagingV1PhoneNumber MessagingV1PhoneNumber { get; }

    public MessagingV1RequestManagedCertApi MessagingV1RequestManagedCertApi { get; }

    public MessagingV1ServiceApi MessagingV1ServiceApi { get; }

    public MessagingV1ShortCode MessagingV1ShortCode { get; }

    public MessagingV1TollfreeVerificationApi MessagingV1TollfreeVerificationApi { get; }

    public MessagingV1UsAppToPerson MessagingV1UsAppToPerson { get; }

    public MessagingV1UsAppToPersonUsecase MessagingV1UsAppToPersonUsecase { get; }

    public MessagingV1UsecaseApi MessagingV1UsecaseApi { get; }

    public MessagingV2ChannelsSender MessagingV2ChannelsSender { get; }

    public MessagingV2DomainCerts MessagingV2DomainCerts { get; }

    public MessagingV2TypingIndicator MessagingV2TypingIndicator { get; }

    /// <summary>
    /// Send typing indicators to OTT channel recipients (WhatsApp, Apple Messages for Business).
    /// </summary>
    public MessagingV3TypingIndicator MessagingV3TypingIndicator { get; }

    public NumbersV1BulkEligibilityApi NumbersV1BulkEligibilityApi { get; }

    public NumbersV1EligibilityApi NumbersV1EligibilityApi { get; }

    public NumbersV1PortingPortInApi NumbersV1PortingPortInApi { get; }

    public NumbersV1PortingPortInPhoneNumberApi NumbersV1PortingPortInPhoneNumberApi { get; }

    public NumbersV1PortingPortabilityApi NumbersV1PortingPortabilityApi { get; }

    public NumbersV1PortingWebhookConfigurationApi NumbersV1PortingWebhookConfigurationApi { get; }

    public NumbersV1PortingWebhookConfigurationDeleteApi NumbersV1PortingWebhookConfigurationDeleteApi { get; }

    public NumbersV1PortingWebhookConfigurationFetchApi NumbersV1PortingWebhookConfigurationFetchApi { get; }

    public NumbersV1SenderIdRegistration NumbersV1SenderIdRegistration { get; }

    public NumbersV1SenderIdRegistrationEmbeddedSession NumbersV1SenderIdRegistrationEmbeddedSession { get; }

    public NumbersV1SigningRequestConfigurationApi NumbersV1SigningRequestConfigurationApi { get; }

    public NumbersV2AuthorizationDocumentApi NumbersV2AuthorizationDocumentApi { get; }

    public NumbersV2BulkHostedNumberOrderApi NumbersV2BulkHostedNumberOrderApi { get; }

    public NumbersV2Bundle NumbersV2Bundle { get; }

    public NumbersV2BundleCloneApi NumbersV2BundleCloneApi { get; }

    public NumbersV2BundleCopy NumbersV2BundleCopy { get; }

    public NumbersV2DependentHostedNumberOrder NumbersV2DependentHostedNumberOrder { get; }

    public NumbersV2EndUser NumbersV2EndUser { get; }

    public NumbersV2EndUserType NumbersV2EndUserType { get; }

    public NumbersV2Evaluation NumbersV2Evaluation { get; }

    public NumbersV2HostedNumberOrderApi NumbersV2HostedNumberOrderApi { get; }

    public NumbersV2ItemAssignment NumbersV2ItemAssignment { get; }

    public NumbersV2Regulation NumbersV2Regulation { get; }

    public NumbersV2ReplaceItems NumbersV2ReplaceItems { get; }

    public NumbersV2SupportingDocument NumbersV2SupportingDocument { get; }

    public NumbersV2SupportingDocumentType NumbersV2SupportingDocumentType { get; }

    public NumbersV3HostedNumbersHostedNumberOrderApi NumbersV3HostedNumbersHostedNumberOrderApi { get; }

    public ProxyV1Interaction ProxyV1Interaction { get; }

    public ProxyV1MessageInteraction ProxyV1MessageInteraction { get; }

    public ProxyV1Participant ProxyV1Participant { get; }

    public ProxyV1PhoneNumber ProxyV1PhoneNumber { get; }

    public ProxyV1ServiceApi ProxyV1ServiceApi { get; }

    public ProxyV1Session ProxyV1Session { get; }

    public StudioV1Engagement StudioV1Engagement { get; }

    public StudioV1EngagementContext StudioV1EngagementContext { get; }

    public StudioV1Execution StudioV1Execution { get; }

    public StudioV1ExecutionContext StudioV1ExecutionContext { get; }

    public StudioV1ExecutionStep StudioV1ExecutionStep { get; }

    public StudioV1ExecutionStepContext StudioV1ExecutionStepContext { get; }

    public StudioV1FlowApi StudioV1FlowApi { get; }

    public StudioV1Step StudioV1Step { get; }

    public StudioV1StepContext StudioV1StepContext { get; }

    public StudioV2Execution StudioV2Execution { get; }

    public StudioV2ExecutionContext StudioV2ExecutionContext { get; }

    public StudioV2ExecutionStep StudioV2ExecutionStep { get; }

    public StudioV2ExecutionStepContext StudioV2ExecutionStepContext { get; }

    public StudioV2FlowApi StudioV2FlowApi { get; }

    public StudioV2FlowRevision StudioV2FlowRevision { get; }

    public StudioV2FlowTestUserApi StudioV2FlowTestUserApi { get; }

    public StudioV2FlowValidateApi StudioV2FlowValidateApi { get; }

    public SyncV1Document SyncV1Document { get; }

    public SyncV1DocumentPermission SyncV1DocumentPermission { get; }

    public SyncV1ServiceApi SyncV1ServiceApi { get; }

    public SyncV1StreamMessage SyncV1StreamMessage { get; }

    public SyncV1SyncList SyncV1SyncList { get; }

    public SyncV1SyncListItem SyncV1SyncListItem { get; }

    public SyncV1SyncListPermission SyncV1SyncListPermission { get; }

    public SyncV1SyncMap SyncV1SyncMap { get; }

    public SyncV1SyncMapItem SyncV1SyncMapItem { get; }

    public SyncV1SyncMapPermission SyncV1SyncMapPermission { get; }

    public SyncV1SyncStream SyncV1SyncStream { get; }

    public TaskrouterV1Activity TaskrouterV1Activity { get; }

    public TaskrouterV1Event TaskrouterV1Event { get; }

    public TaskrouterV1Task TaskrouterV1Task { get; }

    public TaskrouterV1TaskChannel TaskrouterV1TaskChannel { get; }

    public TaskrouterV1TaskQueue TaskrouterV1TaskQueue { get; }

    public TaskrouterV1TaskQueueBulkRealTimeStatistics TaskrouterV1TaskQueueBulkRealTimeStatistics { get; }

    public TaskrouterV1TaskQueueCumulativeStatistics TaskrouterV1TaskQueueCumulativeStatistics { get; }

    public TaskrouterV1TaskQueueRealTimeStatistics TaskrouterV1TaskQueueRealTimeStatistics { get; }

    public TaskrouterV1TaskQueueStatistics TaskrouterV1TaskQueueStatistics { get; }

    public TaskrouterV1TaskQueuesStatistics TaskrouterV1TaskQueuesStatistics { get; }

    public TaskrouterV1TaskReservation TaskrouterV1TaskReservation { get; }

    public TaskrouterV1Worker TaskrouterV1Worker { get; }

    public TaskrouterV1WorkerChannel TaskrouterV1WorkerChannel { get; }

    public TaskrouterV1WorkerReservation TaskrouterV1WorkerReservation { get; }

    public TaskrouterV1WorkerStatistics TaskrouterV1WorkerStatistics { get; }

    public TaskrouterV1WorkersCumulativeStatistics TaskrouterV1WorkersCumulativeStatistics { get; }

    public TaskrouterV1WorkersRealTimeStatistics TaskrouterV1WorkersRealTimeStatistics { get; }

    public TaskrouterV1WorkersStatistics TaskrouterV1WorkersStatistics { get; }

    public TaskrouterV1Workflow TaskrouterV1Workflow { get; }

    public TaskrouterV1WorkflowCumulativeStatistics TaskrouterV1WorkflowCumulativeStatistics { get; }

    public TaskrouterV1WorkflowRealTimeStatistics TaskrouterV1WorkflowRealTimeStatistics { get; }

    public TaskrouterV1WorkflowStatistics TaskrouterV1WorkflowStatistics { get; }

    public TaskrouterV1WorkspaceApi TaskrouterV1WorkspaceApi { get; }

    public TaskrouterV1WorkspaceCumulativeStatistics TaskrouterV1WorkspaceCumulativeStatistics { get; }

    public TaskrouterV1WorkspaceRealTimeStatistics TaskrouterV1WorkspaceRealTimeStatistics { get; }

    public TaskrouterV1WorkspaceStatistics TaskrouterV1WorkspaceStatistics { get; }

    public TrusthubV1ComplianceInquiries TrusthubV1ComplianceInquiries { get; }

    public TrusthubV1ComplianceRegistrationInquiries TrusthubV1ComplianceRegistrationInquiries { get; }

    public TrusthubV1ComplianceTollfreeInquiries TrusthubV1ComplianceTollfreeInquiries { get; }

    public TrusthubV1CustomerProfiles TrusthubV1CustomerProfiles { get; }

    public TrusthubV1CustomerProfilesChannelEndpointAssignment TrusthubV1CustomerProfilesChannelEndpointAssignment { get; }

    public TrusthubV1CustomerProfilesEntityAssignments TrusthubV1CustomerProfilesEntityAssignments { get; }

    public TrusthubV1CustomerProfilesEvaluations TrusthubV1CustomerProfilesEvaluations { get; }

    public TrusthubV1EndUserApi TrusthubV1EndUserApi { get; }

    public TrusthubV1EndUserType TrusthubV1EndUserType { get; }

    public TrusthubV1PoliciesApi TrusthubV1PoliciesApi { get; }

    public TrusthubV1SupportingDocumentApi TrusthubV1SupportingDocumentApi { get; }

    public TrusthubV1SupportingDocumentType TrusthubV1SupportingDocumentType { get; }

    public TrusthubV1TrustProducts TrusthubV1TrustProducts { get; }

    public TrusthubV1TrustProductsChannelEndpointAssignment TrusthubV1TrustProductsChannelEndpointAssignment { get; }

    public TrusthubV1TrustProductsEntityAssignments TrusthubV1TrustProductsEntityAssignments { get; }

    public TrusthubV1TrustProductsEvaluations TrusthubV1TrustProductsEvaluations { get; }

    /// <summary>
    /// Twilio Insights API.
    /// </summary>
    public TwilioInsights TwilioInsights { get; }

    public V2ShortCodeApplications V2ShortCodeApplications { get; }

    public VerifyV2AccessToken VerifyV2AccessToken { get; }

    public VerifyV2Bucket VerifyV2Bucket { get; }

    public VerifyV2Challenge VerifyV2Challenge { get; }

    public VerifyV2Entity VerifyV2Entity { get; }

    public VerifyV2Factor VerifyV2Factor { get; }

    public VerifyV2FormApi VerifyV2FormApi { get; }

    public VerifyV2MessagingConfiguration VerifyV2MessagingConfiguration { get; }

    public VerifyV2NewChallenge VerifyV2NewChallenge { get; }

    public VerifyV2NewFactor VerifyV2NewFactor { get; }

    public VerifyV2Notification VerifyV2Notification { get; }

    public VerifyV2RateLimit VerifyV2RateLimit { get; }

    public VerifyV2SafelistApi VerifyV2SafelistApi { get; }

    public VerifyV2ServiceApi VerifyV2ServiceApi { get; }

    public VerifyV2Template VerifyV2Template { get; }

    public VerifyV2Verification VerifyV2Verification { get; }

    public VerifyV2VerificationAttemptApi VerifyV2VerificationAttemptApi { get; }

    public VerifyV2VerificationAttemptsSummaryApi VerifyV2VerificationAttemptsSummaryApi { get; }

    public VerifyV2VerificationCheck VerifyV2VerificationCheck { get; }

    public VerifyV2Webhook VerifyV2Webhook { get; }

    public VideoV1Anonymize VideoV1Anonymize { get; }

    public VideoV1CompositionApi VideoV1CompositionApi { get; }

    public VideoV1CompositionHookApi VideoV1CompositionHookApi { get; }

    public VideoV1CompositionSettingsApi VideoV1CompositionSettingsApi { get; }

    public VideoV1Participant VideoV1Participant { get; }

    public VideoV1PublishedTrack VideoV1PublishedTrack { get; }

    public VideoV1RecordingApi VideoV1RecordingApi { get; }

    public VideoV1RecordingRules VideoV1RecordingRules { get; }

    public VideoV1RecordingSettingsApi VideoV1RecordingSettingsApi { get; }

    public VideoV1RoomApi VideoV1RoomApi { get; }

    public VideoV1RoomRecording VideoV1RoomRecording { get; }

    public VideoV1SubscribeRules VideoV1SubscribeRules { get; }

    public VideoV1SubscribedTrack VideoV1SubscribedTrack { get; }

    public VideoV1Transcriptions VideoV1Transcriptions { get; }

    /// <summary>
    /// In Request Bulk
    /// </summary>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="LookupResponse1"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Discussions made regarding how to help the customer to correlation request and response objects:
    /// - Respecting the natural order (requests vs. response)
    /// - Using phone numbers as unique key
    /// - Adding a correlation_id key
    /// </remarks>
    public Task<LookupResponse1> CreateBulkLookup(LookupRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/batch/query"),
            [],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<LookupResponse1>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Create Override for a Phone Number for a specific field
    /// </summary>
    /// <param name="field"></param>
    /// <param name="phoneNumber"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OverridesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="CreateLookupPhoneNumberOverridesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Create an Override for a specific package and phone number.
    /// </remarks>
    public Task<OverridesResponse> CreateLookupPhoneNumberOverrides(string field,
        string phoneNumber,
        OverridesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/PhoneNumbers/{PhoneNumber}/Overrides/{Field}"),
            [new TemplateParam("Field", field), new TemplateParam("PhoneNumber", phoneNumber)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<OverridesResponse>(),
            CreateLookupPhoneNumberOverridesErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete an Override for a Phone Number for a specific field
    /// </summary>
    /// <param name="field"></param>
    /// <param name="phoneNumber"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteLookupPhoneNumberOverridesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Delete an Override for a specific package and phone number.
    /// </remarks>
    public Task DeleteLookupPhoneNumberOverrides(string field,
        string phoneNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/PhoneNumbers/{PhoneNumber}/Overrides/{Field}"),
            [new TemplateParam("Field", field), new TemplateParam("PhoneNumber", phoneNumber)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteLookupPhoneNumberOverridesErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Delete rate limit
    /// </summary>
    /// <param name="field">bucket name</param>
    /// <param name="bucket">bucket name</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="DeleteLookupRateLimitError"/> when the server returns an error response.</exception>
    public Task DeleteLookupRateLimit(string field,
        string bucket,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/RateLimits/Fields/{Field}/Bucket/{Bucket}"),
            [new TemplateParam("Field", field), new TemplateParam("Bucket", bucket)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Delete,
            EmptyBody.Instance,
            VoidResponse.Instance,
            DeleteLookupRateLimitErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get account rate limits
    /// </summary>
    /// <param name="fields"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="RateLimitListResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchLookupAccountRateLimitsError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve the list of rate limits for all fields (if any)
    /// It returns also the twilio rate limits.
    /// </remarks>
    public Task<RateLimitListResponse> FetchLookupAccountRateLimits(IReadOnlyList<string>? fields,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/RateLimits"),
            [],
            [new Param("Fields", fields)],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<RateLimitListResponse>(),
            FetchLookupAccountRateLimitsErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get Overrides for a Phone Number for a specific field.
    /// </summary>
    /// <param name="field"></param>
    /// <param name="phoneNumber"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OverridesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchLookupPhoneNumberOverridesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Retrieve an Override for a specific package and phone number.
    /// </remarks>
    public Task<OverridesResponse> FetchLookupPhoneNumberOverrides(string field,
        string phoneNumber,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/PhoneNumbers/{PhoneNumber}/Overrides/{Field}"),
            [new TemplateParam("Field", field), new TemplateParam("PhoneNumber", phoneNumber)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<OverridesResponse>(),
            FetchLookupPhoneNumberOverridesErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Get rate limit
    /// </summary>
    /// <param name="field">bucket name</param>
    /// <param name="bucket">bucket name</param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="RateLimitResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="FetchLookupRateLimitError"/> when the server returns an error response.</exception>
    public Task<RateLimitResponse> FetchLookupRateLimit(string field,
        string bucket,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/RateLimits/Fields/{Field}/Bucket/{Bucket}"),
            [new TemplateParam("Field", field), new TemplateParam("Bucket", bucket)],
            [],
            [],
            HttpMethod.Get,
            EmptyBody.Instance,
            JsonResponse.Create<RateLimitResponse>(),
            FetchLookupRateLimitErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Approve a Passkeys Challenge
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2ServicesPasskeysApproveChallengeResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Approve a Passkeys challenge
    /// </remarks>
    public Task<V2ServicesPasskeysApproveChallengeResponse> UpdateChallengePasskeys(string serviceSid,
        ApprovePasskeysChallengeRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Passkeys/ApproveChallenge"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<V2ServicesPasskeysApproveChallengeResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Update Override for a Phone Number for a specific field
    /// </summary>
    /// <param name="field"></param>
    /// <param name="phoneNumber"></param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="OverridesResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateLookupPhoneNumberOverridesError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Update an Override for a specific package and phone number.
    /// </remarks>
    public Task<OverridesResponse> UpdateLookupPhoneNumberOverrides(string field,
        string phoneNumber,
        OverridesRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/PhoneNumbers/{PhoneNumber}/Overrides/{Field}"),
            [new TemplateParam("Field", field), new TemplateParam("PhoneNumber", phoneNumber)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<OverridesResponse>(),
            UpdateLookupPhoneNumberOverridesErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Upsert rate limit
    /// </summary>
    /// <param name="field">field name</param>
    /// <param name="bucket">bucket name</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="RateLimitResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="UpdateLookupRateLimitError"/> when the server returns an error response.</exception>
    public Task<RateLimitResponse> UpdateLookupRateLimit(string field,
        string bucket,
        RateLimitRequest? body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default4("/v2/RateLimits/Fields/{Field}/Bucket/{Bucket}"),
            [new TemplateParam("Field", field), new TemplateParam("Bucket", bucket)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Put,
            JsonRequest.Create(body),
            JsonResponse.Create<RateLimitResponse>(),
            UpdateLookupRateLimitErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);

    /// <summary>
    /// Verify a Passkeys Factor
    /// </summary>
    /// <param name="serviceSid">The unique SID identifier of the Service.</param>
    /// <param name="body"></param>
    /// <param name="requestOptions">Per-request options, such as an overriding log level for this call</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="Task{TResult}"/> of <see cref="V2ServicesPasskeysVerifyFactorResponse"/> instance.</returns>
    /// <exception cref="SdkException{TResult}"> of <see cref="RawError"/> when the server returns an error response.</exception>
    /// <remarks>
    /// Verify a Passkeys Factor
    /// </remarks>
    public Task<V2ServicesPasskeysVerifyFactorResponse> UpdatePasskeysFactor(string serviceSid,
        VerifyPasskeysFactorRequest body,
        RequestOptions? requestOptions = null,
        CancellationToken ct = default) =>
        _rawClient.Execute(_server.Default3("/v2/Services/{ServiceSid}/Passkeys/VerifyFactor"),
            [new TemplateParam("ServiceSid", serviceSid)],
            [],
            [new HeaderParam("Idempotency-Key", Guid.NewGuid())],
            HttpMethod.Post,
            JsonRequest.Create(body),
            JsonResponse.Create<V2ServicesPasskeysVerifyFactorResponse>(),
            RawErrorResponse.Instance,
            [_auth.AccountSidAuthToken],
            requestOptions,
            ct);
}
