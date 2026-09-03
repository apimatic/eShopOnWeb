using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TwilioSdk.Api;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Core.Logging;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Request;
using TwilioSdk.Core.Response;
using TwilioSdk.Errors;
using TwilioSdk.Models;

namespace TwilioSdk;

/// <summary>
/// This is the public Twilio REST API., Manage configurations, conversations, participants, and communications. Create configurations to define capture rules and channel settings, then use conversations to group related communications., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the public Twilio REST API., This is the reference API for the rest-proxy server., Insights Domain V3 API.
/// </summary>
public sealed class TwilioSdkClient
{
    private readonly RawClient _rawClient;
    private readonly Server _server;
    private readonly AuthSchemes _auth;

    public TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)
    {
        _server = new Server(options.Environment, options.Server);
        var queryParameterFactory = new QueryParameterFactory([]);
        var templateParamsFactory = new TemplateParamsFactory([]);
        var urlFactory = new UriFactory(queryParameterFactory, templateParamsFactory);
        var httpStatusPolicy = new HttpStatusPolicy([]);
        var headersFactory =
            new HeadersFactory([new HeaderParam("User-Agent", "TwilioSdkClient/1.0.0 CSharp"),
                    new HeaderParam("X-APIMatic-Lang", "CSharp"),
                    new HeaderParam("X-APIMatic-Package-Version", "1.0.0"),
                    new HeaderParam("X-APIMatic-Gen-Version", "4.0.0"),
                    new HeaderParam("X-APIMatic-OS", RuntimeEnvironment.Os),
                    new HeaderParam("X-APIMatic-Runtime", RuntimeEnvironment.Runtime)]);
        var resiliencePipelineFactory = new ResiliencePipelineFactory(options.Retry);
        var httpLogger = new HttpLogger(options.Logging, "TwilioSdkClient");
        _rawClient =
            new RawClient(httpClient,
                urlFactory,
                httpStatusPolicy,
                headersFactory,
                resiliencePipelineFactory,
                httpLogger,
                options.Hooks);
        _auth = new AuthSchemes(options);
    }

    public Api20100401Account Api20100401Account =>
        field ??= new Api20100401Account(_rawClient, _server, _auth);

    public Api20100401AddOnResult Api20100401AddOnResult =>
        field ??= new Api20100401AddOnResult(_rawClient, _server, _auth);

    public Api20100401Address Api20100401Address =>
        field ??= new Api20100401Address(_rawClient, _server, _auth);

    public Api20100401AllTime Api20100401AllTime =>
        field ??= new Api20100401AllTime(_rawClient, _server, _auth);

    public Api20100401Application Api20100401Application =>
        field ??= new Api20100401Application(_rawClient, _server, _auth);

    public Api20100401AssignedAddOn Api20100401AssignedAddOn =>
        field ??= new Api20100401AssignedAddOn(_rawClient, _server, _auth);

    public Api20100401AssignedAddOnExtension Api20100401AssignedAddOnExtension =>
        field ??= new Api20100401AssignedAddOnExtension(_rawClient, _server, _auth);

    public Api20100401AuthCallsCredentialListMapping Api20100401AuthCallsCredentialListMapping =>
        field ??= new Api20100401AuthCallsCredentialListMapping(_rawClient, _server, _auth);

    public Api20100401AuthCallsIpAccessControlListMapping Api20100401AuthCallsIpAccessControlListMapping =>
        field ??= new Api20100401AuthCallsIpAccessControlListMapping(_rawClient, _server, _auth);

    public Api20100401AuthRegistrationsCredentialListMapping Api20100401AuthRegistrationsCredentialListMapping =>
        field ??= new Api20100401AuthRegistrationsCredentialListMapping(_rawClient, _server, _auth);

    public Api20100401AuthorizedConnectApp Api20100401AuthorizedConnectApp =>
        field ??= new Api20100401AuthorizedConnectApp(_rawClient, _server, _auth);

    public Api20100401AvailablePhoneNumberCountry Api20100401AvailablePhoneNumberCountry =>
        field ??= new Api20100401AvailablePhoneNumberCountry(_rawClient, _server, _auth);

    public Api20100401Balance Api20100401Balance =>
        field ??= new Api20100401Balance(_rawClient, _server, _auth);

    public Api20100401Call Api20100401Call => field ??= new Api20100401Call(_rawClient, _server, _auth);

    public Api20100401CallNotification Api20100401CallNotification =>
        field ??= new Api20100401CallNotification(_rawClient, _server, _auth);

    public Api20100401CallRecording Api20100401CallRecording =>
        field ??= new Api20100401CallRecording(_rawClient, _server, _auth);

    public Api20100401CallTranscription Api20100401CallTranscription =>
        field ??= new Api20100401CallTranscription(_rawClient, _server, _auth);

    public Api20100401Conference Api20100401Conference =>
        field ??= new Api20100401Conference(_rawClient, _server, _auth);

    public Api20100401ConferenceRecording Api20100401ConferenceRecording =>
        field ??= new Api20100401ConferenceRecording(_rawClient, _server, _auth);

    public Api20100401ConnectApp Api20100401ConnectApp =>
        field ??= new Api20100401ConnectApp(_rawClient, _server, _auth);

    public Api20100401Credential Api20100401Credential =>
        field ??= new Api20100401Credential(_rawClient, _server, _auth);

    public Api20100401CredentialList Api20100401CredentialList =>
        field ??= new Api20100401CredentialList(_rawClient, _server, _auth);

    public Api20100401CredentialListMapping Api20100401CredentialListMapping =>
        field ??= new Api20100401CredentialListMapping(_rawClient, _server, _auth);

    public Api20100401Daily Api20100401Daily => field ??= new Api20100401Daily(_rawClient, _server, _auth);

    public Api20100401Data Api20100401Data => field ??= new Api20100401Data(_rawClient, _server, _auth);

    public Api20100401DependentPhoneNumber Api20100401DependentPhoneNumber =>
        field ??= new Api20100401DependentPhoneNumber(_rawClient, _server, _auth);

    public Api20100401Domain Api20100401Domain => field ??= new Api20100401Domain(_rawClient, _server, _auth);

    public Api20100401Event Api20100401Event => field ??= new Api20100401Event(_rawClient, _server, _auth);

    public Api20100401Feedback Api20100401Feedback =>
        field ??= new Api20100401Feedback(_rawClient, _server, _auth);

    public Api20100401IncomingPhoneNumber Api20100401IncomingPhoneNumber =>
        field ??= new Api20100401IncomingPhoneNumber(_rawClient, _server, _auth);

    public Api20100401IncomingPhoneNumberLocal Api20100401IncomingPhoneNumberLocal =>
        field ??= new Api20100401IncomingPhoneNumberLocal(_rawClient, _server, _auth);

    public Api20100401IncomingPhoneNumberMobile Api20100401IncomingPhoneNumberMobile =>
        field ??= new Api20100401IncomingPhoneNumberMobile(_rawClient, _server, _auth);

    public Api20100401IncomingPhoneNumberTollFree Api20100401IncomingPhoneNumberTollFree =>
        field ??= new Api20100401IncomingPhoneNumberTollFree(_rawClient, _server, _auth);

    public Api20100401IpAccessControlList Api20100401IpAccessControlList =>
        field ??= new Api20100401IpAccessControlList(_rawClient, _server, _auth);

    public Api20100401IpAccessControlListMapping Api20100401IpAccessControlListMapping =>
        field ??= new Api20100401IpAccessControlListMapping(_rawClient, _server, _auth);

    public Api20100401Key Api20100401Key => field ??= new Api20100401Key(_rawClient, _server, _auth);

    public Api20100401LastMonth Api20100401LastMonth =>
        field ??= new Api20100401LastMonth(_rawClient, _server, _auth);

    public Api20100401Local Api20100401Local => field ??= new Api20100401Local(_rawClient, _server, _auth);

    public Api20100401MachineToMachine Api20100401MachineToMachine =>
        field ??= new Api20100401MachineToMachine(_rawClient, _server, _auth);

    public Api20100401Media Api20100401Media => field ??= new Api20100401Media(_rawClient, _server, _auth);

    public Api20100401MediaInstance Api20100401MediaInstance =>
        field ??= new Api20100401MediaInstance(_rawClient, _server, _auth);

    public Api20100401Member Api20100401Member => field ??= new Api20100401Member(_rawClient, _server, _auth);

    public Api20100401Message Api20100401Message =>
        field ??= new Api20100401Message(_rawClient, _server, _auth);

    public Api20100401Mobile Api20100401Mobile => field ??= new Api20100401Mobile(_rawClient, _server, _auth);

    public Api20100401Monthly Api20100401Monthly =>
        field ??= new Api20100401Monthly(_rawClient, _server, _auth);

    public Api20100401National Api20100401National =>
        field ??= new Api20100401National(_rawClient, _server, _auth);

    public Api20100401NewKey Api20100401NewKey => field ??= new Api20100401NewKey(_rawClient, _server, _auth);

    public Api20100401NewSigningKey Api20100401NewSigningKey =>
        field ??= new Api20100401NewSigningKey(_rawClient, _server, _auth);

    public Api20100401Notification Api20100401Notification =>
        field ??= new Api20100401Notification(_rawClient, _server, _auth);

    public Api20100401OutgoingCallerId Api20100401OutgoingCallerId =>
        field ??= new Api20100401OutgoingCallerId(_rawClient, _server, _auth);

    public Api20100401Participant Api20100401Participant =>
        field ??= new Api20100401Participant(_rawClient, _server, _auth);

    public Api20100401Payload Api20100401Payload =>
        field ??= new Api20100401Payload(_rawClient, _server, _auth);

    public Api20100401Payment Api20100401Payment =>
        field ??= new Api20100401Payment(_rawClient, _server, _auth);

    public Api20100401Queue Api20100401Queue => field ??= new Api20100401Queue(_rawClient, _server, _auth);

    public Api20100401Record Api20100401Record => field ??= new Api20100401Record(_rawClient, _server, _auth);

    public Api20100401Recording Api20100401Recording =>
        field ??= new Api20100401Recording(_rawClient, _server, _auth);

    public Api20100401RecordingTranscription Api20100401RecordingTranscription =>
        field ??= new Api20100401RecordingTranscription(_rawClient, _server, _auth);

    public Api20100401SharedCost Api20100401SharedCost =>
        field ??= new Api20100401SharedCost(_rawClient, _server, _auth);

    public Api20100401ShortCode Api20100401ShortCode =>
        field ??= new Api20100401ShortCode(_rawClient, _server, _auth);

    public Api20100401SigningKey Api20100401SigningKey =>
        field ??= new Api20100401SigningKey(_rawClient, _server, _auth);

    public Api20100401SipIpAddress Api20100401SipIpAddress =>
        field ??= new Api20100401SipIpAddress(_rawClient, _server, _auth);

    public Api20100401Siprec Api20100401Siprec => field ??= new Api20100401Siprec(_rawClient, _server, _auth);

    public Api20100401Stream Api20100401Stream => field ??= new Api20100401Stream(_rawClient, _server, _auth);

    public Api20100401ThisMonth Api20100401ThisMonth =>
        field ??= new Api20100401ThisMonth(_rawClient, _server, _auth);

    public Api20100401Today Api20100401Today => field ??= new Api20100401Today(_rawClient, _server, _auth);

    public Api20100401Token Api20100401Token => field ??= new Api20100401Token(_rawClient, _server, _auth);

    public Api20100401TollFree Api20100401TollFree =>
        field ??= new Api20100401TollFree(_rawClient, _server, _auth);

    public Api20100401Transcription Api20100401Transcription =>
        field ??= new Api20100401Transcription(_rawClient, _server, _auth);

    public Api20100401Trigger Api20100401Trigger =>
        field ??= new Api20100401Trigger(_rawClient, _server, _auth);

    public Api20100401UserDefinedMessage Api20100401UserDefinedMessage =>
        field ??= new Api20100401UserDefinedMessage(_rawClient, _server, _auth);

    public Api20100401UserDefinedMessageSubscription Api20100401UserDefinedMessageSubscription =>
        field ??= new Api20100401UserDefinedMessageSubscription(_rawClient, _server, _auth);

    public Api20100401ValidationRequest Api20100401ValidationRequest =>
        field ??= new Api20100401ValidationRequest(_rawClient, _server, _auth);

    public Api20100401Voip Api20100401Voip => field ??= new Api20100401Voip(_rawClient, _server, _auth);

    public Api20100401Yearly Api20100401Yearly => field ??= new Api20100401Yearly(_rawClient, _server, _auth);

    public Api20100401Yesterday Api20100401Yesterday =>
        field ??= new Api20100401Yesterday(_rawClient, _server, _auth);

    public ContentV2Content ContentV2Content => field ??= new ContentV2Content(_rawClient, _server, _auth);

    public ContentV2ContentAndApprovals ContentV2ContentAndApprovals =>
        field ??= new ContentV2ContentAndApprovals(_rawClient, _server, _auth);

    public Contentv1ApprovalCreate Contentv1ApprovalCreate =>
        field ??= new Contentv1ApprovalCreate(_rawClient, _server, _auth);

    public Contentv1ApprovalFetch Contentv1ApprovalFetch =>
        field ??= new Contentv1ApprovalFetch(_rawClient, _server, _auth);

    public Contentv1ContentApi Contentv1ContentApi =>
        field ??= new Contentv1ContentApi(_rawClient, _server, _auth);

    public Contentv1ContentAndApprovalsApi Contentv1ContentAndApprovalsApi =>
        field ??= new Contentv1ContentAndApprovalsApi(_rawClient, _server, _auth);

    public Contentv1LegacyContentApi Contentv1LegacyContentApi =>
        field ??= new Contentv1LegacyContentApi(_rawClient, _server, _auth);

    public ConversationsV1AddressConfiguration ConversationsV1AddressConfiguration =>
        field ??= new ConversationsV1AddressConfiguration(_rawClient, _server, _auth);

    public ConversationsV1Binding ConversationsV1Binding =>
        field ??= new ConversationsV1Binding(_rawClient, _server, _auth);

    public ConversationsV1ConfigurationApi ConversationsV1ConfigurationApi =>
        field ??= new ConversationsV1ConfigurationApi(_rawClient, _server, _auth);

    public ConversationsV1ConversationApi ConversationsV1ConversationApi =>
        field ??= new ConversationsV1ConversationApi(_rawClient, _server, _auth);

    public ConversationsV1ConversationWithParticipantsApi ConversationsV1ConversationWithParticipantsApi =>
        field ??= new ConversationsV1ConversationWithParticipantsApi(_rawClient, _server, _auth);

    public ConversationsV1CredentialApi ConversationsV1CredentialApi =>
        field ??= new ConversationsV1CredentialApi(_rawClient, _server, _auth);

    public ConversationsV1DeliveryReceipt ConversationsV1DeliveryReceipt =>
        field ??= new ConversationsV1DeliveryReceipt(_rawClient, _server, _auth);

    public ConversationsV1Message ConversationsV1Message =>
        field ??= new ConversationsV1Message(_rawClient, _server, _auth);

    public ConversationsV1Notification ConversationsV1Notification =>
        field ??= new ConversationsV1Notification(_rawClient, _server, _auth);

    public ConversationsV1Participant ConversationsV1Participant =>
        field ??= new ConversationsV1Participant(_rawClient, _server, _auth);

    public ConversationsV1ParticipantConversationApi ConversationsV1ParticipantConversationApi =>
        field ??= new ConversationsV1ParticipantConversationApi(_rawClient, _server, _auth);

    public ConversationsV1RoleApi ConversationsV1RoleApi =>
        field ??= new ConversationsV1RoleApi(_rawClient, _server, _auth);

    public ConversationsV1ServiceApi ConversationsV1ServiceApi =>
        field ??= new ConversationsV1ServiceApi(_rawClient, _server, _auth);

    public ConversationsV1UserApi ConversationsV1UserApi =>
        field ??= new ConversationsV1UserApi(_rawClient, _server, _auth);

    public ConversationsV1UserConversation ConversationsV1UserConversation =>
        field ??= new ConversationsV1UserConversation(_rawClient, _server, _auth);

    public ConversationsV1Webhook ConversationsV1Webhook =>
        field ??= new ConversationsV1Webhook(_rawClient, _server, _auth);

    /// <summary>
    /// Perform actions within a Conversation. Actions trigger side effects such as sending messages and return 202 Accepted.
    /// </summary>
    public ConversationsV2ActionApi ConversationsV2ActionApi =>
        field ??= new ConversationsV2ActionApi(_rawClient, _server, _auth);

    /// <summary>
    /// A communication is the smallest unit of interaction within a conversation. Each communication represents a single event—such as an SMS message or a voice utterance.
    /// </summary>
    public ConversationsV2CommunicationApi ConversationsV2CommunicationApi =>
        field ??= new ConversationsV2CommunicationApi(_rawClient, _server, _auth);

    /// <summary>
    /// A conversation configuration is the top-level object in Conversation Orchestrator. It contains the settings that define how Conversation Orchestrator captures traffic and connects to other services.
    /// </summary>
    public ConversationsV2ConfigurationApi ConversationsV2ConfigurationApi =>
        field ??= new ConversationsV2ConfigurationApi(_rawClient, _server, _auth);

    /// <summary>
    /// A conversation is a record of interactions between participants. It's the container for all communications that occur during an interaction, including voice calls, SMS messages, and other supported channels.
    /// </summary>
    public ConversationsV2ConversationApi ConversationsV2ConversationApi =>
        field ??= new ConversationsV2ConversationApi(_rawClient, _server, _auth);

    /// <summary>
    /// Poll the status of a long-running operation.
    /// </summary>
    public ConversationsV2Operation ConversationsV2Operation =>
        field ??= new ConversationsV2Operation(_rawClient, _server, _auth);

    /// <summary>
    /// A participant represents an actor involved in a conversation. Conversation Orchestrator assigns each participant a type that identifies their role, such as customer, human agent, or AI agent.
    /// </summary>
    public ConversationsV2ParticipantApi ConversationsV2ParticipantApi =>
        field ??= new ConversationsV2ParticipantApi(_rawClient, _server, _auth);

    public FlexV1Assessments FlexV1Assessments => field ??= new FlexV1Assessments(_rawClient, _server, _auth);

    public FlexV1ChannelApi FlexV1ChannelApi => field ??= new FlexV1ChannelApi(_rawClient, _server, _auth);

    public FlexV1ConfigurationApi FlexV1ConfigurationApi =>
        field ??= new FlexV1ConfigurationApi(_rawClient, _server, _auth);

    public FlexV1ConfiguredPlugin FlexV1ConfiguredPlugin =>
        field ??= new FlexV1ConfiguredPlugin(_rawClient, _server, _auth);

    public FlexV1FlexFlowApi FlexV1FlexFlowApi => field ??= new FlexV1FlexFlowApi(_rawClient, _server, _auth);

    public FlexV1InsightsAssessmentsCommentApi FlexV1InsightsAssessmentsCommentApi =>
        field ??= new FlexV1InsightsAssessmentsCommentApi(_rawClient, _server, _auth);

    public FlexV1InsightsConversationsApi FlexV1InsightsConversationsApi =>
        field ??= new FlexV1InsightsConversationsApi(_rawClient, _server, _auth);

    public FlexV1InsightsQuestionnairesApi FlexV1InsightsQuestionnairesApi =>
        field ??= new FlexV1InsightsQuestionnairesApi(_rawClient, _server, _auth);

    public FlexV1InsightsQuestionnairesCategoryApi FlexV1InsightsQuestionnairesCategoryApi =>
        field ??= new FlexV1InsightsQuestionnairesCategoryApi(_rawClient, _server, _auth);

    public FlexV1InsightsQuestionnairesQuestionApi FlexV1InsightsQuestionnairesQuestionApi =>
        field ??= new FlexV1InsightsQuestionnairesQuestionApi(_rawClient, _server, _auth);

    public FlexV1InsightsSegmentsApi FlexV1InsightsSegmentsApi =>
        field ??= new FlexV1InsightsSegmentsApi(_rawClient, _server, _auth);

    public FlexV1InsightsSessionApi FlexV1InsightsSessionApi =>
        field ??= new FlexV1InsightsSessionApi(_rawClient, _server, _auth);

    public FlexV1InsightsSettingsAnswerSetsApi FlexV1InsightsSettingsAnswerSetsApi =>
        field ??= new FlexV1InsightsSettingsAnswerSetsApi(_rawClient, _server, _auth);

    public FlexV1InsightsSettingsCommentApi FlexV1InsightsSettingsCommentApi =>
        field ??= new FlexV1InsightsSettingsCommentApi(_rawClient, _server, _auth);

    public FlexV1InsightsUserRolesApi FlexV1InsightsUserRolesApi =>
        field ??= new FlexV1InsightsUserRolesApi(_rawClient, _server, _auth);

    public FlexV1InteractionApi FlexV1InteractionApi =>
        field ??= new FlexV1InteractionApi(_rawClient, _server, _auth);

    public FlexV1InteractionChannel FlexV1InteractionChannel =>
        field ??= new FlexV1InteractionChannel(_rawClient, _server, _auth);

    public FlexV1InteractionChannelInvite FlexV1InteractionChannelInvite =>
        field ??= new FlexV1InteractionChannelInvite(_rawClient, _server, _auth);

    public FlexV1InteractionChannelParticipant FlexV1InteractionChannelParticipant =>
        field ??= new FlexV1InteractionChannelParticipant(_rawClient, _server, _auth);

    public FlexV1InteractionTransfer FlexV1InteractionTransfer =>
        field ??= new FlexV1InteractionTransfer(_rawClient, _server, _auth);

    public FlexV1PluginApi FlexV1PluginApi => field ??= new FlexV1PluginApi(_rawClient, _server, _auth);

    public FlexV1PluginArchiveApi FlexV1PluginArchiveApi =>
        field ??= new FlexV1PluginArchiveApi(_rawClient, _server, _auth);

    public FlexV1PluginConfigurationApi FlexV1PluginConfigurationApi =>
        field ??= new FlexV1PluginConfigurationApi(_rawClient, _server, _auth);

    public FlexV1PluginConfigurationArchiveApi FlexV1PluginConfigurationArchiveApi =>
        field ??= new FlexV1PluginConfigurationArchiveApi(_rawClient, _server, _auth);

    public FlexV1PluginReleaseApi FlexV1PluginReleaseApi =>
        field ??= new FlexV1PluginReleaseApi(_rawClient, _server, _auth);

    public FlexV1PluginVersionArchiveApi FlexV1PluginVersionArchiveApi =>
        field ??= new FlexV1PluginVersionArchiveApi(_rawClient, _server, _auth);

    public FlexV1PluginVersions FlexV1PluginVersions =>
        field ??= new FlexV1PluginVersions(_rawClient, _server, _auth);

    public FlexV1ProvisioningStatusApi FlexV1ProvisioningStatusApi =>
        field ??= new FlexV1ProvisioningStatusApi(_rawClient, _server, _auth);

    public FlexV1WebChannelApi FlexV1WebChannelApi =>
        field ??= new FlexV1WebChannelApi(_rawClient, _server, _auth);

    public FlexV2FlexUserApi FlexV2FlexUserApi => field ??= new FlexV2FlexUserApi(_rawClient, _server, _auth);

    public FlexV2WebChannels FlexV2WebChannels => field ??= new FlexV2WebChannels(_rawClient, _server, _auth);

    public InsightsV1Annotation InsightsV1Annotation =>
        field ??= new InsightsV1Annotation(_rawClient, _server, _auth);

    public InsightsV1CallApi InsightsV1CallApi => field ??= new InsightsV1CallApi(_rawClient, _server, _auth);

    public InsightsV1CallSummariesApi InsightsV1CallSummariesApi =>
        field ??= new InsightsV1CallSummariesApi(_rawClient, _server, _auth);

    public InsightsV1CallSummaryApi InsightsV1CallSummaryApi =>
        field ??= new InsightsV1CallSummaryApi(_rawClient, _server, _auth);

    public InsightsV1ConferenceApi InsightsV1ConferenceApi =>
        field ??= new InsightsV1ConferenceApi(_rawClient, _server, _auth);

    public InsightsV1ConferenceParticipant InsightsV1ConferenceParticipant =>
        field ??= new InsightsV1ConferenceParticipant(_rawClient, _server, _auth);

    public InsightsV1CreateAccountReport InsightsV1CreateAccountReport =>
        field ??= new InsightsV1CreateAccountReport(_rawClient, _server, _auth);

    public InsightsV1CreateInboundPhoneNumbersReport InsightsV1CreateInboundPhoneNumbersReport =>
        field ??= new InsightsV1CreateInboundPhoneNumbersReport(_rawClient, _server, _auth);

    public InsightsV1CreateOutboundPhoneNumbersReport InsightsV1CreateOutboundPhoneNumbersReport =>
        field ??= new InsightsV1CreateOutboundPhoneNumbersReport(_rawClient, _server, _auth);

    public InsightsV1Event InsightsV1Event => field ??= new InsightsV1Event(_rawClient, _server, _auth);

    public InsightsV1GetAccountReport InsightsV1GetAccountReport =>
        field ??= new InsightsV1GetAccountReport(_rawClient, _server, _auth);

    public InsightsV1GetInboundPhoneNumbersReport InsightsV1GetInboundPhoneNumbersReport =>
        field ??= new InsightsV1GetInboundPhoneNumbersReport(_rawClient, _server, _auth);

    public InsightsV1GetOutboundPhoneNumbersReport InsightsV1GetOutboundPhoneNumbersReport =>
        field ??= new InsightsV1GetOutboundPhoneNumbersReport(_rawClient, _server, _auth);

    public InsightsV1Metric InsightsV1Metric => field ??= new InsightsV1Metric(_rawClient, _server, _auth);

    public InsightsV1Participant InsightsV1Participant =>
        field ??= new InsightsV1Participant(_rawClient, _server, _auth);

    public InsightsV1Room InsightsV1Room => field ??= new InsightsV1Room(_rawClient, _server, _auth);

    public InsightsV1Setting InsightsV1Setting => field ??= new InsightsV1Setting(_rawClient, _server, _auth);

    public LookupsV1PhoneNumberApi LookupsV1PhoneNumberApi =>
        field ??= new LookupsV1PhoneNumberApi(_rawClient, _server, _auth);

    public LookupsV2PhoneNumber LookupsV2PhoneNumber =>
        field ??= new LookupsV2PhoneNumber(_rawClient, _server, _auth);

    public MessagingV1AlphaSender MessagingV1AlphaSender =>
        field ??= new MessagingV1AlphaSender(_rawClient, _server, _auth);

    public MessagingV1BrandRegistration MessagingV1BrandRegistration =>
        field ??= new MessagingV1BrandRegistration(_rawClient, _server, _auth);

    public MessagingV1BrandRegistrationOtp MessagingV1BrandRegistrationOtp =>
        field ??= new MessagingV1BrandRegistrationOtp(_rawClient, _server, _auth);

    public MessagingV1BrandVetting MessagingV1BrandVetting =>
        field ??= new MessagingV1BrandVetting(_rawClient, _server, _auth);

    public MessagingV1ChannelSender MessagingV1ChannelSender =>
        field ??= new MessagingV1ChannelSender(_rawClient, _server, _auth);

    public MessagingV1Deactivations MessagingV1Deactivations =>
        field ??= new MessagingV1Deactivations(_rawClient, _server, _auth);

    public MessagingV1DestinationAlphaSender MessagingV1DestinationAlphaSender =>
        field ??= new MessagingV1DestinationAlphaSender(_rawClient, _server, _auth);

    public MessagingV1DomainCerts MessagingV1DomainCerts =>
        field ??= new MessagingV1DomainCerts(_rawClient, _server, _auth);

    public MessagingV1DomainConfigApi MessagingV1DomainConfigApi =>
        field ??= new MessagingV1DomainConfigApi(_rawClient, _server, _auth);

    public MessagingV1DomainConfigMessagingServiceApi MessagingV1DomainConfigMessagingServiceApi =>
        field ??= new MessagingV1DomainConfigMessagingServiceApi(_rawClient, _server, _auth);

    public MessagingV1DomainValidateDns MessagingV1DomainValidateDns =>
        field ??= new MessagingV1DomainValidateDns(_rawClient, _server, _auth);

    public MessagingV1ExternalCampaignApi MessagingV1ExternalCampaignApi =>
        field ??= new MessagingV1ExternalCampaignApi(_rawClient, _server, _auth);

    public MessagingV1LinkshorteningMessagingServiceApi MessagingV1LinkshorteningMessagingServiceApi =>
        field ??= new MessagingV1LinkshorteningMessagingServiceApi(_rawClient, _server, _auth);

    public MessagingV1LinkshorteningMessagingServiceDomainAssociationApi MessagingV1LinkshorteningMessagingServiceDomainAssociationApi =>
        field ??= new MessagingV1LinkshorteningMessagingServiceDomainAssociationApi(_rawClient, _server, _auth);

    public MessagingV1PhoneNumber MessagingV1PhoneNumber =>
        field ??= new MessagingV1PhoneNumber(_rawClient, _server, _auth);

    public MessagingV1RequestManagedCertApi MessagingV1RequestManagedCertApi =>
        field ??= new MessagingV1RequestManagedCertApi(_rawClient, _server, _auth);

    public MessagingV1ServiceApi MessagingV1ServiceApi =>
        field ??= new MessagingV1ServiceApi(_rawClient, _server, _auth);

    public MessagingV1ShortCode MessagingV1ShortCode =>
        field ??= new MessagingV1ShortCode(_rawClient, _server, _auth);

    public MessagingV1TollfreeVerificationApi MessagingV1TollfreeVerificationApi =>
        field ??= new MessagingV1TollfreeVerificationApi(_rawClient, _server, _auth);

    public MessagingV1UsAppToPerson MessagingV1UsAppToPerson =>
        field ??= new MessagingV1UsAppToPerson(_rawClient, _server, _auth);

    public MessagingV1UsAppToPersonUsecase MessagingV1UsAppToPersonUsecase =>
        field ??= new MessagingV1UsAppToPersonUsecase(_rawClient, _server, _auth);

    public MessagingV1UsecaseApi MessagingV1UsecaseApi =>
        field ??= new MessagingV1UsecaseApi(_rawClient, _server, _auth);

    public MessagingV2ChannelsSender MessagingV2ChannelsSender =>
        field ??= new MessagingV2ChannelsSender(_rawClient, _server, _auth);

    public MessagingV2DomainCerts MessagingV2DomainCerts =>
        field ??= new MessagingV2DomainCerts(_rawClient, _server, _auth);

    public MessagingV2TypingIndicator MessagingV2TypingIndicator =>
        field ??= new MessagingV2TypingIndicator(_rawClient, _server, _auth);

    /// <summary>
    /// Send typing indicators to OTT channel recipients (WhatsApp, Apple Messages for Business).
    /// </summary>
    public MessagingV3TypingIndicator MessagingV3TypingIndicator =>
        field ??= new MessagingV3TypingIndicator(_rawClient, _server, _auth);

    public NumbersV1BulkEligibilityApi NumbersV1BulkEligibilityApi =>
        field ??= new NumbersV1BulkEligibilityApi(_rawClient, _server, _auth);

    public NumbersV1EligibilityApi NumbersV1EligibilityApi =>
        field ??= new NumbersV1EligibilityApi(_rawClient, _server, _auth);

    public NumbersV1PortingPortInApi NumbersV1PortingPortInApi =>
        field ??= new NumbersV1PortingPortInApi(_rawClient, _server, _auth);

    public NumbersV1PortingPortInPhoneNumberApi NumbersV1PortingPortInPhoneNumberApi =>
        field ??= new NumbersV1PortingPortInPhoneNumberApi(_rawClient, _server, _auth);

    public NumbersV1PortingPortabilityApi NumbersV1PortingPortabilityApi =>
        field ??= new NumbersV1PortingPortabilityApi(_rawClient, _server, _auth);

    public NumbersV1PortingWebhookConfigurationApi NumbersV1PortingWebhookConfigurationApi =>
        field ??= new NumbersV1PortingWebhookConfigurationApi(_rawClient, _server, _auth);

    public NumbersV1PortingWebhookConfigurationDeleteApi NumbersV1PortingWebhookConfigurationDeleteApi =>
        field ??= new NumbersV1PortingWebhookConfigurationDeleteApi(_rawClient, _server, _auth);

    public NumbersV1PortingWebhookConfigurationFetchApi NumbersV1PortingWebhookConfigurationFetchApi =>
        field ??= new NumbersV1PortingWebhookConfigurationFetchApi(_rawClient, _server, _auth);

    public NumbersV1SenderIdRegistration NumbersV1SenderIdRegistration =>
        field ??= new NumbersV1SenderIdRegistration(_rawClient, _server, _auth);

    public NumbersV1SenderIdRegistrationEmbeddedSession NumbersV1SenderIdRegistrationEmbeddedSession =>
        field ??= new NumbersV1SenderIdRegistrationEmbeddedSession(_rawClient, _server, _auth);

    public NumbersV1SigningRequestConfigurationApi NumbersV1SigningRequestConfigurationApi =>
        field ??= new NumbersV1SigningRequestConfigurationApi(_rawClient, _server, _auth);

    public NumbersV2AuthorizationDocumentApi NumbersV2AuthorizationDocumentApi =>
        field ??= new NumbersV2AuthorizationDocumentApi(_rawClient, _server, _auth);

    public NumbersV2BulkHostedNumberOrderApi NumbersV2BulkHostedNumberOrderApi =>
        field ??= new NumbersV2BulkHostedNumberOrderApi(_rawClient, _server, _auth);

    public NumbersV2Bundle NumbersV2Bundle => field ??= new NumbersV2Bundle(_rawClient, _server, _auth);

    public NumbersV2BundleCloneApi NumbersV2BundleCloneApi =>
        field ??= new NumbersV2BundleCloneApi(_rawClient, _server, _auth);

    public NumbersV2BundleCopy NumbersV2BundleCopy =>
        field ??= new NumbersV2BundleCopy(_rawClient, _server, _auth);

    public NumbersV2DependentHostedNumberOrder NumbersV2DependentHostedNumberOrder =>
        field ??= new NumbersV2DependentHostedNumberOrder(_rawClient, _server, _auth);

    public NumbersV2EndUser NumbersV2EndUser => field ??= new NumbersV2EndUser(_rawClient, _server, _auth);

    public NumbersV2EndUserType NumbersV2EndUserType =>
        field ??= new NumbersV2EndUserType(_rawClient, _server, _auth);

    public NumbersV2Evaluation NumbersV2Evaluation =>
        field ??= new NumbersV2Evaluation(_rawClient, _server, _auth);

    public NumbersV2HostedNumberOrderApi NumbersV2HostedNumberOrderApi =>
        field ??= new NumbersV2HostedNumberOrderApi(_rawClient, _server, _auth);

    public NumbersV2ItemAssignment NumbersV2ItemAssignment =>
        field ??= new NumbersV2ItemAssignment(_rawClient, _server, _auth);

    public NumbersV2Regulation NumbersV2Regulation =>
        field ??= new NumbersV2Regulation(_rawClient, _server, _auth);

    public NumbersV2ReplaceItems NumbersV2ReplaceItems =>
        field ??= new NumbersV2ReplaceItems(_rawClient, _server, _auth);

    public NumbersV2SupportingDocument NumbersV2SupportingDocument =>
        field ??= new NumbersV2SupportingDocument(_rawClient, _server, _auth);

    public NumbersV2SupportingDocumentType NumbersV2SupportingDocumentType =>
        field ??= new NumbersV2SupportingDocumentType(_rawClient, _server, _auth);

    public NumbersV3HostedNumbersHostedNumberOrderApi NumbersV3HostedNumbersHostedNumberOrderApi =>
        field ??= new NumbersV3HostedNumbersHostedNumberOrderApi(_rawClient, _server, _auth);

    public ProxyV1Interaction ProxyV1Interaction =>
        field ??= new ProxyV1Interaction(_rawClient, _server, _auth);

    public ProxyV1MessageInteraction ProxyV1MessageInteraction =>
        field ??= new ProxyV1MessageInteraction(_rawClient, _server, _auth);

    public ProxyV1Participant ProxyV1Participant =>
        field ??= new ProxyV1Participant(_rawClient, _server, _auth);

    public ProxyV1PhoneNumber ProxyV1PhoneNumber =>
        field ??= new ProxyV1PhoneNumber(_rawClient, _server, _auth);

    public ProxyV1ServiceApi ProxyV1ServiceApi => field ??= new ProxyV1ServiceApi(_rawClient, _server, _auth);

    public ProxyV1Session ProxyV1Session => field ??= new ProxyV1Session(_rawClient, _server, _auth);

    public StudioV1Engagement StudioV1Engagement =>
        field ??= new StudioV1Engagement(_rawClient, _server, _auth);

    public StudioV1EngagementContext StudioV1EngagementContext =>
        field ??= new StudioV1EngagementContext(_rawClient, _server, _auth);

    public StudioV1Execution StudioV1Execution => field ??= new StudioV1Execution(_rawClient, _server, _auth);

    public StudioV1ExecutionContext StudioV1ExecutionContext =>
        field ??= new StudioV1ExecutionContext(_rawClient, _server, _auth);

    public StudioV1ExecutionStep StudioV1ExecutionStep =>
        field ??= new StudioV1ExecutionStep(_rawClient, _server, _auth);

    public StudioV1ExecutionStepContext StudioV1ExecutionStepContext =>
        field ??= new StudioV1ExecutionStepContext(_rawClient, _server, _auth);

    public StudioV1FlowApi StudioV1FlowApi => field ??= new StudioV1FlowApi(_rawClient, _server, _auth);

    public StudioV1Step StudioV1Step => field ??= new StudioV1Step(_rawClient, _server, _auth);

    public StudioV1StepContext StudioV1StepContext =>
        field ??= new StudioV1StepContext(_rawClient, _server, _auth);

    public StudioV2Execution StudioV2Execution => field ??= new StudioV2Execution(_rawClient, _server, _auth);

    public StudioV2ExecutionContext StudioV2ExecutionContext =>
        field ??= new StudioV2ExecutionContext(_rawClient, _server, _auth);

    public StudioV2ExecutionStep StudioV2ExecutionStep =>
        field ??= new StudioV2ExecutionStep(_rawClient, _server, _auth);

    public StudioV2ExecutionStepContext StudioV2ExecutionStepContext =>
        field ??= new StudioV2ExecutionStepContext(_rawClient, _server, _auth);

    public StudioV2FlowApi StudioV2FlowApi => field ??= new StudioV2FlowApi(_rawClient, _server, _auth);

    public StudioV2FlowRevision StudioV2FlowRevision =>
        field ??= new StudioV2FlowRevision(_rawClient, _server, _auth);

    public StudioV2FlowTestUserApi StudioV2FlowTestUserApi =>
        field ??= new StudioV2FlowTestUserApi(_rawClient, _server, _auth);

    public StudioV2FlowValidateApi StudioV2FlowValidateApi =>
        field ??= new StudioV2FlowValidateApi(_rawClient, _server, _auth);

    public SyncV1Document SyncV1Document => field ??= new SyncV1Document(_rawClient, _server, _auth);

    public SyncV1DocumentPermission SyncV1DocumentPermission =>
        field ??= new SyncV1DocumentPermission(_rawClient, _server, _auth);

    public SyncV1ServiceApi SyncV1ServiceApi => field ??= new SyncV1ServiceApi(_rawClient, _server, _auth);

    public SyncV1StreamMessage SyncV1StreamMessage =>
        field ??= new SyncV1StreamMessage(_rawClient, _server, _auth);

    public SyncV1SyncList SyncV1SyncList => field ??= new SyncV1SyncList(_rawClient, _server, _auth);

    public SyncV1SyncListItem SyncV1SyncListItem =>
        field ??= new SyncV1SyncListItem(_rawClient, _server, _auth);

    public SyncV1SyncListPermission SyncV1SyncListPermission =>
        field ??= new SyncV1SyncListPermission(_rawClient, _server, _auth);

    public SyncV1SyncMap SyncV1SyncMap => field ??= new SyncV1SyncMap(_rawClient, _server, _auth);

    public SyncV1SyncMapItem SyncV1SyncMapItem => field ??= new SyncV1SyncMapItem(_rawClient, _server, _auth);

    public SyncV1SyncMapPermission SyncV1SyncMapPermission =>
        field ??= new SyncV1SyncMapPermission(_rawClient, _server, _auth);

    public SyncV1SyncStream SyncV1SyncStream => field ??= new SyncV1SyncStream(_rawClient, _server, _auth);

    public TaskrouterV1Activity TaskrouterV1Activity =>
        field ??= new TaskrouterV1Activity(_rawClient, _server, _auth);

    public TaskrouterV1Event TaskrouterV1Event => field ??= new TaskrouterV1Event(_rawClient, _server, _auth);

    public TaskrouterV1Task TaskrouterV1Task => field ??= new TaskrouterV1Task(_rawClient, _server, _auth);

    public TaskrouterV1TaskChannel TaskrouterV1TaskChannel =>
        field ??= new TaskrouterV1TaskChannel(_rawClient, _server, _auth);

    public TaskrouterV1TaskQueue TaskrouterV1TaskQueue =>
        field ??= new TaskrouterV1TaskQueue(_rawClient, _server, _auth);

    public TaskrouterV1TaskQueueBulkRealTimeStatistics TaskrouterV1TaskQueueBulkRealTimeStatistics =>
        field ??= new TaskrouterV1TaskQueueBulkRealTimeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1TaskQueueCumulativeStatistics TaskrouterV1TaskQueueCumulativeStatistics =>
        field ??= new TaskrouterV1TaskQueueCumulativeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1TaskQueueRealTimeStatistics TaskrouterV1TaskQueueRealTimeStatistics =>
        field ??= new TaskrouterV1TaskQueueRealTimeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1TaskQueueStatistics TaskrouterV1TaskQueueStatistics =>
        field ??= new TaskrouterV1TaskQueueStatistics(_rawClient, _server, _auth);

    public TaskrouterV1TaskQueuesStatistics TaskrouterV1TaskQueuesStatistics =>
        field ??= new TaskrouterV1TaskQueuesStatistics(_rawClient, _server, _auth);

    public TaskrouterV1TaskReservation TaskrouterV1TaskReservation =>
        field ??= new TaskrouterV1TaskReservation(_rawClient, _server, _auth);

    public TaskrouterV1Worker TaskrouterV1Worker =>
        field ??= new TaskrouterV1Worker(_rawClient, _server, _auth);

    public TaskrouterV1WorkerChannel TaskrouterV1WorkerChannel =>
        field ??= new TaskrouterV1WorkerChannel(_rawClient, _server, _auth);

    public TaskrouterV1WorkerReservation TaskrouterV1WorkerReservation =>
        field ??= new TaskrouterV1WorkerReservation(_rawClient, _server, _auth);

    public TaskrouterV1WorkerStatistics TaskrouterV1WorkerStatistics =>
        field ??= new TaskrouterV1WorkerStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkersCumulativeStatistics TaskrouterV1WorkersCumulativeStatistics =>
        field ??= new TaskrouterV1WorkersCumulativeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkersRealTimeStatistics TaskrouterV1WorkersRealTimeStatistics =>
        field ??= new TaskrouterV1WorkersRealTimeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkersStatistics TaskrouterV1WorkersStatistics =>
        field ??= new TaskrouterV1WorkersStatistics(_rawClient, _server, _auth);

    public TaskrouterV1Workflow TaskrouterV1Workflow =>
        field ??= new TaskrouterV1Workflow(_rawClient, _server, _auth);

    public TaskrouterV1WorkflowCumulativeStatistics TaskrouterV1WorkflowCumulativeStatistics =>
        field ??= new TaskrouterV1WorkflowCumulativeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkflowRealTimeStatistics TaskrouterV1WorkflowRealTimeStatistics =>
        field ??= new TaskrouterV1WorkflowRealTimeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkflowStatistics TaskrouterV1WorkflowStatistics =>
        field ??= new TaskrouterV1WorkflowStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkspaceApi TaskrouterV1WorkspaceApi =>
        field ??= new TaskrouterV1WorkspaceApi(_rawClient, _server, _auth);

    public TaskrouterV1WorkspaceCumulativeStatistics TaskrouterV1WorkspaceCumulativeStatistics =>
        field ??= new TaskrouterV1WorkspaceCumulativeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkspaceRealTimeStatistics TaskrouterV1WorkspaceRealTimeStatistics =>
        field ??= new TaskrouterV1WorkspaceRealTimeStatistics(_rawClient, _server, _auth);

    public TaskrouterV1WorkspaceStatistics TaskrouterV1WorkspaceStatistics =>
        field ??= new TaskrouterV1WorkspaceStatistics(_rawClient, _server, _auth);

    public TrusthubV1ComplianceInquiries TrusthubV1ComplianceInquiries =>
        field ??= new TrusthubV1ComplianceInquiries(_rawClient, _server, _auth);

    public TrusthubV1ComplianceRegistrationInquiries TrusthubV1ComplianceRegistrationInquiries =>
        field ??= new TrusthubV1ComplianceRegistrationInquiries(_rawClient, _server, _auth);

    public TrusthubV1ComplianceTollfreeInquiries TrusthubV1ComplianceTollfreeInquiries =>
        field ??= new TrusthubV1ComplianceTollfreeInquiries(_rawClient, _server, _auth);

    public TrusthubV1CustomerProfiles TrusthubV1CustomerProfiles =>
        field ??= new TrusthubV1CustomerProfiles(_rawClient, _server, _auth);

    public TrusthubV1CustomerProfilesChannelEndpointAssignment TrusthubV1CustomerProfilesChannelEndpointAssignment =>
        field ??= new TrusthubV1CustomerProfilesChannelEndpointAssignment(_rawClient, _server, _auth);

    public TrusthubV1CustomerProfilesEntityAssignments TrusthubV1CustomerProfilesEntityAssignments =>
        field ??= new TrusthubV1CustomerProfilesEntityAssignments(_rawClient, _server, _auth);

    public TrusthubV1CustomerProfilesEvaluations TrusthubV1CustomerProfilesEvaluations =>
        field ??= new TrusthubV1CustomerProfilesEvaluations(_rawClient, _server, _auth);

    public TrusthubV1EndUserApi TrusthubV1EndUserApi =>
        field ??= new TrusthubV1EndUserApi(_rawClient, _server, _auth);

    public TrusthubV1EndUserType TrusthubV1EndUserType =>
        field ??= new TrusthubV1EndUserType(_rawClient, _server, _auth);

    public TrusthubV1PoliciesApi TrusthubV1PoliciesApi =>
        field ??= new TrusthubV1PoliciesApi(_rawClient, _server, _auth);

    public TrusthubV1SupportingDocumentApi TrusthubV1SupportingDocumentApi =>
        field ??= new TrusthubV1SupportingDocumentApi(_rawClient, _server, _auth);

    public TrusthubV1SupportingDocumentType TrusthubV1SupportingDocumentType =>
        field ??= new TrusthubV1SupportingDocumentType(_rawClient, _server, _auth);

    public TrusthubV1TrustProducts TrusthubV1TrustProducts =>
        field ??= new TrusthubV1TrustProducts(_rawClient, _server, _auth);

    public TrusthubV1TrustProductsChannelEndpointAssignment TrusthubV1TrustProductsChannelEndpointAssignment =>
        field ??= new TrusthubV1TrustProductsChannelEndpointAssignment(_rawClient, _server, _auth);

    public TrusthubV1TrustProductsEntityAssignments TrusthubV1TrustProductsEntityAssignments =>
        field ??= new TrusthubV1TrustProductsEntityAssignments(_rawClient, _server, _auth);

    public TrusthubV1TrustProductsEvaluations TrusthubV1TrustProductsEvaluations =>
        field ??= new TrusthubV1TrustProductsEvaluations(_rawClient, _server, _auth);

    /// <summary>
    /// Twilio Insights API.
    /// </summary>
    public TwilioInsights TwilioInsights => field ??= new TwilioInsights(_rawClient, _server, _auth);

    public V2ShortCodeApplications V2ShortCodeApplications =>
        field ??= new V2ShortCodeApplications(_rawClient, _server, _auth);

    public VerifyV2AccessToken VerifyV2AccessToken =>
        field ??= new VerifyV2AccessToken(_rawClient, _server, _auth);

    public VerifyV2Bucket VerifyV2Bucket => field ??= new VerifyV2Bucket(_rawClient, _server, _auth);

    public VerifyV2Challenge VerifyV2Challenge => field ??= new VerifyV2Challenge(_rawClient, _server, _auth);

    public VerifyV2Entity VerifyV2Entity => field ??= new VerifyV2Entity(_rawClient, _server, _auth);

    public VerifyV2Factor VerifyV2Factor => field ??= new VerifyV2Factor(_rawClient, _server, _auth);

    public VerifyV2FormApi VerifyV2FormApi => field ??= new VerifyV2FormApi(_rawClient, _server, _auth);

    public VerifyV2MessagingConfiguration VerifyV2MessagingConfiguration =>
        field ??= new VerifyV2MessagingConfiguration(_rawClient, _server, _auth);

    public VerifyV2NewChallenge VerifyV2NewChallenge =>
        field ??= new VerifyV2NewChallenge(_rawClient, _server, _auth);

    public VerifyV2NewFactor VerifyV2NewFactor => field ??= new VerifyV2NewFactor(_rawClient, _server, _auth);

    public VerifyV2Notification VerifyV2Notification =>
        field ??= new VerifyV2Notification(_rawClient, _server, _auth);

    public VerifyV2RateLimit VerifyV2RateLimit => field ??= new VerifyV2RateLimit(_rawClient, _server, _auth);

    public VerifyV2SafelistApi VerifyV2SafelistApi =>
        field ??= new VerifyV2SafelistApi(_rawClient, _server, _auth);

    public VerifyV2ServiceApi VerifyV2ServiceApi =>
        field ??= new VerifyV2ServiceApi(_rawClient, _server, _auth);

    public VerifyV2Template VerifyV2Template => field ??= new VerifyV2Template(_rawClient, _server, _auth);

    public VerifyV2Verification VerifyV2Verification =>
        field ??= new VerifyV2Verification(_rawClient, _server, _auth);

    public VerifyV2VerificationAttemptApi VerifyV2VerificationAttemptApi =>
        field ??= new VerifyV2VerificationAttemptApi(_rawClient, _server, _auth);

    public VerifyV2VerificationAttemptsSummaryApi VerifyV2VerificationAttemptsSummaryApi =>
        field ??= new VerifyV2VerificationAttemptsSummaryApi(_rawClient, _server, _auth);

    public VerifyV2VerificationCheck VerifyV2VerificationCheck =>
        field ??= new VerifyV2VerificationCheck(_rawClient, _server, _auth);

    public VerifyV2Webhook VerifyV2Webhook => field ??= new VerifyV2Webhook(_rawClient, _server, _auth);

    public VideoV1Anonymize VideoV1Anonymize => field ??= new VideoV1Anonymize(_rawClient, _server, _auth);

    public VideoV1CompositionApi VideoV1CompositionApi =>
        field ??= new VideoV1CompositionApi(_rawClient, _server, _auth);

    public VideoV1CompositionHookApi VideoV1CompositionHookApi =>
        field ??= new VideoV1CompositionHookApi(_rawClient, _server, _auth);

    public VideoV1CompositionSettingsApi VideoV1CompositionSettingsApi =>
        field ??= new VideoV1CompositionSettingsApi(_rawClient, _server, _auth);

    public VideoV1Participant VideoV1Participant =>
        field ??= new VideoV1Participant(_rawClient, _server, _auth);

    public VideoV1PublishedTrack VideoV1PublishedTrack =>
        field ??= new VideoV1PublishedTrack(_rawClient, _server, _auth);

    public VideoV1RecordingApi VideoV1RecordingApi =>
        field ??= new VideoV1RecordingApi(_rawClient, _server, _auth);

    public VideoV1RecordingRules VideoV1RecordingRules =>
        field ??= new VideoV1RecordingRules(_rawClient, _server, _auth);

    public VideoV1RecordingSettingsApi VideoV1RecordingSettingsApi =>
        field ??= new VideoV1RecordingSettingsApi(_rawClient, _server, _auth);

    public VideoV1RoomApi VideoV1RoomApi => field ??= new VideoV1RoomApi(_rawClient, _server, _auth);

    public VideoV1RoomRecording VideoV1RoomRecording =>
        field ??= new VideoV1RoomRecording(_rawClient, _server, _auth);

    public VideoV1SubscribeRules VideoV1SubscribeRules =>
        field ??= new VideoV1SubscribeRules(_rawClient, _server, _auth);

    public VideoV1SubscribedTrack VideoV1SubscribedTrack =>
        field ??= new VideoV1SubscribedTrack(_rawClient, _server, _auth);

    public VideoV1Transcriptions VideoV1Transcriptions =>
        field ??= new VideoV1Transcriptions(_rawClient, _server, _auth);

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
