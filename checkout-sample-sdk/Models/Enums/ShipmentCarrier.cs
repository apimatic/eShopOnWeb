using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The carrier for the shipment. Some carriers have a global version as well as local subsidiaries. The subsidiaries are repeated over many countries and might also have an entry in the global list. Choose the carrier for your country. If the carrier is not available for your country, choose the global version of the carrier. If your carrier name is not in the list, set <c>carrier</c> to <c>OTHER</c> and set carrier name in <c>carrier_name_other</c>. For allowed values, see Carriers.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ShipmentCarrier>))]
public sealed record ShipmentCarrier : StringEnum<ShipmentCarrier>
{
    private ShipmentCarrier(string value) : base(value)
    {
    }

    /// <summary>
    /// DPD Russia.
    /// </summary>
    public static readonly ShipmentCarrier DpdRu = new("DPD_RU");

    /// <summary>
    /// Bulgarian Posts.
    /// </summary>
    public static readonly ShipmentCarrier BgBulgarianPost = new("BG_BULGARIAN_POST");

    /// <summary>
    /// Koreapost (www.koreapost.go.kr).
    /// </summary>
    public static readonly ShipmentCarrier KrKoreaPost = new("KR_KOREA_POST");

    /// <summary>
    /// Courier IT.
    /// </summary>
    public static readonly ShipmentCarrier ZaCourierit = new("ZA_COURIERIT");

    /// <summary>
    /// DPD France (formerly exapaq).
    /// </summary>
    public static readonly ShipmentCarrier FrExapaq = new("FR_EXAPAQ");

    /// <summary>
    /// Emirates Post.
    /// </summary>
    public static readonly ShipmentCarrier AreEmiratesPost = new("ARE_EMIRATES_POST");

    /// <summary>
    /// GAC.
    /// </summary>
    public static readonly ShipmentCarrier Gac = new("GAC");

    /// <summary>
    /// Geis CZ.
    /// </summary>
    public static readonly ShipmentCarrier Geis = new("GEIS");

    /// <summary>
    /// SF Express.
    /// </summary>
    public static readonly ShipmentCarrier SfEx = new("SF_EX");

    /// <summary>
    /// Pago Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Pago = new("PAGO");

    /// <summary>
    /// MyHermes UK.
    /// </summary>
    public static readonly ShipmentCarrier Myhermes = new("MYHERMES");

    /// <summary>
    /// Diamond Eurogistics Limited.
    /// </summary>
    public static readonly ShipmentCarrier DiamondEurogistics = new("DIAMOND_EUROGISTICS");

    /// <summary>
    /// Corporate Couriers.
    /// </summary>
    public static readonly ShipmentCarrier CorporatecouriersWebhook = new("CORPORATECOURIERS_WEBHOOK");

    /// <summary>
    /// Bond courier.
    /// </summary>
    public static readonly ShipmentCarrier Bond = new("BOND");

    /// <summary>
    /// Omni Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Omniparcel = new("OMNIPARCEL");

    /// <summary>
    /// Slovenska pošta.
    /// </summary>
    public static readonly ShipmentCarrier SkPosta = new("SK_POSTA");

    /// <summary>
    /// purolator.
    /// </summary>
    public static readonly ShipmentCarrier Purolator = new("PUROLATOR");

    /// <summary>
    /// Mena 360 (Fetchr).
    /// </summary>
    public static readonly ShipmentCarrier FetchrWebhook = new("FETCHR_WEBHOOK");

    /// <summary>
    /// TDG – The Delivery Group.
    /// </summary>
    public static readonly ShipmentCarrier Thedeliverygroup = new("THEDELIVERYGROUP");

    /// <summary>
    /// Cello Square.
    /// </summary>
    public static readonly ShipmentCarrier CelloSquare = new("CELLO_SQUARE");

    /// <summary>
    /// TONDA GLOBAL.
    /// </summary>
    public static readonly ShipmentCarrier Tarrive = new("TARRIVE");

    /// <summary>
    /// MDS Collivery Pty (Ltd).
    /// </summary>
    public static readonly ShipmentCarrier Collivery = new("COLLIVERY");

    /// <summary>
    /// Mainfreight.
    /// </summary>
    public static readonly ShipmentCarrier Mainfreight = new("MAINFREIGHT");

    /// <summary>
    /// First Flight Couriers.
    /// </summary>
    public static readonly ShipmentCarrier IndFirstflight = new("IND_FIRSTFLIGHT");

    /// <summary>
    /// ACS Worldwide Express.
    /// </summary>
    public static readonly ShipmentCarrier Acsworldwide = new("ACSWORLDWIDE");

    /// <summary>
    /// Amstan Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Amstan = new("AMSTAN");

    /// <summary>
    /// OkayParcel.
    /// </summary>
    public static readonly ShipmentCarrier Okayparcel = new("OKAYPARCEL");

    /// <summary>
    /// Envialia Reference.
    /// </summary>
    public static readonly ShipmentCarrier EnvialiaReference = new("ENVIALIA_REFERENCE");

    /// <summary>
    /// Seur Spain.
    /// </summary>
    public static readonly ShipmentCarrier SeurEs = new("SEUR_ES");

    /// <summary>
    /// Continental.
    /// </summary>
    public static readonly ShipmentCarrier Continental = new("CONTINENTAL");

    /// <summary>
    /// FDSEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Fdsexpress = new("FDSEXPRESS");

    /// <summary>
    /// Swiship UK.
    /// </summary>
    public static readonly ShipmentCarrier AmazonFbaSwiship = new("AMAZON_FBA_SWISHIP");

    /// <summary>
    /// Wyngs.
    /// </summary>
    public static readonly ShipmentCarrier Wyngs = new("WYNGS");

    /// <summary>
    /// DHL Active Tracing.
    /// </summary>
    public static readonly ShipmentCarrier DhlActiveTracing = new("DHL_ACTIVE_TRACING");

    /// <summary>
    /// Zyllem.
    /// </summary>
    public static readonly ShipmentCarrier Zyllem = new("ZYLLEM");

    /// <summary>
    /// Ruston.
    /// </summary>
    public static readonly ShipmentCarrier Ruston = new("RUSTON");

    /// <summary>
    /// Xpost.ph.
    /// </summary>
    public static readonly ShipmentCarrier Xpost = new("XPOST");

    /// <summary>
    /// correos Express (www.correos.es).
    /// </summary>
    public static readonly ShipmentCarrier CorreosEs = new("CORREOS_ES");

    /// <summary>
    /// DHL France (www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlFr = new("DHL_FR");

    /// <summary>
    /// Pan-Asia International.
    /// </summary>
    public static readonly ShipmentCarrier PanAsia = new("PAN_ASIA");

    /// <summary>
    /// BRT couriers Italy (www.brt.it).
    /// </summary>
    public static readonly ShipmentCarrier BrtIt = new("BRT_IT");

    /// <summary>
    /// SRE Korea (www.srekorea.co.kr).
    /// </summary>
    public static readonly ShipmentCarrier SreKorea = new("SRE_KOREA");

    /// <summary>
    /// Spee-Dee Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Speedee = new("SPEEDEE");

    /// <summary>
    /// TNT UK Limited (www.tnt.com).
    /// </summary>
    public static readonly ShipmentCarrier TntUk = new("TNT_UK");

    /// <summary>
    /// Venipak.
    /// </summary>
    public static readonly ShipmentCarrier Venipak = new("VENIPAK");

    /// <summary>
    /// SHREE NANDAN COURIER.
    /// </summary>
    public static readonly ShipmentCarrier Shreenandancourier = new("SHREENANDANCOURIER");

    /// <summary>
    /// Croshot.
    /// </summary>
    public static readonly ShipmentCarrier Croshot = new("CROSHOT");

    /// <summary>
    /// NIpost (www.nipost.gov.ng).
    /// </summary>
    public static readonly ShipmentCarrier NipostNg = new("NIPOST_NG");

    /// <summary>
    /// ePost Global.
    /// </summary>
    public static readonly ShipmentCarrier EpstGlbl = new("EPST_GLBL");

    /// <summary>
    /// Newgistics.
    /// </summary>
    public static readonly ShipmentCarrier Newgistics = new("NEWGISTICS");

    /// <summary>
    /// Post of Slovenia.
    /// </summary>
    public static readonly ShipmentCarrier PostSlovenia = new("POST_SLOVENIA");

    /// <summary>
    /// Jersey Post.
    /// </summary>
    public static readonly ShipmentCarrier JerseyPost = new("JERSEY_POST");

    /// <summary>
    /// Bombino Express Pvt.
    /// </summary>
    public static readonly ShipmentCarrier Bombinoexp = new("BOMBINOEXP");

    /// <summary>
    /// WMG Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Wmg = new("WMG");

    /// <summary>
    /// XQ Express.
    /// </summary>
    public static readonly ShipmentCarrier XqExpress = new("XQ_EXPRESS");

    /// <summary>
    /// Furdeco.
    /// </summary>
    public static readonly ShipmentCarrier Furdeco = new("FURDECO");

    /// <summary>
    /// LHT Express.
    /// </summary>
    public static readonly ShipmentCarrier LhtExpress = new("LHT_EXPRESS");

    /// <summary>
    /// South African Post Office.
    /// </summary>
    public static readonly ShipmentCarrier SouthAfricanPostOffice = new("SOUTH_AFRICAN_POST_OFFICE");

    /// <summary>
    /// SPOTON Logistics Pvt Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Spoton = new("SPOTON");

    /// <summary>
    /// Dimerco Express Group.
    /// </summary>
    public static readonly ShipmentCarrier Dimerco = new("DIMERCO");

    /// <summary>
    /// cyprus post.
    /// </summary>
    public static readonly ShipmentCarrier CyprusPostCyp = new("CYPRUS_POST_CYP");

    /// <summary>
    /// AB Custom Group.
    /// </summary>
    public static readonly ShipmentCarrier Abcustom = new("ABCUSTOM");

    /// <summary>
    /// deliverE.
    /// </summary>
    public static readonly ShipmentCarrier IndDelivree = new("IND_DELIVREE");

    /// <summary>
    /// Best Express.
    /// </summary>
    public static readonly ShipmentCarrier CnBestexpress = new("CN_BESTEXPRESS");

    /// <summary>
    /// DX (SFTP).
    /// </summary>
    public static readonly ShipmentCarrier DxSftp = new("DX_SFTP");

    /// <summary>
    /// PICK UPP.
    /// </summary>
    public static readonly ShipmentCarrier PickuppMys = new("PICKUPP_MYS");

    /// <summary>
    /// FMX.
    /// </summary>
    public static readonly ShipmentCarrier Fmx = new("FMX");

    /// <summary>
    /// Hellmann Worldwide Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Hellmann = new("HELLMANN");

    /// <summary>
    /// Ship It Asia.
    /// </summary>
    public static readonly ShipmentCarrier ShipItAsia = new("SHIP_IT_ASIA");

    /// <summary>
    /// Kerry eCommerce.
    /// </summary>
    public static readonly ShipmentCarrier KerryEcommerce = new("KERRY_ECOMMERCE");

    /// <summary>
    /// Frete Rapido.
    /// </summary>
    public static readonly ShipmentCarrier Freterapido = new("FRETERAPIDO");

    /// <summary>
    /// Pitney Bowes.
    /// </summary>
    public static readonly ShipmentCarrier PitneyBowes = new("PITNEY_BOWES");

    /// <summary>
    /// Xpressen courier.
    /// </summary>
    public static readonly ShipmentCarrier XpressenDk = new("XPRESSEN_DK");

    /// <summary>
    /// Spanish Seur API.
    /// </summary>
    public static readonly ShipmentCarrier SeurSpApi = new("SEUR_SP_API");

    /// <summary>
    /// DELIVERYONTIME LOGISTICS PVT LTD.
    /// </summary>
    public static readonly ShipmentCarrier Deliveryontime = new("DELIVERYONTIME");

    /// <summary>
    /// JINSUNG TRADING.
    /// </summary>
    public static readonly ShipmentCarrier Jinsung = new("JINSUNG");

    /// <summary>
    /// Trans Kargo Internasional.
    /// </summary>
    public static readonly ShipmentCarrier TransKargo = new("TRANS_KARGO");

    /// <summary>
    /// Swiship DE.
    /// </summary>
    public static readonly ShipmentCarrier SwishipDe = new("SWISHIP_DE");

    /// <summary>
    /// Ivoy courier.
    /// </summary>
    public static readonly ShipmentCarrier IvoyWebhook = new("IVOY_WEBHOOK");

    /// <summary>
    /// Airmee couriers.
    /// </summary>
    public static readonly ShipmentCarrier AirmeeWebhook = new("AIRMEE_WEBHOOK");

    /// <summary>
    /// dhl benelux.
    /// </summary>
    public static readonly ShipmentCarrier DhlBenelux = new("DHL_BENELUX");

    /// <summary>
    /// FirstMile.
    /// </summary>
    public static readonly ShipmentCarrier Firstmile = new("FIRSTMILE");

    /// <summary>
    /// Fastway Ireland.
    /// </summary>
    public static readonly ShipmentCarrier FastwayIr = new("FASTWAY_IR");

    /// <summary>
    /// Hua Han Logistics.
    /// </summary>
    public static readonly ShipmentCarrier HhExp = new("HH_EXP");

    /// <summary>
    /// Mypostonline.
    /// </summary>
    public static readonly ShipmentCarrier MysMypostOnline = new("MYS_MYPOST_ONLINE");

    /// <summary>
    /// THT Netherland.
    /// </summary>
    public static readonly ShipmentCarrier TntNl = new("TNT_NL");

    /// <summary>
    /// TIPSA courier.
    /// </summary>
    public static readonly ShipmentCarrier Tipsa = new("TIPSA");

    /// <summary>
    /// TAQBIN Malaysia.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinMy = new("TAQBIN_MY");

    /// <summary>
    /// KGM Hub.
    /// </summary>
    public static readonly ShipmentCarrier Kgmhub = new("KGMHUB");

    /// <summary>
    /// Internet Express.
    /// </summary>
    public static readonly ShipmentCarrier Intexpress = new("INTEXPRESS");

    /// <summary>
    /// Overseas Express.
    /// </summary>
    public static readonly ShipmentCarrier OverseExp = new("OVERSE_EXP");

    /// <summary>
    /// One click delivery services.
    /// </summary>
    public static readonly ShipmentCarrier Oneclick = new("ONECLICK");

    /// <summary>
    /// Roadbull Logistics.
    /// </summary>
    public static readonly ShipmentCarrier RoadrunnerFreight = new("ROADRUNNER_FREIGHT");

    /// <summary>
    /// GLS Croatia.
    /// </summary>
    public static readonly ShipmentCarrier GlsCrotia = new("GLS_CROTIA");

    /// <summary>
    /// MRW courier.
    /// </summary>
    public static readonly ShipmentCarrier MrwFtp = new("MRW_FTP");

    /// <summary>
    /// Blue Express.
    /// </summary>
    public static readonly ShipmentCarrier Bluex = new("BLUEX");

    /// <summary>
    /// Daylight Transport.
    /// </summary>
    public static readonly ShipmentCarrier Dylt = new("DYLT");

    /// <summary>
    /// DPD Ireland.
    /// </summary>
    public static readonly ShipmentCarrier DpdIr = new("DPD_IR");

    /// <summary>
    /// Sin Global Express.
    /// </summary>
    public static readonly ShipmentCarrier SinGlbl = new("SIN_GLBL");

    /// <summary>
    /// Tuffnells Parcels Express- Reference.
    /// </summary>
    public static readonly ShipmentCarrier TuffnellsReference = new("TUFFNELLS_REFERENCE");

    /// <summary>
    /// CJ Packet.
    /// </summary>
    public static readonly ShipmentCarrier Cjpacket = new("CJPACKET");

    /// <summary>
    /// Milkman courier.
    /// </summary>
    public static readonly ShipmentCarrier Milkman = new("MILKMAN");

    /// <summary>
    /// ASIGNA courier.
    /// </summary>
    public static readonly ShipmentCarrier Asigna = new("ASIGNA");

    /// <summary>
    /// One World Express.
    /// </summary>
    public static readonly ShipmentCarrier Oneworldexpress = new("ONEWORLDEXPRESS");

    /// <summary>
    /// RoyalShipments.
    /// </summary>
    public static readonly ShipmentCarrier RoyalMail = new("ROYAL_MAIL");

    /// <summary>
    /// Viaxpress.
    /// </summary>
    public static readonly ShipmentCarrier ViaExpress = new("VIA_EXPRESS");

    /// <summary>
    /// TIG Freight.
    /// </summary>
    public static readonly ShipmentCarrier Tigfreight = new("TIGFREIGHT");

    /// <summary>
    /// ZTO Express.
    /// </summary>
    public static readonly ShipmentCarrier ZtoExpress = new("ZTO_EXPRESS");

    /// <summary>
    /// 2GO Courier.
    /// </summary>
    public static readonly ShipmentCarrier TwoGo = new("TWO_GO");

    /// <summary>
    /// IML courier.
    /// </summary>
    public static readonly ShipmentCarrier Iml = new("IML");

    /// <summary>
    /// Intel-Valley Supply chain (ShenZhen) Co. Ltd.
    /// </summary>
    public static readonly ShipmentCarrier IntelValley = new("INTEL_VALLEY");

    /// <summary>
    /// EFS (E-commerce Fulfillment Service).
    /// </summary>
    public static readonly ShipmentCarrier Efs = new("EFS");

    /// <summary>
    /// UK mail (ukmail.com).
    /// </summary>
    public static readonly ShipmentCarrier UkUkMail = new("UK_UK_MAIL");

    /// <summary>
    /// RAM courier.
    /// </summary>
    public static readonly ShipmentCarrier Ram = new("RAM");

    /// <summary>
    /// Allied Express.
    /// </summary>
    public static readonly ShipmentCarrier Alliedexpress = new("ALLIEDEXPRESS");

    /// <summary>
    /// APC overnight (apc-overnight.com).
    /// </summary>
    public static readonly ShipmentCarrier ApcOvernight = new("APC_OVERNIGHT");

    /// <summary>
    /// Shippit.
    /// </summary>
    public static readonly ShipmentCarrier Shippit = new("SHIPPIT");

    /// <summary>
    /// TFM Xpress.
    /// </summary>
    public static readonly ShipmentCarrier Tfm = new("TFM");

    /// <summary>
    /// M Xpress Sdn Bhd.
    /// </summary>
    public static readonly ShipmentCarrier MXpress = new("M_XPRESS");

    /// <summary>
    /// Haidaibao (BOX).
    /// </summary>
    public static readonly ShipmentCarrier HdbBox = new("HDB_BOX");

    /// <summary>
    /// Clevy Links.
    /// </summary>
    public static readonly ShipmentCarrier ClevyLinks = new("CLEVY_LINKS");

    /// <summary>
    /// Beone Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ibeone = new("IBEONE");

    /// <summary>
    /// Fiege Netherlands.
    /// </summary>
    public static readonly ShipmentCarrier FiegeNl = new("FIEGE_NL");

    /// <summary>
    /// KWE Global.
    /// </summary>
    public static readonly ShipmentCarrier KweGlobal = new("KWE_GLOBAL");

    /// <summary>
    /// CTC Express.
    /// </summary>
    public static readonly ShipmentCarrier CtcExpress = new("CTC_EXPRESS");

    /// <summary>
    /// Amazon Shipping.
    /// </summary>
    public static readonly ShipmentCarrier Amazon = new("AMAZON");

    /// <summary>
    /// Morelink.
    /// </summary>
    public static readonly ShipmentCarrier MoreLink = new("MORE_LINK");

    /// <summary>
    /// JX courier.
    /// </summary>
    public static readonly ShipmentCarrier Jx = new("JX");

    /// <summary>
    /// Easy Mail.
    /// </summary>
    public static readonly ShipmentCarrier EasyMail = new("EASY_MAIL");

    /// <summary>
    /// A Duie Pyle.
    /// </summary>
    public static readonly ShipmentCarrier Aduiepyle = new("ADUIEPYLE");

    /// <summary>
    /// Panther.
    /// </summary>
    public static readonly ShipmentCarrier GbPanther = new("GB_PANTHER");

    /// <summary>
    /// Expresssale.
    /// </summary>
    public static readonly ShipmentCarrier Expresssale = new("EXPRESSSALE");

    /// <summary>
    /// Detrack.
    /// </summary>
    public static readonly ShipmentCarrier SgDetrack = new("SG_DETRACK");

    /// <summary>
    /// Trunkrs courier.
    /// </summary>
    public static readonly ShipmentCarrier TrunkrsWebhook = new("TRUNKRS_WEBHOOK");

    /// <summary>
    /// Matdespatch.
    /// </summary>
    public static readonly ShipmentCarrier Matdespatch = new("MATDESPATCH");

    /// <summary>
    /// GLS Logistic Systems Canada Ltd./Dicom.
    /// </summary>
    public static readonly ShipmentCarrier Dicom = new("DICOM");

    /// <summary>
    /// MBW Courier Inc..
    /// </summary>
    public static readonly ShipmentCarrier Mbw = new("MBW");

    /// <summary>
    /// Cambodia Post.
    /// </summary>
    public static readonly ShipmentCarrier KhmCambodiaPost = new("KHM_CAMBODIA_POST");

    /// <summary>
    /// Sinotrans.
    /// </summary>
    public static readonly ShipmentCarrier Sinotrans = new("SINOTRANS");

    /// <summary>
    /// BRT Bartolini(Parcel ID).
    /// </summary>
    public static readonly ShipmentCarrier BrtItParcelid = new("BRT_IT_PARCELID");

    /// <summary>
    /// DHL Supply Chain APAC.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplyChain = new("DHL_SUPPLY_CHAIN");

    /// <summary>
    /// DHL Poland.
    /// </summary>
    public static readonly ShipmentCarrier DhlPl = new("DHL_PL");

    /// <summary>
    /// TopYou.
    /// </summary>
    public static readonly ShipmentCarrier Topyou = new("TOPYOU");

    /// <summary>
    /// PAL Express Limited.
    /// </summary>
    public static readonly ShipmentCarrier Palexpress = new("PALEXPRESS");

    /// <summary>
    /// dhl Singapore.
    /// </summary>
    public static readonly ShipmentCarrier DhlSg = new("DHL_SG");

    /// <summary>
    /// WeDo Logistics.
    /// </summary>
    public static readonly ShipmentCarrier CnWedo = new("CN_WEDO");

    /// <summary>
    /// Fulfillme.
    /// </summary>
    public static readonly ShipmentCarrier Fulfillme = new("FULFILLME");

    /// <summary>
    /// DPD delistrack.
    /// </summary>
    public static readonly ShipmentCarrier DpdDelistrack = new("DPD_DELISTRACK");

    /// <summary>
    /// UPS Reference.
    /// </summary>
    public static readonly ShipmentCarrier UpsReference = new("UPS_REFERENCE");

    /// <summary>
    /// Caribou.
    /// </summary>
    public static readonly ShipmentCarrier Caribou = new("CARIBOU");

    /// <summary>
    /// Locus courier.
    /// </summary>
    public static readonly ShipmentCarrier LocusWebhook = new("LOCUS_WEBHOOK");

    /// <summary>
    /// DSV courier.
    /// </summary>
    public static readonly ShipmentCarrier Dsv = new("DSV");

    /// <summary>
    /// P2P TrakPak.
    /// </summary>
    public static readonly ShipmentCarrier P2PTrc = new("P2P_TRC");

    /// <summary>
    /// Direct Parcels.
    /// </summary>
    public static readonly ShipmentCarrier Directparcels = new("DIRECTPARCELS");

    /// <summary>
    /// Nova Poshta (International).
    /// </summary>
    public static readonly ShipmentCarrier NovaPoshtaInt = new("NOVA_POSHTA_INT");

    /// <summary>
    /// FedEx® Poland Domestic.
    /// </summary>
    public static readonly ShipmentCarrier FedexPoland = new("FEDEX_POLAND");

    /// <summary>
    /// JCEX courier.
    /// </summary>
    public static readonly ShipmentCarrier CnJcex = new("CN_JCEX");

    /// <summary>
    /// FAR international.
    /// </summary>
    public static readonly ShipmentCarrier FarInternational = new("FAR_INTERNATIONAL");

    /// <summary>
    /// IDEX courier.
    /// </summary>
    public static readonly ShipmentCarrier Idexpress = new("IDEXPRESS");

    /// <summary>
    /// GANGBAO Supplychain.
    /// </summary>
    public static readonly ShipmentCarrier Gangbao = new("GANGBAO");

    /// <summary>
    /// Neway Transport.
    /// </summary>
    public static readonly ShipmentCarrier Neway = new("NEWAY");

    /// <summary>
    /// PostNL International.
    /// </summary>
    public static readonly ShipmentCarrier PostnlInt3S = new("POSTNL_INT_3_S");

    /// <summary>
    /// RPX Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier RpxId = new("RPX_ID");

    /// <summary>
    /// Designer Transport.
    /// </summary>
    public static readonly ShipmentCarrier DesignertransportWebhook = new("DESIGNERTRANSPORT_WEBHOOK");

    /// <summary>
    /// GLS Slovenia.
    /// </summary>
    public static readonly ShipmentCarrier GlsSloven = new("GLS_SLOVEN");

    /// <summary>
    /// Parcelled.in.
    /// </summary>
    public static readonly ShipmentCarrier ParcelledIn = new("PARCELLED_IN");

    /// <summary>
    /// GSI EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier GsiExpress = new("GSI_EXPRESS");

    /// <summary>
    /// Con-way Freight.
    /// </summary>
    public static readonly ShipmentCarrier ConWay = new("CON_WAY");

    /// <summary>
    /// Brouwer Transport en Logistiek.
    /// </summary>
    public static readonly ShipmentCarrier BrouwerTransport = new("BROUWER_TRANSPORT");

    /// <summary>
    /// Captain Express International.
    /// </summary>
    public static readonly ShipmentCarrier Cpex = new("CPEX");

    /// <summary>
    /// Israel Post.
    /// </summary>
    public static readonly ShipmentCarrier IsraelPost = new("ISRAEL_POST");

    /// <summary>
    /// DTDC India.
    /// </summary>
    public static readonly ShipmentCarrier DtdcIn = new("DTDC_IN");

    /// <summary>
    /// PTT Post.
    /// </summary>
    public static readonly ShipmentCarrier PttPost = new("PTT_POST");

    /// <summary>
    /// Ximex Delivery Express.
    /// </summary>
    public static readonly ShipmentCarrier XdeWebhook = new("XDE_WEBHOOK");

    /// <summary>
    /// Tolos courier.
    /// </summary>
    public static readonly ShipmentCarrier Tolos = new("TOLOS");

    /// <summary>
    /// Giao hàng nhanh.
    /// </summary>
    public static readonly ShipmentCarrier GiaoHang = new("GIAO_HANG");

    /// <summary>
    /// Geodis E-space.
    /// </summary>
    public static readonly ShipmentCarrier GeodisEspace = new("GEODIS_ESPACE");

    /// <summary>
    /// Magyar Post.
    /// </summary>
    public static readonly ShipmentCarrier MagyarHu = new("MAGYAR_HU");

    /// <summary>
    /// DoorDash.
    /// </summary>
    public static readonly ShipmentCarrier DoordashWebhook = new("DOORDASH_WEBHOOK");

    /// <summary>
    /// Tiki shipment.
    /// </summary>
    public static readonly ShipmentCarrier TikiId = new("TIKI_ID");

    /// <summary>
    /// CJ Logistics International(Hong Kong).
    /// </summary>
    public static readonly ShipmentCarrier CjHkInternational = new("CJ_HK_INTERNATIONAL");

    /// <summary>
    /// Star Track Express.
    /// </summary>
    public static readonly ShipmentCarrier StarTrackExpress = new("STAR_TRACK_EXPRESS");

    /// <summary>
    /// Helthjem.
    /// </summary>
    public static readonly ShipmentCarrier Helthjem = new("HELTHJEM");

    /// <summary>
    /// SF International.
    /// </summary>
    public static readonly ShipmentCarrier Sfb2C = new("SFB2C");

    /// <summary>
    /// Freightquote by C.H. Robinson.
    /// </summary>
    public static readonly ShipmentCarrier Freightquote = new("FREIGHTQUOTE");

    /// <summary>
    /// Landmark Global Reference.
    /// </summary>
    public static readonly ShipmentCarrier LandmarkGlobalReference = new("LANDMARK_GLOBAL_REFERENCE");

    /// <summary>
    /// Parcel2Go.
    /// </summary>
    public static readonly ShipmentCarrier Parcel2Go = new("PARCEL2GO");

    /// <summary>
    /// Delnext.
    /// </summary>
    public static readonly ShipmentCarrier Delnext = new("DELNEXT");

    /// <summary>
    /// Red Carpet Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Rcl = new("RCL");

    /// <summary>
    /// CGS Express.
    /// </summary>
    public static readonly ShipmentCarrier CgsExpress = new("CGS_EXPRESS");

    /// <summary>
    /// Hongkong Post (www.hongkongpost.hk).
    /// </summary>
    public static readonly ShipmentCarrier HkPost = new("HK_POST");

    /// <summary>
    /// SAP EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier SapExpress = new("SAP_EXPRESS");

    /// <summary>
    /// Parcel Post Singapore.
    /// </summary>
    public static readonly ShipmentCarrier ParcelpostSg = new("PARCELPOST_SG");

    /// <summary>
    /// HermesWorld UK.
    /// </summary>
    public static readonly ShipmentCarrier Hermes = new("HERMES");

    /// <summary>
    /// Safexpress.
    /// </summary>
    public static readonly ShipmentCarrier IndSafeexpress = new("IND_SAFEEXPRESS");

    /// <summary>
    /// Tophatter Express.
    /// </summary>
    public static readonly ShipmentCarrier Tophatterexpress = new("TOPHATTEREXPRESS");

    /// <summary>
    /// PT MGLOBAL LOGISTICS INDONESIA.
    /// </summary>
    public static readonly ShipmentCarrier Mglobal = new("MGLOBAL");

    /// <summary>
    /// Averitt Express.
    /// </summary>
    public static readonly ShipmentCarrier Averitt = new("AVERITT");

    /// <summary>
    /// leader.
    /// </summary>
    public static readonly ShipmentCarrier Leader = new("LEADER");

    /// <summary>
    /// 2ebox courier.
    /// </summary>
    public static readonly ShipmentCarrier _2Ebox = new("_2EBOX");

    /// <summary>
    /// Singapore Speedpost.
    /// </summary>
    public static readonly ShipmentCarrier SgSpeedpost = new("SG_SPEEDPOST");

    /// <summary>
    /// DB Schenker (www.dbschenker.com).
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerSe = new("DBSCHENKER_SE");

    /// <summary>
    /// Israel Post Domestic.
    /// </summary>
    public static readonly ShipmentCarrier IsrPostDomestic = new("ISR_POST_DOMESTIC");

    /// <summary>
    /// Best Way Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Bestwayparcel = new("BESTWAYPARCEL");

    /// <summary>
    /// asendia_de.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaDe = new("ASENDIA_DE");

    /// <summary>
    /// nightline_uk.
    /// </summary>
    public static readonly ShipmentCarrier NightlineUk = new("NIGHTLINE_UK");

    /// <summary>
    /// taqbin_sg.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinSg = new("TAQBIN_SG");

    /// <summary>
    /// TCK Express.
    /// </summary>
    public static readonly ShipmentCarrier TckExpress = new("TCK_EXPRESS");

    /// <summary>
    /// Endeavour Delivery.
    /// </summary>
    public static readonly ShipmentCarrier EndeavourDelivery = new("ENDEAVOUR_DELIVERY");

    /// <summary>
    /// Nanjing Woyuan.
    /// </summary>
    public static readonly ShipmentCarrier Nanjingwoyuan = new("NANJINGWOYUAN");

    /// <summary>
    /// Heppner France.
    /// </summary>
    public static readonly ShipmentCarrier HeppnerFr = new("HEPPNER_FR");

    /// <summary>
    /// EMPS Express.
    /// </summary>
    public static readonly ShipmentCarrier EmpsCn = new("EMPS_CN");

    /// <summary>
    /// Fonsen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Fonsen = new("FONSEN");

    /// <summary>
    /// Pickrr.
    /// </summary>
    public static readonly ShipmentCarrier Pickrr = new("PICKRR");

    /// <summary>
    /// APC Overnight Consignment.
    /// </summary>
    public static readonly ShipmentCarrier ApcOvernightConnum = new("APC_OVERNIGHT_CONNUM");

    /// <summary>
    /// Star Track Next Flight.
    /// </summary>
    public static readonly ShipmentCarrier StarTrackNextFlight = new("STAR_TRACK_NEXT_FLIGHT");

    /// <summary>
    /// Shanghai Aqrum Chemical Logistics Co.Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Dajin = new("DAJIN");

    /// <summary>
    /// UPS Freight.
    /// </summary>
    public static readonly ShipmentCarrier UpsFreight = new("UPS_FREIGHT");

    /// <summary>
    /// Posta Plus.
    /// </summary>
    public static readonly ShipmentCarrier PostaPlus = new("POSTA_PLUS");

    /// <summary>
    /// CEVA LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Ceva = new("CEVA");

    /// <summary>
    /// ANSERX courier.
    /// </summary>
    public static readonly ShipmentCarrier Anserx = new("ANSERX");

    /// <summary>
    /// JS EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier JsExpress = new("JS_EXPRESS");

    /// <summary>
    /// padtf.com.
    /// </summary>
    public static readonly ShipmentCarrier Padtf = new("PADTF");

    /// <summary>
    /// UPS Mail Innovations.
    /// </summary>
    public static readonly ShipmentCarrier UpsMailInnovations = new("UPS_MAIL_INNOVATIONS");

    /// <summary>
    /// Sunyou Post.
    /// </summary>
    public static readonly ShipmentCarrier Sypost = new("SYPOST");

    /// <summary>
    /// Amazon Shipping + Amazon MCF.
    /// </summary>
    public static readonly ShipmentCarrier AmazonShipMcf = new("AMAZON_SHIP_MCF");

    /// <summary>
    /// Yusen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Yusen = new("YUSEN");

    /// <summary>
    /// Bring.
    /// </summary>
    public static readonly ShipmentCarrier Bring = new("BRING");

    /// <summary>
    /// SDA Italy.
    /// </summary>
    public static readonly ShipmentCarrier SdaIt = new("SDA_IT");

    /// <summary>
    /// GBA Services Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Gba = new("GBA");

    /// <summary>
    /// Newegg Express.
    /// </summary>
    public static readonly ShipmentCarrier Neweggexpress = new("NEWEGGEXPRESS");

    /// <summary>
    /// Speed Couriers.
    /// </summary>
    public static readonly ShipmentCarrier SpeedcouriersGr = new("SPEEDCOURIERS_GR");

    /// <summary>
    /// forrun Pvt Ltd (Arpatech Venture).
    /// </summary>
    public static readonly ShipmentCarrier Forrun = new("FORRUN");

    /// <summary>
    /// Pickupp.
    /// </summary>
    public static readonly ShipmentCarrier Pickup = new("PICKUP");

    /// <summary>
    /// ECMS International Logistics Co..
    /// </summary>
    public static readonly ShipmentCarrier Ecms = new("ECMS");

    /// <summary>
    /// Intelipost (TMS for LATAM).
    /// </summary>
    public static readonly ShipmentCarrier Intelipost = new("INTELIPOST");

    /// <summary>
    /// Flash Express.
    /// </summary>
    public static readonly ShipmentCarrier Flashexpress = new("FLASHEXPRESS");

    /// <summary>
    /// STO Express.
    /// </summary>
    public static readonly ShipmentCarrier CnSto = new("CN_STO");

    /// <summary>
    /// SEKO Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier SekoSftp = new("SEKO_SFTP");

    /// <summary>
    /// Home Delivery Solutions Ltd.
    /// </summary>
    public static readonly ShipmentCarrier HomeDeliverySolutions = new("HOME_DELIVERY_SOLUTIONS");

    /// <summary>
    /// DPD Hungary.
    /// </summary>
    public static readonly ShipmentCarrier DpdHgry = new("DPD_HGRY");

    /// <summary>
    /// Kerry Express (Vietnam) Co Ltd.
    /// </summary>
    public static readonly ShipmentCarrier KerryttcVn = new("KERRYTTC_VN");

    /// <summary>
    /// Joying Box.
    /// </summary>
    public static readonly ShipmentCarrier JoyingBox = new("JOYING_BOX");

    /// <summary>
    /// Total Express.
    /// </summary>
    public static readonly ShipmentCarrier TotalExpress = new("TOTAL_EXPRESS");

    /// <summary>
    /// ZJS International.
    /// </summary>
    public static readonly ShipmentCarrier ZjsExpress = new("ZJS_EXPRESS");

    /// <summary>
    /// STARKEN couriers.
    /// </summary>
    public static readonly ShipmentCarrier Starken = new("STARKEN");

    /// <summary>
    /// DemandShip.
    /// </summary>
    public static readonly ShipmentCarrier Demandship = new("DEMANDSHIP");

    /// <summary>
    /// DPEX.
    /// </summary>
    public static readonly ShipmentCarrier CnDpex = new("CN_DPEX");

    /// <summary>
    /// AuPost China.
    /// </summary>
    public static readonly ShipmentCarrier AupostCn = new("AUPOST_CN");

    /// <summary>
    /// Logisters.
    /// </summary>
    public static readonly ShipmentCarrier Logisters = new("LOGISTERS");

    /// <summary>
    /// Global Post.
    /// </summary>
    public static readonly ShipmentCarrier Goglobalpost = new("GOGLOBALPOST");

    /// <summary>
    /// GLS Czech Republic.
    /// </summary>
    public static readonly ShipmentCarrier GlsCz = new("GLS_CZ");

    /// <summary>
    /// Paack courier.
    /// </summary>
    public static readonly ShipmentCarrier PaackWebhook = new("PAACK_WEBHOOK");

    /// <summary>
    /// Grab courier.
    /// </summary>
    public static readonly ShipmentCarrier GrabWebhook = new("GRAB_WEBHOOK");

    /// <summary>
    /// Parcelpoint.
    /// </summary>
    public static readonly ShipmentCarrier Parcelpoint = new("PARCELPOINT");

    /// <summary>
    /// iCumulus.
    /// </summary>
    public static readonly ShipmentCarrier Icumulus = new("ICUMULUS");

    /// <summary>
    /// DAI Post.
    /// </summary>
    public static readonly ShipmentCarrier Daiglobaltrack = new("DAIGLOBALTRACK");

    /// <summary>
    /// i-parcel.
    /// </summary>
    public static readonly ShipmentCarrier GlobalIparcel = new("GLOBAL_IPARCEL");

    /// <summary>
    /// Yurtici Kargo.
    /// </summary>
    public static readonly ShipmentCarrier YurticiKargo = new("YURTICI_KARGO");

    /// <summary>
    /// PayPal Package.
    /// </summary>
    public static readonly ShipmentCarrier CnPaypalPackage = new("CN_PAYPAL_PACKAGE");

    /// <summary>
    /// Parcel To Post.
    /// </summary>
    public static readonly ShipmentCarrier Parcel2Post = new("PARCEL_2_POST");

    /// <summary>
    /// GLS Italy.
    /// </summary>
    public static readonly ShipmentCarrier GlsIt = new("GLS_IT");

    /// <summary>
    /// PIL Logistics (China) Co..
    /// </summary>
    public static readonly ShipmentCarrier PilLogistics = new("PIL_LOGISTICS");

    /// <summary>
    /// Heppner Internationale Spedition GmbH &amp; Co..
    /// </summary>
    public static readonly ShipmentCarrier Heppner = new("HEPPNER");

    /// <summary>
    /// Go!Express and logistics.
    /// </summary>
    public static readonly ShipmentCarrier GeneralOvernight = new("GENERAL_OVERNIGHT");

    /// <summary>
    /// Happy 2ThePoint.
    /// </summary>
    public static readonly ShipmentCarrier Happy2Point = new("HAPPY2POINT");

    /// <summary>
    /// Chit Chats.
    /// </summary>
    public static readonly ShipmentCarrier Chitchats = new("CHITCHATS");

    /// <summary>
    /// Smooth Couriers.
    /// </summary>
    public static readonly ShipmentCarrier Smooth = new("SMOOTH");

    /// <summary>
    /// CL E-Logistics Solutions Limited.
    /// </summary>
    public static readonly ShipmentCarrier CleLogistics = new("CLE_LOGISTICS");

    /// <summary>
    /// Fiege Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Fiege = new("FIEGE");

    /// <summary>
    /// M&amp;X cargo.
    /// </summary>
    public static readonly ShipmentCarrier MxCargo = new("MX_CARGO");

    /// <summary>
    /// Ziing Final Mile Inc.
    /// </summary>
    public static readonly ShipmentCarrier Ziingfinalmile = new("ZIINGFINALMILE");

    /// <summary>
    /// Dayton Freight.
    /// </summary>
    public static readonly ShipmentCarrier DaytonFreight = new("DAYTON_FREIGHT");

    /// <summary>
    /// TCS courier.
    /// </summary>
    public static readonly ShipmentCarrier Tcs = new("TCS");

    /// <summary>
    /// AEX Group.
    /// </summary>
    public static readonly ShipmentCarrier Aex = new("AEX");

    /// <summary>
    /// Hermes Germany.
    /// </summary>
    public static readonly ShipmentCarrier HermesDe = new("HERMES_DE");

    /// <summary>
    /// Routific.
    /// </summary>
    public static readonly ShipmentCarrier RoutificWebhook = new("ROUTIFIC_WEBHOOK");

    /// <summary>
    /// Globavend.
    /// </summary>
    public static readonly ShipmentCarrier Globavend = new("GLOBAVEND");

    /// <summary>
    /// CJ Logistics International.
    /// </summary>
    public static readonly ShipmentCarrier CjLogistics = new("CJ_LOGISTICS");

    /// <summary>
    /// The Pallet Network.
    /// </summary>
    public static readonly ShipmentCarrier PalletNetwork = new("PALLET_NETWORK");

    /// <summary>
    /// RAF Philippines.
    /// </summary>
    public static readonly ShipmentCarrier RafPh = new("RAF_PH");

    /// <summary>
    /// XDP Express.
    /// </summary>
    public static readonly ShipmentCarrier UkXdp = new("UK_XDP");

    /// <summary>
    /// Paper Express.
    /// </summary>
    public static readonly ShipmentCarrier PaperExpress = new("PAPER_EXPRESS");

    /// <summary>
    /// La Poste.
    /// </summary>
    public static readonly ShipmentCarrier LaPosteSuivi = new("LA_POSTE_SUIVI");

    /// <summary>
    /// Paquetexpress.
    /// </summary>
    public static readonly ShipmentCarrier Paquetexpress = new("PAQUETEXPRESS");

    /// <summary>
    /// liefery.
    /// </summary>
    public static readonly ShipmentCarrier Liefery = new("LIEFERY");

    /// <summary>
    /// Streck Transport.
    /// </summary>
    public static readonly ShipmentCarrier StreckTransport = new("STRECK_TRANSPORT");

    /// <summary>
    /// Pony express.
    /// </summary>
    public static readonly ShipmentCarrier PonyExpress = new("PONY_EXPRESS");

    /// <summary>
    /// Always Express.
    /// </summary>
    public static readonly ShipmentCarrier AlwaysExpress = new("ALWAYS_EXPRESS");

    /// <summary>
    /// GBS-Broker.
    /// </summary>
    public static readonly ShipmentCarrier GbsBroker = new("GBS_BROKER");

    /// <summary>
    /// City-Link Express.
    /// </summary>
    public static readonly ShipmentCarrier CitylinkMy = new("CITYLINK_MY");

    /// <summary>
    /// ALLJOY SUPPLY CHAIN.
    /// </summary>
    public static readonly ShipmentCarrier Alljoy = new("ALLJOY");

    /// <summary>
    /// yodel.
    /// </summary>
    public static readonly ShipmentCarrier Yodel = new("YODEL");

    /// <summary>
    /// Yodel Direct.
    /// </summary>
    public static readonly ShipmentCarrier YodelDir = new("YODEL_DIR");

    /// <summary>
    /// STONE3PL.
    /// </summary>
    public static readonly ShipmentCarrier Stone3Pl = new("STONE3PL");

    /// <summary>
    /// ParcelPal.
    /// </summary>
    public static readonly ShipmentCarrier ParcelpalWebhook = new("PARCELPAL_WEBHOOK");

    /// <summary>
    /// DHL eCommerce Asia (API).
    /// </summary>
    public static readonly ShipmentCarrier DhlEcomerceAsa = new("DHL_ECOMERCE_ASA");

    /// <summary>
    /// J&amp;T Express Singapore.
    /// </summary>
    public static readonly ShipmentCarrier Simplypost = new("SIMPLYPOST");

    /// <summary>
    /// Kua Yue Express.
    /// </summary>
    public static readonly ShipmentCarrier KyExpress = new("KY_EXPRESS");

    /// <summary>
    /// shenzhen 1st International Logistics(Group)Co.
    /// </summary>
    public static readonly ShipmentCarrier Shenzhen = new("SHENZHEN");

    /// <summary>
    /// LaserShip.
    /// </summary>
    public static readonly ShipmentCarrier UsLasership = new("US_LASERSHIP");

    /// <summary>
    /// ucexpress.
    /// </summary>
    public static readonly ShipmentCarrier UcExpre = new("UC_EXPRE");

    /// <summary>
    /// DIDADI Logistics tech.
    /// </summary>
    public static readonly ShipmentCarrier Didadi = new("DIDADI");

    /// <summary>
    /// CJ Korea Express.
    /// </summary>
    public static readonly ShipmentCarrier CjKr = new("CJ_KR");

    /// <summary>
    /// DB Schenker B2B.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerB2B = new("DBSCHENKER_B2B");

    /// <summary>
    /// MXE Express.
    /// </summary>
    public static readonly ShipmentCarrier Mxe = new("MXE");

    /// <summary>
    /// CAE Delivers.
    /// </summary>
    public static readonly ShipmentCarrier CaeDelivers = new("CAE_DELIVERS");

    /// <summary>
    /// PFC Express.
    /// </summary>
    public static readonly ShipmentCarrier Pfcexpress = new("PFCEXPRESS");

    /// <summary>
    /// Whistl.
    /// </summary>
    public static readonly ShipmentCarrier Whistl = new("WHISTL");

    /// <summary>
    /// WePost Sdn Bhd.
    /// </summary>
    public static readonly ShipmentCarrier Wepost = new("WEPOST");

    /// <summary>
    /// DHL parcel Spain(www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlParcelEs = new("DHL_PARCEL_ES");

    /// <summary>
    /// DD Express Courier.
    /// </summary>
    public static readonly ShipmentCarrier Ddexpress = new("DDEXPRESS");

    /// <summary>
    /// Aramex Australia (formerly Fastway AU).
    /// </summary>
    public static readonly ShipmentCarrier AramexAu = new("ARAMEX_AU");

    /// <summary>
    /// Bneed courier.
    /// </summary>
    public static readonly ShipmentCarrier Bneed = new("BNEED");

    /// <summary>
    /// Kerry Express Hong Kong.
    /// </summary>
    public static readonly ShipmentCarrier HkTgx = new("HK_TGX");

    /// <summary>
    /// Latvijas Pasts.
    /// </summary>
    public static readonly ShipmentCarrier LatvijasPasts = new("LATVIJAS_PASTS");

    /// <summary>
    /// ViaEurope.
    /// </summary>
    public static readonly ShipmentCarrier Viaeurope = new("VIAEUROPE");

    /// <summary>
    /// Correo Uruguayo.
    /// </summary>
    public static readonly ShipmentCarrier CorreoUy = new("CORREO_UY");

    /// <summary>
    /// Chronopost france (www.chronopost.fr).
    /// </summary>
    public static readonly ShipmentCarrier ChronopostFr = new("CHRONOPOST_FR");

    /// <summary>
    /// J-Net.
    /// </summary>
    public static readonly ShipmentCarrier JNet = new("J_NET");

    /// <summary>
    /// 6ls.com.
    /// </summary>
    public static readonly ShipmentCarrier _6Ls = new("_6LS");

    /// <summary>
    /// Belpost.
    /// </summary>
    public static readonly ShipmentCarrier BlrBelpost = new("BLR_BELPOST");

    /// <summary>
    /// BirdSystem.
    /// </summary>
    public static readonly ShipmentCarrier Birdsystem = new("BIRDSYSTEM");

    /// <summary>
    /// DobroPost.
    /// </summary>
    public static readonly ShipmentCarrier Dobropost = new("DOBROPOST");

    /// <summary>
    /// Wahana express (www.wahana.com).
    /// </summary>
    public static readonly ShipmentCarrier WahanaId = new("WAHANA_ID");

    /// <summary>
    /// Weaship.
    /// </summary>
    public static readonly ShipmentCarrier Weaship = new("WEASHIP");

    /// <summary>
    /// Sonic Transportation &amp; Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Sonictl = new("SONICTL");

    /// <summary>
    /// Shenzhen Jinghuada Logistics Co..
    /// </summary>
    public static readonly ShipmentCarrier Kwt = new("KWT");

    /// <summary>
    /// AFL LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier AfllogFtp = new("AFLLOG_FTP");

    /// <summary>
    /// SkyNet Worldwide Express.
    /// </summary>
    public static readonly ShipmentCarrier SkynetWorldwide = new("SKYNET_WORLDWIDE");

    /// <summary>
    /// Nova Poshta (novaposhta.ua).
    /// </summary>
    public static readonly ShipmentCarrier NovaPoshta = new("NOVA_POSHTA");

    /// <summary>
    /// Seino.
    /// </summary>
    public static readonly ShipmentCarrier Seino = new("SEINO");

    /// <summary>
    /// SZENDEX.
    /// </summary>
    public static readonly ShipmentCarrier Szendex = new("SZENDEX");

    /// <summary>
    /// Bpost international.
    /// </summary>
    public static readonly ShipmentCarrier BpostInt = new("BPOST_INT");

    /// <summary>
    /// DB Schenker Sweden.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerSv = new("DBSCHENKER_SV");

    /// <summary>
    /// AO Deutschland.
    /// </summary>
    public static readonly ShipmentCarrier AoDeutschland = new("AO_DEUTSCHLAND");

    /// <summary>
    /// EU Fleet Solutions.
    /// </summary>
    public static readonly ShipmentCarrier EuFleetSolutions = new("EU_FLEET_SOLUTIONS");

    /// <summary>
    /// PCF Final Mile.
    /// </summary>
    public static readonly ShipmentCarrier Pcfcorp = new("PCFCORP");

    /// <summary>
    /// Link Bridge(BeiJing)international logistics co..
    /// </summary>
    public static readonly ShipmentCarrier Linkbridge = new("LINKBRIDGE");

    /// <summary>
    /// PT Prima Multi Cipta.
    /// </summary>
    public static readonly ShipmentCarrier Primamulticipta = new("PRIMAMULTICIPTA");

    /// <summary>
    /// Urbanfox.
    /// </summary>
    public static readonly ShipmentCarrier Courex = new("COUREX");

    /// <summary>
    /// Zajil Express Company.
    /// </summary>
    public static readonly ShipmentCarrier ZajilExpress = new("ZAJIL_EXPRESS");

    /// <summary>
    /// CollectCo.
    /// </summary>
    public static readonly ShipmentCarrier Collectco = new("COLLECTCO");

    /// <summary>
    /// J&amp;T EXPRESS MALAYSIA.
    /// </summary>
    public static readonly ShipmentCarrier Jtexpress = new("JTEXPRESS");

    /// <summary>
    /// FedEx® UK.
    /// </summary>
    public static readonly ShipmentCarrier FedexUk = new("FEDEX_UK");

    /// <summary>
    /// uShip courier.
    /// </summary>
    public static readonly ShipmentCarrier Uship = new("USHIP");

    /// <summary>
    /// PIXSELL LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Pixsell = new("PIXSELL");

    /// <summary>
    /// Shiptor.
    /// </summary>
    public static readonly ShipmentCarrier Shiptor = new("SHIPTOR");

    /// <summary>
    /// CDEK courier.
    /// </summary>
    public static readonly ShipmentCarrier Cdek = new("CDEK");

    /// <summary>
    /// ViettelPost.
    /// </summary>
    public static readonly ShipmentCarrier VnmViettelpost = new("VNM_VIETTELPOST");

    /// <summary>
    /// CJ Century.
    /// </summary>
    public static readonly ShipmentCarrier CjCentury = new("CJ_CENTURY");

    /// <summary>
    /// GSO(GLS-USA).
    /// </summary>
    public static readonly ShipmentCarrier Gso = new("GSO");

    /// <summary>
    /// VIWO IoT.
    /// </summary>
    public static readonly ShipmentCarrier Viwo = new("VIWO");

    /// <summary>
    /// SKYBOX.
    /// </summary>
    public static readonly ShipmentCarrier Skybox = new("SKYBOX");

    /// <summary>
    /// Kerry TJ Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Kerrytj = new("KERRYTJ");

    /// <summary>
    /// Nhat Tin Logistics.
    /// </summary>
    public static readonly ShipmentCarrier NtlogisticsVn = new("NTLOGISTICS_VN");

    /// <summary>
    /// lightning monkey.
    /// </summary>
    public static readonly ShipmentCarrier SdhScm = new("SDH_SCM");

    /// <summary>
    /// Zinc courier.
    /// </summary>
    public static readonly ShipmentCarrier Zinc = new("ZINC");

    /// <summary>
    /// DPE South Africa.
    /// </summary>
    public static readonly ShipmentCarrier DpeSouthAfrc = new("DPE_SOUTH_AFRC");

    /// <summary>
    /// Czech Post.
    /// </summary>
    public static readonly ShipmentCarrier CeskaCz = new("CESKA_CZ");

    /// <summary>
    /// ACS Courier.
    /// </summary>
    public static readonly ShipmentCarrier AcsGr = new("ACS_GR");

    /// <summary>
    /// DealerSend.
    /// </summary>
    public static readonly ShipmentCarrier Dealersend = new("DEALERSEND");

    /// <summary>
    /// Jocom.
    /// </summary>
    public static readonly ShipmentCarrier Jocom = new("JOCOM");

    /// <summary>
    /// CSE courier.
    /// </summary>
    public static readonly ShipmentCarrier Cse = new("CSE");

    /// <summary>
    /// TForce Final Mile.
    /// </summary>
    public static readonly ShipmentCarrier TforceFinalmile = new("TFORCE_FINALMILE");

    /// <summary>
    /// ShipGate.
    /// </summary>
    public static readonly ShipmentCarrier ShipGate = new("SHIP_GATE");

    /// <summary>
    /// SHIPTER.
    /// </summary>
    public static readonly ShipmentCarrier Shipter = new("SHIPTER");

    /// <summary>
    /// National Sameday.
    /// </summary>
    public static readonly ShipmentCarrier NationalSameday = new("NATIONAL_SAMEDAY");

    /// <summary>
    /// YunExpress.
    /// </summary>
    public static readonly ShipmentCarrier Yunexpress = new("YUNEXPRESS");

    /// <summary>
    /// AliExpress Standard Shipping.
    /// </summary>
    public static readonly ShipmentCarrier Cainiao = new("CAINIAO");

    /// <summary>
    /// DMSMatrix.
    /// </summary>
    public static readonly ShipmentCarrier DmsMatrix = new("DMS_MATRIX");

    /// <summary>
    /// Directlog (www.directlog.com.br).
    /// </summary>
    public static readonly ShipmentCarrier Directlog = new("DIRECTLOG");

    /// <summary>
    /// Asendia USA.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaUs = new("ASENDIA_US");

    /// <summary>
    /// 3JMS Logistics.
    /// </summary>
    public static readonly ShipmentCarrier _3Jmslogistics = new("_3JMSLOGISTICS");

    /// <summary>
    /// LICCARDI EXPRESS COURIER.
    /// </summary>
    public static readonly ShipmentCarrier LiccardiExpress = new("LICCARDI_EXPRESS");

    /// <summary>
    /// SkyPostal.
    /// </summary>
    public static readonly ShipmentCarrier SkyPostal = new("SKY_POSTAL");

    /// <summary>
    /// cnwangtong.
    /// </summary>
    public static readonly ShipmentCarrier Cnwangtong = new("CNWANGTONG");

    /// <summary>
    /// ostnord denmark.
    /// </summary>
    public static readonly ShipmentCarrier PostnordLogisticsDk = new("POSTNORD_LOGISTICS_DK");

    /// <summary>
    /// Logistika.
    /// </summary>
    public static readonly ShipmentCarrier Logistika = new("LOGISTIKA");

    /// <summary>
    /// Celeritas Transporte.
    /// </summary>
    public static readonly ShipmentCarrier Celeritas = new("CELERITAS");

    /// <summary>
    /// Pressio.
    /// </summary>
    public static readonly ShipmentCarrier Pressiode = new("PRESSIODE");

    /// <summary>
    /// Shree Maruti Courier Services Pvt Ltd.
    /// </summary>
    public static readonly ShipmentCarrier ShreeMaruti = new("SHREE_MARUTI");

    /// <summary>
    /// Logistic Worldwide Express (LWE Honkong).
    /// </summary>
    public static readonly ShipmentCarrier LogisticsworldwideHk = new("LOGISTICSWORLDWIDE_HK");

    /// <summary>
    /// eFEx (E-Commerce Fulfillment &amp; Express).
    /// </summary>
    public static readonly ShipmentCarrier Efex = new("EFEX");

    /// <summary>
    /// Lotte Global Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Lotte = new("LOTTE");

    /// <summary>
    /// Lone Star Overnight.
    /// </summary>
    public static readonly ShipmentCarrier Lonestar = new("LONESTAR");

    /// <summary>
    /// Aprisa Express.
    /// </summary>
    public static readonly ShipmentCarrier Aprisaexpress = new("APRISAEXPRESS");

    /// <summary>
    /// BEL North Russia.
    /// </summary>
    public static readonly ShipmentCarrier BelRs = new("BEL_RS");

    /// <summary>
    /// OSM Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier OsmWorldwide = new("OSM_WORLDWIDE");

    /// <summary>
    /// Westgate Global.
    /// </summary>
    public static readonly ShipmentCarrier WestgateGl = new("WESTGATE_GL");

    /// <summary>
    /// Fasttrack.
    /// </summary>
    public static readonly ShipmentCarrier Fastrack = new("FASTRACK");

    /// <summary>
    /// DTD Express.
    /// </summary>
    public static readonly ShipmentCarrier DtdExpr = new("DTD_EXPR");

    /// <summary>
    /// AlfaTrex.
    /// </summary>
    public static readonly ShipmentCarrier Alfatrex = new("ALFATREX");

    /// <summary>
    /// ProMed Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Promeddelivery = new("PROMEDDELIVERY");

    /// <summary>
    /// Thabit Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ThabitLogistics = new("THABIT_LOGISTICS");

    /// <summary>
    /// HCT LOGISTICS CO.LTD..
    /// </summary>
    public static readonly ShipmentCarrier HctLogistics = new("HCT_LOGISTICS");

    /// <summary>
    /// Carry-Flap Co..
    /// </summary>
    public static readonly ShipmentCarrier CarryFlap = new("CARRY_FLAP");

    /// <summary>
    /// Old Dominion Freight Line.
    /// </summary>
    public static readonly ShipmentCarrier UsOldDominion = new("US_OLD_DOMINION");

    /// <summary>
    /// ANICAM BOX EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier AnicamBox = new("ANICAM_BOX");

    /// <summary>
    /// WanbExpress.
    /// </summary>
    public static readonly ShipmentCarrier Wanbexpress = new("WANBEXPRESS");

    /// <summary>
    /// An Post.
    /// </summary>
    public static readonly ShipmentCarrier AnPost = new("AN_POST");

    /// <summary>
    /// DPD Local.
    /// </summary>
    public static readonly ShipmentCarrier DpdLocal = new("DPD_LOCAL");

    /// <summary>
    /// Stallion Express.
    /// </summary>
    public static readonly ShipmentCarrier Stallionexpress = new("STALLIONEXPRESS");

    /// <summary>
    /// RaidereX.
    /// </summary>
    public static readonly ShipmentCarrier Raiderex = new("RAIDEREX");

    /// <summary>
    /// ShopfansRU LLC.
    /// </summary>
    public static readonly ShipmentCarrier Shopfans = new("SHOPFANS");

    /// <summary>
    /// Kyungdong Parcel.
    /// </summary>
    public static readonly ShipmentCarrier KyungdongParcel = new("KYUNGDONG_PARCEL");

    /// <summary>
    /// Champion Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ChampionLogistics = new("CHAMPION_LOGISTICS");

    /// <summary>
    /// PICK UPP (Singapore).
    /// </summary>
    public static readonly ShipmentCarrier PickuppSgp = new("PICKUPP_SGP");

    /// <summary>
    /// Morning Express.
    /// </summary>
    public static readonly ShipmentCarrier MorningExpress = new("MORNING_EXPRESS");

    /// <summary>
    /// NACEX.
    /// </summary>
    public static readonly ShipmentCarrier Nacex = new("NACEX");

    /// <summary>
    /// SortHub courier.
    /// </summary>
    public static readonly ShipmentCarrier ThenileWebhook = new("THENILE_WEBHOOK");

    /// <summary>
    /// Holisol.
    /// </summary>
    public static readonly ShipmentCarrier Holisol = new("HOLISOL");

    /// <summary>
    /// LBC EXPRESS INC..
    /// </summary>
    public static readonly ShipmentCarrier LbcexpressFtp = new("LBCEXPRESS_FTP");

    /// <summary>
    /// KURASI.
    /// </summary>
    public static readonly ShipmentCarrier Kurasi = new("KURASI");

    /// <summary>
    /// USF Reddaway.
    /// </summary>
    public static readonly ShipmentCarrier UsfReddaway = new("USF_REDDAWAY");

    /// <summary>
    /// APG eCommerce Solutions.
    /// </summary>
    public static readonly ShipmentCarrier Apg = new("APG");

    /// <summary>
    /// BoxC courier.
    /// </summary>
    public static readonly ShipmentCarrier CnBoxc = new("CN_BOXC");

    /// <summary>
    /// ECOSCOOTING.
    /// </summary>
    public static readonly ShipmentCarrier Ecoscooting = new("ECOSCOOTING");

    /// <summary>
    /// Mainway.
    /// </summary>
    public static readonly ShipmentCarrier Mainway = new("MAINWAY");

    /// <summary>
    /// Paperfly Private Limited.
    /// </summary>
    public static readonly ShipmentCarrier Paperfly = new("PAPERFLY");

    /// <summary>
    /// Hound Express.
    /// </summary>
    public static readonly ShipmentCarrier Houndexpress = new("HOUNDEXPRESS");

    /// <summary>
    /// Boxberry courier.
    /// </summary>
    public static readonly ShipmentCarrier BoxBerry = new("BOX_BERRY");

    /// <summary>
    /// EP-Box courier.
    /// </summary>
    public static readonly ShipmentCarrier EpBox = new("EP_BOX");

    /// <summary>
    /// Plus UK Logistics.
    /// </summary>
    public static readonly ShipmentCarrier PlusLogUk = new("PLUS_LOG_UK");

    /// <summary>
    /// Fulfilla.
    /// </summary>
    public static readonly ShipmentCarrier Fulfilla = new("FULFILLA");

    /// <summary>
    /// ASE KARGO.
    /// </summary>
    public static readonly ShipmentCarrier Ase = new("ASE");

    /// <summary>
    /// MailPlus.
    /// </summary>
    public static readonly ShipmentCarrier MailPlus = new("MAIL_PLUS");

    /// <summary>
    /// XPO logistics.
    /// </summary>
    public static readonly ShipmentCarrier XpoLogistics = new("XPO_LOGISTICS");

    /// <summary>
    /// wnDirect.
    /// </summary>
    public static readonly ShipmentCarrier Wndirect = new("WNDIRECT");

    /// <summary>
    /// Cloudwish Asia.
    /// </summary>
    public static readonly ShipmentCarrier CloudwishAsia = new("CLOUDWISH_ASIA");

    /// <summary>
    /// Zeleris.
    /// </summary>
    public static readonly ShipmentCarrier Zeleris = new("ZELERIS");

    /// <summary>
    /// Gio Express.
    /// </summary>
    public static readonly ShipmentCarrier GioExpress = new("GIO_EXPRESS");

    /// <summary>
    /// OCS WORLDWIDE.
    /// </summary>
    public static readonly ShipmentCarrier OcsWorldwide = new("OCS_WORLDWIDE");

    /// <summary>
    /// ARK Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ArkLogistics = new("ARK_LOGISTICS");

    /// <summary>
    /// Aquiline.
    /// </summary>
    public static readonly ShipmentCarrier Aquiline = new("AQUILINE");

    /// <summary>
    /// Pilot Freight Services.
    /// </summary>
    public static readonly ShipmentCarrier PilotFreight = new("PILOT_FREIGHT");

    /// <summary>
    /// Qwintry Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Qwintry = new("QWINTRY");

    /// <summary>
    /// Danske Fragtaend.
    /// </summary>
    public static readonly ShipmentCarrier DanskeFragt = new("DANSKE_FRAGT");

    /// <summary>
    /// Carriers courier.
    /// </summary>
    public static readonly ShipmentCarrier Carriers = new("CARRIERS");

    /// <summary>
    /// Rivo (Air canada).
    /// </summary>
    public static readonly ShipmentCarrier AirCanadaGlobal = new("AIR_CANADA_GLOBAL");

    /// <summary>
    /// PRESIDENT TRANSNET CORP.
    /// </summary>
    public static readonly ShipmentCarrier PresidentTrans = new("PRESIDENT_TRANS");

    /// <summary>
    /// STEP FORWARD FREIGHT SERVICE CO LTD.
    /// </summary>
    public static readonly ShipmentCarrier Stepforwardfs = new("STEPFORWARDFS");

    /// <summary>
    /// Skynet UK.
    /// </summary>
    public static readonly ShipmentCarrier SkynetUk = new("SKYNET_UK");

    /// <summary>
    /// PITT OHIO.
    /// </summary>
    public static readonly ShipmentCarrier Pittohio = new("PITTOHIO");

    /// <summary>
    /// Correos Express.
    /// </summary>
    public static readonly ShipmentCarrier CorreosExpress = new("CORREOS_EXPRESS");

    /// <summary>
    /// RL Carriers.
    /// </summary>
    public static readonly ShipmentCarrier RlUs = new("RL_US");

    /// <summary>
    /// Destiny Transportation.
    /// </summary>
    public static readonly ShipmentCarrier Destiny = new("DESTINY");

    /// <summary>
    /// Yodel (www.yodel.co.uk).
    /// </summary>
    public static readonly ShipmentCarrier UkYodel = new("UK_YODEL");

    /// <summary>
    /// CometTech.
    /// </summary>
    public static readonly ShipmentCarrier CometTech = new("COMET_TECH");

    /// <summary>
    /// DHL Parcel Russia.
    /// </summary>
    public static readonly ShipmentCarrier DhlParcelRu = new("DHL_PARCEL_RU");

    /// <summary>
    /// TNT Reference.
    /// </summary>
    public static readonly ShipmentCarrier TntRefr = new("TNT_REFR");

    /// <summary>
    /// Shree Anjani Courier.
    /// </summary>
    public static readonly ShipmentCarrier ShreeAnjaniCourier = new("SHREE_ANJANI_COURIER");

    /// <summary>
    /// Mikropakket Belgium.
    /// </summary>
    public static readonly ShipmentCarrier MikropakketBe = new("MIKROPAKKET_BE");

    /// <summary>
    /// RETS express.
    /// </summary>
    public static readonly ShipmentCarrier EtsExpress = new("ETS_EXPRESS");

    /// <summary>
    /// Colis Privé.
    /// </summary>
    public static readonly ShipmentCarrier ColisPrive = new("COLIS_PRIVE");

    /// <summary>
    /// Yunda Express.
    /// </summary>
    public static readonly ShipmentCarrier CnYunda = new("CN_YUNDA");

    /// <summary>
    /// AAA Cooper.
    /// </summary>
    public static readonly ShipmentCarrier AaaCooper = new("AAA_COOPER");

    /// <summary>
    /// Rocket Parcel International.
    /// </summary>
    public static readonly ShipmentCarrier RocketParcel = new("ROCKET_PARCEL");

    /// <summary>
    /// 360 Lion Express.
    /// </summary>
    public static readonly ShipmentCarrier _360Lion = new("_360LION");

    /// <summary>
    /// PANDU.
    /// </summary>
    public static readonly ShipmentCarrier Pandu = new("PANDU");

    /// <summary>
    /// PROFESSIONAL COURIERS.
    /// </summary>
    public static readonly ShipmentCarrier ProfessionalCouriers = new("PROFESSIONAL_COURIERS");

    /// <summary>
    /// FLYTEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Flytexpress = new("FLYTEXPRESS");

    /// <summary>
    /// LOGISTICSWORLDWIDE MY.
    /// </summary>
    public static readonly ShipmentCarrier LogisticsworldwideMy = new("LOGISTICSWORLDWIDE_MY");

    /// <summary>
    /// CORREOS DE ESPANA.
    /// </summary>
    public static readonly ShipmentCarrier CorreosDeEspana = new("CORREOS_DE_ESPANA");

    /// <summary>
    /// IMX.
    /// </summary>
    public static readonly ShipmentCarrier Imx = new("IMX");

    /// <summary>
    /// FOUR PX EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier FourPxExpress = new("FOUR_PX_EXPRESS");

    /// <summary>
    /// XPRESSBEES.
    /// </summary>
    public static readonly ShipmentCarrier Xpressbees = new("XPRESSBEES");

    /// <summary>
    /// pickupp_vnm.
    /// </summary>
    public static readonly ShipmentCarrier PickuppVnm = new("PICKUPP_VNM");

    /// <summary>
    /// startrack_express.
    /// </summary>
    public static readonly ShipmentCarrier StartrackExpress = new("STARTRACK_EXPRESS");

    /// <summary>
    /// fr_colissimo.
    /// </summary>
    public static readonly ShipmentCarrier FrColissimo = new("FR_COLISSIMO");

    /// <summary>
    /// nacex_spain_reference.
    /// </summary>
    public static readonly ShipmentCarrier NacexSpainReference = new("NACEX_SPAIN_REFERENCE");

    /// <summary>
    /// dhl_supply_chain_au.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplyChainAu = new("DHL_SUPPLY_CHAIN_AU");

    /// <summary>
    /// Eshipping.
    /// </summary>
    public static readonly ShipmentCarrier Eshipping = new("ESHIPPING");

    /// <summary>
    /// SHREE TIRUPATI COURIER SERVICES PVT. LTD..
    /// </summary>
    public static readonly ShipmentCarrier Shreetirupati = new("SHREETIRUPATI");

    /// <summary>
    /// HX Express.
    /// </summary>
    public static readonly ShipmentCarrier HxExpress = new("HX_EXPRESS");

    /// <summary>
    /// INDOPAKET.
    /// </summary>
    public static readonly ShipmentCarrier Indopaket = new("INDOPAKET");

    /// <summary>
    /// 17 Post Service.
    /// </summary>
    public static readonly ShipmentCarrier Cn17Post = new("CN_17POST");

    /// <summary>
    /// K1 Express.
    /// </summary>
    public static readonly ShipmentCarrier K1Express = new("K1_EXPRESS");

    /// <summary>
    /// CJ GLS.
    /// </summary>
    public static readonly ShipmentCarrier CjGls = new("CJ_GLS");

    /// <summary>
    /// GDEX courier.
    /// </summary>
    public static readonly ShipmentCarrier MysGdex = new("MYS_GDEX");

    /// <summary>
    /// Nationex courier.
    /// </summary>
    public static readonly ShipmentCarrier Nationex = new("NATIONEX");

    /// <summary>
    /// Anjun couriers.
    /// </summary>
    public static readonly ShipmentCarrier Anjun = new("ANJUN");

    /// <summary>
    /// FarGood.
    /// </summary>
    public static readonly ShipmentCarrier Fargood = new("FARGOOD");

    /// <summary>
    /// SMG Direct.
    /// </summary>
    public static readonly ShipmentCarrier SmgExpress = new("SMG_EXPRESS");

    /// <summary>
    /// RZY Express.
    /// </summary>
    public static readonly ShipmentCarrier Rzyexpress = new("RZYEXPRESS");

    /// <summary>
    /// Southeastern Freight Lines.
    /// </summary>
    public static readonly ShipmentCarrier Sefl = new("SEFL");

    /// <summary>
    /// TNT-Click Italy.
    /// </summary>
    public static readonly ShipmentCarrier TntClickIt = new("TNT_CLICK_IT");

    /// <summary>
    /// Haidaibao.
    /// </summary>
    public static readonly ShipmentCarrier Hdb = new("HDB");

    /// <summary>
    /// Hipshipper.
    /// </summary>
    public static readonly ShipmentCarrier Hipshipper = new("HIPSHIPPER");

    /// <summary>
    /// RPX Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Rpxlogistics = new("RPXLOGISTICS");

    /// <summary>
    /// Kuehne + Nagel.
    /// </summary>
    public static readonly ShipmentCarrier Kuehne = new("KUEHNE");

    /// <summary>
    /// Nexive (TNT Post Italy).
    /// </summary>
    public static readonly ShipmentCarrier ItNexive = new("IT_NEXIVE");

    /// <summary>
    /// PTS courier.
    /// </summary>
    public static readonly ShipmentCarrier Pts = new("PTS");

    /// <summary>
    /// Swiss Post FTP.
    /// </summary>
    public static readonly ShipmentCarrier SwissPostFtp = new("SWISS_POST_FTP");

    /// <summary>
    /// Fastrak Services.
    /// </summary>
    public static readonly ShipmentCarrier FastrkServ = new("FASTRK_SERV");

    /// <summary>
    /// 4-72 Entregando.
    /// </summary>
    public static readonly ShipmentCarrier _472 = new("_4_72");

    /// <summary>
    /// YRC courier.
    /// </summary>
    public static readonly ShipmentCarrier UsYrc = new("US_YRC");

    /// <summary>
    /// PostNL International 3S.
    /// </summary>
    public static readonly ShipmentCarrier PostnlIntl3S = new("POSTNL_INTL_3S");

    /// <summary>
    /// Yilian (Elian) Supply Chain.
    /// </summary>
    public static readonly ShipmentCarrier ElianPost = new("ELIAN_POST");

    /// <summary>
    /// Cubyn.
    /// </summary>
    public static readonly ShipmentCarrier Cubyn = new("CUBYN");

    /// <summary>
    /// Saudi Post.
    /// </summary>
    public static readonly ShipmentCarrier SauSaudiPost = new("SAU_SAUDI_POST");

    /// <summary>
    /// ABX Express.
    /// </summary>
    public static readonly ShipmentCarrier AbxexpressMy = new("ABXEXPRESS_MY");

    /// <summary>
    /// HUAHANG EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier HuahanExpress = new("HUAHAN_EXPRESS");

    /// <summary>
    /// Eshun international Logistic.
    /// </summary>
    public static readonly ShipmentCarrier ZesExpress = new("ZES_EXPRESS");

    /// <summary>
    /// ZeptoExpress.
    /// </summary>
    public static readonly ShipmentCarrier ZeptoExpress = new("ZEPTO_EXPRESS");

    /// <summary>
    /// Skynet World Wide Express South Africa.
    /// </summary>
    public static readonly ShipmentCarrier SkynetZa = new("SKYNET_ZA");

    /// <summary>
    /// Zeek2Door.
    /// </summary>
    public static readonly ShipmentCarrier Zeek2Door = new("ZEEK_2_DOOR");

    /// <summary>
    /// Blink.
    /// </summary>
    public static readonly ShipmentCarrier Blinklastmile = new("BLINKLASTMILE");

    /// <summary>
    /// UkrPoshta.
    /// </summary>
    public static readonly ShipmentCarrier PostaUkr = new("POSTA_UKR");

    /// <summary>
    /// C.H. Robinson Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier Chrobinson = new("CHROBINSON");

    /// <summary>
    /// Post56.
    /// </summary>
    public static readonly ShipmentCarrier CnPost56 = new("CN_POST56");

    /// <summary>
    /// Courant Plus.
    /// </summary>
    public static readonly ShipmentCarrier CourantPlus = new("COURANT_PLUS");

    /// <summary>
    /// Scudex Express.
    /// </summary>
    public static readonly ShipmentCarrier ScudexExpress = new("SCUDEX_EXPRESS");

    /// <summary>
    /// ShipEntegra.
    /// </summary>
    public static readonly ShipmentCarrier Shipentegra = new("SHIPENTEGRA");

    /// <summary>
    /// B2C courier Europe.
    /// </summary>
    public static readonly ShipmentCarrier BTwoCEurope = new("B_TWO_C_EUROPE");

    /// <summary>
    /// Cope Sensitive Freight.
    /// </summary>
    public static readonly ShipmentCarrier Cope = new("COPE");

    /// <summary>
    /// Gati-KWE.
    /// </summary>
    public static readonly ShipmentCarrier IndGati = new("IND_GATI");

    /// <summary>
    /// WishPost.
    /// </summary>
    public static readonly ShipmentCarrier CnWishpost = new("CN_WISHPOST");

    /// <summary>
    /// NACEX Spain.
    /// </summary>
    public static readonly ShipmentCarrier NacexEs = new("NACEX_ES");

    /// <summary>
    /// TAQBIN Hong Kong.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinHk = new("TAQBIN_HK");

    /// <summary>
    /// GlobalTranz.
    /// </summary>
    public static readonly ShipmentCarrier Globaltranz = new("GLOBALTRANZ");

    /// <summary>
    /// Qingdao HKD International Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Hkd = new("HKD");

    /// <summary>
    /// BJS Distribution courier.
    /// </summary>
    public static readonly ShipmentCarrier Bjshomedelivery = new("BJSHOMEDELIVERY");

    /// <summary>
    /// Omniva.
    /// </summary>
    public static readonly ShipmentCarrier Omniva = new("OMNIVA");

    /// <summary>
    /// Sutton Transport.
    /// </summary>
    public static readonly ShipmentCarrier Sutton = new("SUTTON");

    /// <summary>
    /// Panther Reference.
    /// </summary>
    public static readonly ShipmentCarrier PantherReference = new("PANTHER_REFERENCE");

    /// <summary>
    /// SFC Service.
    /// </summary>
    public static readonly ShipmentCarrier Sfcservice = new("SFCSERVICE");

    /// <summary>
    /// LTL COURIER.
    /// </summary>
    public static readonly ShipmentCarrier Ltl = new("LTL");

    /// <summary>
    /// Park N Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Parknparcel = new("PARKNPARCEL");

    /// <summary>
    /// Spring GDS.
    /// </summary>
    public static readonly ShipmentCarrier SpringGds = new("SPRING_GDS");

    /// <summary>
    /// ECexpress.
    /// </summary>
    public static readonly ShipmentCarrier Ecexpress = new("ECEXPRESS");

    /// <summary>
    /// Interparcel Australia.
    /// </summary>
    public static readonly ShipmentCarrier InterparcelAu = new("INTERPARCEL_AU");

    /// <summary>
    /// Agility.
    /// </summary>
    public static readonly ShipmentCarrier Agility = new("AGILITY");

    /// <summary>
    /// XL Express.
    /// </summary>
    public static readonly ShipmentCarrier XlExpress = new("XL_EXPRESS");

    /// <summary>
    /// Ader couriers.
    /// </summary>
    public static readonly ShipmentCarrier Aderonline = new("ADERONLINE");

    /// <summary>
    /// Direct Couriers.
    /// </summary>
    public static readonly ShipmentCarrier Directcouriers = new("DIRECTCOURIERS");

    /// <summary>
    /// Planzer Group.
    /// </summary>
    public static readonly ShipmentCarrier Planzer = new("PLANZER");

    /// <summary>
    /// Sending Transporte Urgente y Comunicacion.
    /// </summary>
    public static readonly ShipmentCarrier Sending = new("SENDING");

    /// <summary>
    /// Ninjavan Webhook.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanWb = new("NINJAVAN_WB");

    /// <summary>
    /// Nationwide Express Courier Services Bhd (www.nationwide.com.my).
    /// </summary>
    public static readonly ShipmentCarrier NationwideMy = new("NATIONWIDE_MY");

    /// <summary>
    /// Sendit.
    /// </summary>
    public static readonly ShipmentCarrier Sendit = new("SENDIT");

    /// <summary>
    /// Arrow XL.
    /// </summary>
    public static readonly ShipmentCarrier GbArrow = new("GB_ARROW");

    /// <summary>
    /// GoJavas.
    /// </summary>
    public static readonly ShipmentCarrier IndGojavas = new("IND_GOJAVAS");

    /// <summary>
    /// Korea Post.
    /// </summary>
    public static readonly ShipmentCarrier Kpost = new("KPOST");

    /// <summary>
    /// DHL Freight.
    /// </summary>
    public static readonly ShipmentCarrier DhlFreight = new("DHL_FREIGHT");

    /// <summary>
    /// Bluecare Express Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Bluecare = new("BLUECARE");

    /// <summary>
    /// jindouyun courier.
    /// </summary>
    public static readonly ShipmentCarrier Jindouyun = new("JINDOUYUN");

    /// <summary>
    /// Trackon Couriers Pvt. Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Trackon = new("TRACKON");

    /// <summary>
    /// Tuffnells Parcels Express.
    /// </summary>
    public static readonly ShipmentCarrier GbTuffnells = new("GB_TUFFNELLS");

    /// <summary>
    /// TRUMPCARD LLC.
    /// </summary>
    public static readonly ShipmentCarrier Trumpcard = new("TRUMPCARD");

    /// <summary>
    /// eTotal Solution Limited.
    /// </summary>
    public static readonly ShipmentCarrier Etotal = new("ETOTAL");

    /// <summary>
    /// Zeek courier.
    /// </summary>
    public static readonly ShipmentCarrier SfplusWebhook = new("SFPLUS_WEBHOOK");

    /// <summary>
    /// SEKO Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Sekologistics = new("SEKOLOGISTICS");

    /// <summary>
    /// Hermes Einrichtungs Service GmbH &amp; Co. KG.
    /// </summary>
    public static readonly ShipmentCarrier Hermes2MannHandling = new("HERMES_2MANN_HANDLING");

    /// <summary>
    /// DPD Local reference.
    /// </summary>
    public static readonly ShipmentCarrier DpdLocalRef = new("DPD_LOCAL_REF");

    /// <summary>
    /// United Delivery Service.
    /// </summary>
    public static readonly ShipmentCarrier Uds = new("UDS");

    /// <summary>
    /// Specialised Freight.
    /// </summary>
    public static readonly ShipmentCarrier ZaSpecialisedFreight = new("ZA_SPECIALISED_FREIGHT");

    /// <summary>
    /// Kerry Express Thailand.
    /// </summary>
    public static readonly ShipmentCarrier ThaKerry = new("THA_KERRY");

    /// <summary>
    /// SEUR International.
    /// </summary>
    public static readonly ShipmentCarrier PrtIntSeur = new("PRT_INT_SEUR");

    /// <summary>
    /// Correios Brazil.
    /// </summary>
    public static readonly ShipmentCarrier BraCorreios = new("BRA_CORREIOS");

    /// <summary>
    /// New Zealand Post.
    /// </summary>
    public static readonly ShipmentCarrier NzNzPost = new("NZ_NZ_POST");

    /// <summary>
    /// Equick China.
    /// </summary>
    public static readonly ShipmentCarrier CnEquick = new("CN_EQUICK");

    /// <summary>
    /// Malaysia Post EMS / Pos Laju.
    /// </summary>
    public static readonly ShipmentCarrier MysEms = new("MYS_EMS");

    /// <summary>
    /// Norsk Global.
    /// </summary>
    public static readonly ShipmentCarrier GbNorsk = new("GB_NORSK");

    /// <summary>
    /// MRW spain.
    /// </summary>
    public static readonly ShipmentCarrier EspMrw = new("ESP_MRW");

    /// <summary>
    /// Packlink.
    /// </summary>
    public static readonly ShipmentCarrier EspPacklink = new("ESP_PACKLINK");

    /// <summary>
    /// Kangaroo Worldwide Express.
    /// </summary>
    public static readonly ShipmentCarrier KangarooMy = new("KANGAROO_MY");

    /// <summary>
    /// RPX Online.
    /// </summary>
    public static readonly ShipmentCarrier Rpx = new("RPX");

    /// <summary>
    /// XDP Express Reference.
    /// </summary>
    public static readonly ShipmentCarrier XdpUkReference = new("XDP_UK_REFERENCE");

    /// <summary>
    /// ninja van (www.ninjavan.co).
    /// </summary>
    public static readonly ShipmentCarrier NinjavanMy = new("NINJAVAN_MY");

    /// <summary>
    /// Adicional Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Adicional = new("ADICIONAL");

    /// <summary>
    /// Red Carpet Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Roadbull = new("ROADBULL");

    /// <summary>
    /// Yakit courier.
    /// </summary>
    public static readonly ShipmentCarrier Yakit = new("YAKIT");

    /// <summary>
    /// MailAmericas.
    /// </summary>
    public static readonly ShipmentCarrier Mailamericas = new("MAILAMERICAS");

    /// <summary>
    /// Mikropakket.
    /// </summary>
    public static readonly ShipmentCarrier Mikropakket = new("MIKROPAKKET");

    /// <summary>
    /// Dynamic Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Dynalogic = new("DYNALOGIC");

    /// <summary>
    /// DHL Spain(www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlEs = new("DHL_ES");

    /// <summary>
    /// DHL Parcel NL.
    /// </summary>
    public static readonly ShipmentCarrier DhlParcelNl = new("DHL_PARCEL_NL");

    /// <summary>
    /// DHL Global Mail Asia (www.dhl.com).
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalMailAsia = new("DHL_GLOBAL_MAIL_ASIA");

    /// <summary>
    /// Dawn Wing.
    /// </summary>
    public static readonly ShipmentCarrier DawnWing = new("DAWN_WING");

    /// <summary>
    /// Geniki Taxydromiki.
    /// </summary>
    public static readonly ShipmentCarrier GenikiGr = new("GENIKI_GR");

    /// <summary>
    /// hermesworld_uk.
    /// </summary>
    public static readonly ShipmentCarrier HermesworldUk = new("HERMESWORLD_UK");

    /// <summary>
    /// Alphafast (www.alphafast.com).
    /// </summary>
    public static readonly ShipmentCarrier Alphafast = new("ALPHAFAST");

    /// <summary>
    /// buylogic.
    /// </summary>
    public static readonly ShipmentCarrier Buylogic = new("BUYLOGIC");

    /// <summary>
    /// Ekart logistics (ekartlogistics.com).
    /// </summary>
    public static readonly ShipmentCarrier Ekart = new("EKART");

    /// <summary>
    /// mexico senda express.
    /// </summary>
    public static readonly ShipmentCarrier MexSenda = new("MEX_SENDA");

    /// <summary>
    /// SFC.
    /// </summary>
    public static readonly ShipmentCarrier SfcLogistics = new("SFC_LOGISTICS");

    /// <summary>
    /// Posta Serbia.
    /// </summary>
    public static readonly ShipmentCarrier PostSerbia = new("POST_SERBIA");

    /// <summary>
    /// Delhivery India.
    /// </summary>
    public static readonly ShipmentCarrier IndDelhivery = new("IND_DELHIVERY");

    /// <summary>
    /// DPD Germany.
    /// </summary>
    public static readonly ShipmentCarrier DeDpdDelistrack = new("DE_DPD_DELISTRACK");

    /// <summary>
    /// RPD2man Deliveries.
    /// </summary>
    public static readonly ShipmentCarrier Rpd2Man = new("RPD2MAN");

    /// <summary>
    /// SF Express (www.sf-express.com).
    /// </summary>
    public static readonly ShipmentCarrier CnSfExpress = new("CN_SF_EXPRESS");

    /// <summary>
    /// Yanwen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Yanwen = new("YANWEN");

    /// <summary>
    /// Skynet Malaysia.
    /// </summary>
    public static readonly ShipmentCarrier MysSkynet = new("MYS_SKYNET");

    /// <summary>
    /// correos mexico.
    /// </summary>
    public static readonly ShipmentCarrier CorreosDeMexico = new("CORREOS_DE_MEXICO");

    /// <summary>
    /// CBL Logistica.
    /// </summary>
    public static readonly ShipmentCarrier CblLogistica = new("CBL_LOGISTICA");

    /// <summary>
    /// Estafeta (www.estafeta.com).
    /// </summary>
    public static readonly ShipmentCarrier MexEstafeta = new("MEX_ESTAFETA");

    /// <summary>
    /// Austrian Post (Registered).
    /// </summary>
    public static readonly ShipmentCarrier AuAustrianPost = new("AU_AUSTRIAN_POST");

    /// <summary>
    /// Rincos.
    /// </summary>
    public static readonly ShipmentCarrier Rincos = new("RINCOS");

    /// <summary>
    /// DHL Netherland.
    /// </summary>
    public static readonly ShipmentCarrier NldDhl = new("NLD_DHL");

    /// <summary>
    /// Russian post.
    /// </summary>
    public static readonly ShipmentCarrier RussianPost = new("RUSSIAN_POST");

    /// <summary>
    /// CouriersPlease (couriersplease.com.au).
    /// </summary>
    public static readonly ShipmentCarrier CouriersPlease = new("COURIERS_PLEASE");

    /// <summary>
    /// PostNord Logistics.
    /// </summary>
    public static readonly ShipmentCarrier PostnordLogistics = new("POSTNORD_LOGISTICS");

    /// <summary>
    /// Fedex.
    /// </summary>
    public static readonly ShipmentCarrier Fedex = new("FEDEX");

    /// <summary>
    /// DPE Express.
    /// </summary>
    public static readonly ShipmentCarrier DpeExpress = new("DPE_EXPRESS");

    /// <summary>
    /// DPD.
    /// </summary>
    public static readonly ShipmentCarrier Dpd = new("DPD");

    /// <summary>
    /// ADSone.
    /// </summary>
    public static readonly ShipmentCarrier Adsone = new("ADSONE");

    /// <summary>
    /// JNE Express (Jalur Nugraha Ekakurir).
    /// </summary>
    public static readonly ShipmentCarrier IdnJne = new("IDN_JNE");

    /// <summary>
    /// The Courier Guy.
    /// </summary>
    public static readonly ShipmentCarrier Thecourierguy = new("THECOURIERGUY");

    /// <summary>
    /// CNE Express.
    /// </summary>
    public static readonly ShipmentCarrier Cnexps = new("CNEXPS");

    /// <summary>
    /// Chronopost Portugal.
    /// </summary>
    public static readonly ShipmentCarrier PrtChronopost = new("PRT_CHRONOPOST");

    /// <summary>
    /// Landmark Global.
    /// </summary>
    public static readonly ShipmentCarrier LandmarkGlobal = new("LANDMARK_GLOBAL");

    /// <summary>
    /// DHL International.
    /// </summary>
    public static readonly ShipmentCarrier ItDhlEcommerce = new("IT_DHL_ECOMMERCE");

    /// <summary>
    /// NACEX Spain.
    /// </summary>
    public static readonly ShipmentCarrier EspNacex = new("ESP_NACEX");

    /// <summary>
    /// CTT Portugal.
    /// </summary>
    public static readonly ShipmentCarrier PrtCtt = new("PRT_CTT");

    /// <summary>
    /// Kiala.
    /// </summary>
    public static readonly ShipmentCarrier BeKiala = new("BE_KIALA");

    /// <summary>
    /// Asendia UK.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaUk = new("ASENDIA_UK");

    /// <summary>
    /// TNT global.
    /// </summary>
    public static readonly ShipmentCarrier GlobalTnt = new("GLOBAL_TNT");

    /// <summary>
    /// Iceland Post.
    /// </summary>
    public static readonly ShipmentCarrier PosturIs = new("POSTUR_IS");

    /// <summary>
    /// eParcel Korea.
    /// </summary>
    public static readonly ShipmentCarrier EparcelKr = new("EPARCEL_KR");

    /// <summary>
    /// InPost Paczkomaty.
    /// </summary>
    public static readonly ShipmentCarrier InpostPaczkomaty = new("INPOST_PACZKOMATY");

    /// <summary>
    /// Poste italiane (www.poste.it).
    /// </summary>
    public static readonly ShipmentCarrier ItPosteItalia = new("IT_POSTE_ITALIA");

    /// <summary>
    /// Bpost (www.bpost.be).
    /// </summary>
    public static readonly ShipmentCarrier BeBpost = new("BE_BPOST");

    /// <summary>
    /// Poczta Polska (www.poczta-polska.pl).
    /// </summary>
    public static readonly ShipmentCarrier PlPocztaPolska = new("PL_POCZTA_POLSKA");

    /// <summary>
    /// Malaysia Post.
    /// </summary>
    public static readonly ShipmentCarrier MysMysPost = new("MYS_MYS_POST");

    /// <summary>
    /// Singapore Post.
    /// </summary>
    public static readonly ShipmentCarrier SgSgPost = new("SG_SG_POST");

    /// <summary>
    /// Thailand Post (www.thailandpost.co.th).
    /// </summary>
    public static readonly ShipmentCarrier ThaThailandPost = new("THA_THAILAND_POST");

    /// <summary>
    /// LexShip.
    /// </summary>
    public static readonly ShipmentCarrier Lexship = new("LEXSHIP");

    /// <summary>
    /// Fastway New Zealand.
    /// </summary>
    public static readonly ShipmentCarrier FastwayNz = new("FASTWAY_NZ");

    /// <summary>
    /// DHL Supply Chain Australia.
    /// </summary>
    public static readonly ShipmentCarrier DhlAu = new("DHL_AU");

    /// <summary>
    /// Cosmetics Now.
    /// </summary>
    public static readonly ShipmentCarrier Costmeticsnow = new("COSTMETICSNOW");

    /// <summary>
    /// PFL.
    /// </summary>
    public static readonly ShipmentCarrier Pflogistics = new("PFLOGISTICS");

    /// <summary>
    /// Loomis Express.
    /// </summary>
    public static readonly ShipmentCarrier LoomisExpress = new("LOOMIS_EXPRESS");

    /// <summary>
    /// GLS Italy.
    /// </summary>
    public static readonly ShipmentCarrier GlsItaly = new("GLS_ITALY");

    /// <summary>
    /// Line Clear Express &amp; Logistics Sdn Bhd.
    /// </summary>
    public static readonly ShipmentCarrier Line = new("LINE");

    /// <summary>
    /// Gel Express Logistik.
    /// </summary>
    public static readonly ShipmentCarrier GelExpress = new("GEL_EXPRESS");

    /// <summary>
    /// Huodull.
    /// </summary>
    public static readonly ShipmentCarrier Huodull = new("HUODULL");

    /// <summary>
    /// Ninja van Singapore.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanSg = new("NINJAVAN_SG");

    /// <summary>
    /// Janio Asia.
    /// </summary>
    public static readonly ShipmentCarrier Janio = new("JANIO");

    /// <summary>
    /// AO Logistics.
    /// </summary>
    public static readonly ShipmentCarrier AoCourier = new("AO_COURIER");

    /// <summary>
    /// BRT Bartolini(Sender Reference).
    /// </summary>
    public static readonly ShipmentCarrier BrtItSenderRef = new("BRT_IT_SENDER_REF");

    /// <summary>
    /// SAILPOST.
    /// </summary>
    public static readonly ShipmentCarrier Sailpost = new("SAILPOST");

    /// <summary>
    /// Lalamove.
    /// </summary>
    public static readonly ShipmentCarrier Lalamove = new("LALAMOVE");

    /// <summary>
    /// NEW ZEALAND COURIERS.
    /// </summary>
    public static readonly ShipmentCarrier NewzealandCouriers = new("NEWZEALAND_COURIERS");

    /// <summary>
    /// Etomars.
    /// </summary>
    public static readonly ShipmentCarrier Etomars = new("ETOMARS");

    /// <summary>
    /// VIR Transport.
    /// </summary>
    public static readonly ShipmentCarrier Virtransport = new("VIRTRANSPORT");

    /// <summary>
    /// Wizmo.
    /// </summary>
    public static readonly ShipmentCarrier Wizmo = new("WIZMO");

    /// <summary>
    /// Palletways.
    /// </summary>
    public static readonly ShipmentCarrier Palletways = new("PALLETWAYS");

    /// <summary>
    /// i-dika.
    /// </summary>
    public static readonly ShipmentCarrier IDika = new("I_DIKA");

    /// <summary>
    /// CFL Logistics.
    /// </summary>
    public static readonly ShipmentCarrier CflLogistics = new("CFL_LOGISTICS");

    /// <summary>
    /// GEM Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier Gemworldwide = new("GEMWORLDWIDE");

    /// <summary>
    /// Tai Wan Global Business.
    /// </summary>
    public static readonly ShipmentCarrier GlobalExpress = new("GLOBAL_EXPRESS");

    /// <summary>
    /// Transgroup courier.
    /// </summary>
    public static readonly ShipmentCarrier LogistyxTransgroup = new("LOGISTYX_TRANSGROUP");

    /// <summary>
    /// West Bank Courier.
    /// </summary>
    public static readonly ShipmentCarrier WestbankCourier = new("WESTBANK_COURIER");

    /// <summary>
    /// Arco Spedizioni SP.
    /// </summary>
    public static readonly ShipmentCarrier ArcoSpedizioni = new("ARCO_SPEDIZIONI");

    /// <summary>
    /// YDH express.
    /// </summary>
    public static readonly ShipmentCarrier YdhExpress = new("YDH_EXPRESS");

    /// <summary>
    /// Parcelink Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Parcelinklogistics = new("PARCELINKLOGISTICS");

    /// <summary>
    /// CND Express.
    /// </summary>
    public static readonly ShipmentCarrier Cndexpress = new("CNDEXPRESS");

    /// <summary>
    /// NOX NightTimeExpress.
    /// </summary>
    public static readonly ShipmentCarrier NoxNightTimeExpress = new("NOX_NIGHT_TIME_EXPRESS");

    /// <summary>
    /// Aeronet couriers.
    /// </summary>
    public static readonly ShipmentCarrier Aeronet = new("AERONET");

    /// <summary>
    /// LTIAN EXP.
    /// </summary>
    public static readonly ShipmentCarrier Ltianexp = new("LTIANEXP");

    /// <summary>
    /// Integra2.
    /// </summary>
    public static readonly ShipmentCarrier Integra2Ftp = new("INTEGRA2_FTP");

    /// <summary>
    /// PARCEL ONE.
    /// </summary>
    public static readonly ShipmentCarrier Parcelone = new("PARCELONE");

    /// <summary>
    /// Innight Express Germany GmbH (nox NachtExpress).
    /// </summary>
    public static readonly ShipmentCarrier NoxNachtexpress = new("NOX_NACHTEXPRESS");

    /// <summary>
    /// China Post.
    /// </summary>
    public static readonly ShipmentCarrier CnChinaPostEms = new("CN_CHINA_POST_EMS");

    /// <summary>
    /// Chukou1.
    /// </summary>
    public static readonly ShipmentCarrier Chukou1 = new("CHUKOU1");

    /// <summary>
    /// GLS General Logistics Systems Slovakia s.r.o..
    /// </summary>
    public static readonly ShipmentCarrier GlsSlov = new("GLS_SLOV");

    /// <summary>
    /// OrangeDS (Orange Distribution Solutions Inc).
    /// </summary>
    public static readonly ShipmentCarrier OrangeDs = new("ORANGE_DS");

    /// <summary>
    /// Joom Logistics.
    /// </summary>
    public static readonly ShipmentCarrier JoomLogis = new("JOOM_LOGIS");

    /// <summary>
    /// StarTrack (startrack.com.au).
    /// </summary>
    public static readonly ShipmentCarrier AusStartrack = new("AUS_STARTRACK");

    /// <summary>
    /// dhl Global.
    /// </summary>
    public static readonly ShipmentCarrier Dhl = new("DHL");

    /// <summary>
    /// APC postal logistics germany.
    /// </summary>
    public static readonly ShipmentCarrier GbApc = new("GB_APC");

    /// <summary>
    /// Bonds Courier Service (bondscouriers.com.au).
    /// </summary>
    public static readonly ShipmentCarrier Bondscouriers = new("BONDSCOURIERS");

    /// <summary>
    /// Japan Post.
    /// </summary>
    public static readonly ShipmentCarrier JpnJapanPost = new("JPN_JAPAN_POST");

    /// <summary>
    /// United States Postal Service.
    /// </summary>
    public static readonly ShipmentCarrier Usps = new("USPS");

    /// <summary>
    /// WinIt.
    /// </summary>
    public static readonly ShipmentCarrier Winit = new("WINIT");

    /// <summary>
    /// OCA Argentina.
    /// </summary>
    public static readonly ShipmentCarrier ArgOca = new("ARG_OCA");

    /// <summary>
    /// Taiwan Post.
    /// </summary>
    public static readonly ShipmentCarrier TwTaiwanPost = new("TW_TAIWAN_POST");

    /// <summary>
    /// DMM Network.
    /// </summary>
    public static readonly ShipmentCarrier DmmNetwork = new("DMM_NETWORK");

    /// <summary>
    /// TNT Express.
    /// </summary>
    public static readonly ShipmentCarrier Tnt = new("TNT");

    /// <summary>
    /// BH Posta (www.posta.ba).
    /// </summary>
    public static readonly ShipmentCarrier BhPosta = new("BH_POSTA");

    /// <summary>
    /// Postnord sweden.
    /// </summary>
    public static readonly ShipmentCarrier SwePostnord = new("SWE_POSTNORD");

    /// <summary>
    /// Canada Post.
    /// </summary>
    public static readonly ShipmentCarrier CaCanadaPost = new("CA_CANADA_POST");

    /// <summary>
    /// Wiseloads.
    /// </summary>
    public static readonly ShipmentCarrier Wiseloads = new("WISELOADS");

    /// <summary>
    /// Asendia HonKong.
    /// </summary>
    public static readonly ShipmentCarrier AsendiaHk = new("ASENDIA_HK");

    /// <summary>
    /// GLS Netherland.
    /// </summary>
    public static readonly ShipmentCarrier NldGls = new("NLD_GLS");

    /// <summary>
    /// Redpack.
    /// </summary>
    public static readonly ShipmentCarrier MexRedpack = new("MEX_REDPACK");

    /// <summary>
    /// Jet-Ship Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier JetShip = new("JET_SHIP");

    /// <summary>
    /// DHL Express.
    /// </summary>
    public static readonly ShipmentCarrier DeDhlExpress = new("DE_DHL_EXPRESS");

    /// <summary>
    /// Ninja van Thai.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanThai = new("NINJAVAN_THAI");

    /// <summary>
    /// Raben Group.
    /// </summary>
    public static readonly ShipmentCarrier RabenGroup = new("RABEN_GROUP");

    /// <summary>
    /// ASM(GLS Spain).
    /// </summary>
    public static readonly ShipmentCarrier EspAsm = new("ESP_ASM");

    /// <summary>
    /// Hrvatska posta.
    /// </summary>
    public static readonly ShipmentCarrier HrvHrvatska = new("HRV_HRVATSKA");

    /// <summary>
    /// Estes Express Lines.
    /// </summary>
    public static readonly ShipmentCarrier GlobalEstes = new("GLOBAL_ESTES");

    /// <summary>
    /// Lietuvos pastas.
    /// </summary>
    public static readonly ShipmentCarrier LtuLietuvos = new("LTU_LIETUVOS");

    /// <summary>
    /// DHL Benelux.
    /// </summary>
    public static readonly ShipmentCarrier BelDhl = new("BEL_DHL");

    /// <summary>
    /// Australia Post.
    /// </summary>
    public static readonly ShipmentCarrier AuAuPost = new("AU_AU_POST");

    /// <summary>
    /// SPEEDEX couriers.
    /// </summary>
    public static readonly ShipmentCarrier Speedexcourier = new("SPEEDEXCOURIER");

    /// <summary>
    /// Colissimo.
    /// </summary>
    public static readonly ShipmentCarrier FrColis = new("FR_COLIS");

    /// <summary>
    /// Aramex.
    /// </summary>
    public static readonly ShipmentCarrier Aramex = new("ARAMEX");

    /// <summary>
    /// DPEX (www.dpex.com).
    /// </summary>
    public static readonly ShipmentCarrier Dpex = new("DPEX");

    /// <summary>
    /// Airpak Express.
    /// </summary>
    public static readonly ShipmentCarrier MysAirpak = new("MYS_AIRPAK");

    /// <summary>
    /// Cuckoo Express.
    /// </summary>
    public static readonly ShipmentCarrier Cuckooexpress = new("CUCKOOEXPRESS");

    /// <summary>
    /// DPD Poland.
    /// </summary>
    public static readonly ShipmentCarrier DpdPoland = new("DPD_POLAND");

    /// <summary>
    /// PostNL International.
    /// </summary>
    public static readonly ShipmentCarrier NldPostnl = new("NLD_POSTNL");

    /// <summary>
    /// Nim Express.
    /// </summary>
    public static readonly ShipmentCarrier NimExpress = new("NIM_EXPRESS");

    /// <summary>
    /// Quantium.
    /// </summary>
    public static readonly ShipmentCarrier Quantium = new("QUANTIUM");

    /// <summary>
    /// Sendle.
    /// </summary>
    public static readonly ShipmentCarrier Sendle = new("SENDLE");

    /// <summary>
    /// Redur Spain.
    /// </summary>
    public static readonly ShipmentCarrier EspRedur = new("ESP_REDUR");

    /// <summary>
    /// Matkahuolto.
    /// </summary>
    public static readonly ShipmentCarrier Matkahuolto = new("MATKAHUOLTO");

    /// <summary>
    /// Cpacket couriers.
    /// </summary>
    public static readonly ShipmentCarrier Cpacket = new("CPACKET");

    /// <summary>
    /// Posti courier.
    /// </summary>
    public static readonly ShipmentCarrier Posti = new("POSTI");

    /// <summary>
    /// Hunter Express.
    /// </summary>
    public static readonly ShipmentCarrier HunterExpress = new("HUNTER_EXPRESS");

    /// <summary>
    /// Choir Express Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier ChoirExp = new("CHOIR_EXP");

    /// <summary>
    /// Legion Express.
    /// </summary>
    public static readonly ShipmentCarrier LegionExpress = new("LEGION_EXPRESS");

    /// <summary>
    /// austrian post.
    /// </summary>
    public static readonly ShipmentCarrier AustrianPostExpress = new("AUSTRIAN_POST_EXPRESS");

    /// <summary>
    /// Grupo ampm.
    /// </summary>
    public static readonly ShipmentCarrier Grupo = new("GRUPO");

    /// <summary>
    /// Post Roman (www.posta-romana.ro).
    /// </summary>
    public static readonly ShipmentCarrier PostaRo = new("POSTA_RO");

    /// <summary>
    /// Interparcel UK.
    /// </summary>
    public static readonly ShipmentCarrier InterparcelUk = new("INTERPARCEL_UK");

    /// <summary>
    /// ABF Freight.
    /// </summary>
    public static readonly ShipmentCarrier GlobalAbf = new("GLOBAL_ABF");

    /// <summary>
    /// Posten Norge (www.posten.no).
    /// </summary>
    public static readonly ShipmentCarrier PostenNorge = new("POSTEN_NORGE");

    /// <summary>
    /// Xpert Delivery.
    /// </summary>
    public static readonly ShipmentCarrier XpertDelivery = new("XPERT_DELIVERY");

    /// <summary>
    /// DHl (Reference number).
    /// </summary>
    public static readonly ShipmentCarrier DhlRefr = new("DHL_REFR");

    /// <summary>
    /// DHL HonKong.
    /// </summary>
    public static readonly ShipmentCarrier DhlHk = new("DHL_HK");

    /// <summary>
    /// SKYNET UAE.
    /// </summary>
    public static readonly ShipmentCarrier SkynetUae = new("SKYNET_UAE");

    /// <summary>
    /// Gojek.
    /// </summary>
    public static readonly ShipmentCarrier Gojek = new("GOJEK");

    /// <summary>
    /// Yodel International.
    /// </summary>
    public static readonly ShipmentCarrier YodelIntnl = new("YODEL_INTNL");

    /// <summary>
    /// Janco Ecommerce.
    /// </summary>
    public static readonly ShipmentCarrier Janco = new("JANCO");

    /// <summary>
    /// YTO Express.
    /// </summary>
    public static readonly ShipmentCarrier Yto = new("YTO");

    /// <summary>
    /// Wise Express.
    /// </summary>
    public static readonly ShipmentCarrier WiseExpress = new("WISE_EXPRESS");

    /// <summary>
    /// J&amp;T Express Vietnam.
    /// </summary>
    public static readonly ShipmentCarrier JtexpressVn = new("JTEXPRESS_VN");

    /// <summary>
    /// FedEx International MailService.
    /// </summary>
    public static readonly ShipmentCarrier FedexIntlMlserv = new("FEDEX_INTL_MLSERV");

    /// <summary>
    /// VAMOX.
    /// </summary>
    public static readonly ShipmentCarrier Vamox = new("VAMOX");

    /// <summary>
    /// AMS Group.
    /// </summary>
    public static readonly ShipmentCarrier AmsGrp = new("AMS_GRP");

    /// <summary>
    /// DHL Japan.
    /// </summary>
    public static readonly ShipmentCarrier DhlJp = new("DHL_JP");

    /// <summary>
    /// HR Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Hrparcel = new("HRPARCEL");

    /// <summary>
    /// GESWL Express.
    /// </summary>
    public static readonly ShipmentCarrier Geswl = new("GESWL");

    /// <summary>
    /// Blue Star.
    /// </summary>
    public static readonly ShipmentCarrier Bluestar = new("BLUESTAR");

    /// <summary>
    /// CDEK TR.
    /// </summary>
    public static readonly ShipmentCarrier CdekTr = new("CDEK_TR");

    /// <summary>
    /// Innovel courier.
    /// </summary>
    public static readonly ShipmentCarrier Descartes = new("DESCARTES");

    /// <summary>
    /// Deltec Courier.
    /// </summary>
    public static readonly ShipmentCarrier DeltecUk = new("DELTEC_UK");

    /// <summary>
    /// DTDC express.
    /// </summary>
    public static readonly ShipmentCarrier DtdcExpress = new("DTDC_EXPRESS");

    /// <summary>
    /// tourline.
    /// </summary>
    public static readonly ShipmentCarrier Tourline = new("TOURLINE");

    /// <summary>
    /// B&amp;H Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier BhWorldwide = new("BH_WORLDWIDE");

    /// <summary>
    /// OCS ANA Group.
    /// </summary>
    public static readonly ShipmentCarrier Ocs = new("OCS");

    /// <summary>
    /// yingnuo logistics.
    /// </summary>
    public static readonly ShipmentCarrier YingnuoLogistics = new("YINGNUO_LOGISTICS");

    /// <summary>
    /// United Parcel Service.
    /// </summary>
    public static readonly ShipmentCarrier Ups = new("UPS");

    /// <summary>
    /// Toll IPEC.
    /// </summary>
    public static readonly ShipmentCarrier Toll = new("TOLL");

    /// <summary>
    /// SEUR portugal.
    /// </summary>
    public static readonly ShipmentCarrier PrtSeur = new("PRT_SEUR");

    /// <summary>
    /// DTDC Australia.
    /// </summary>
    public static readonly ShipmentCarrier DtdcAu = new("DTDC_AU");

    /// <summary>
    /// Dynamic Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ThaDynamicLogistics = new("THA_DYNAMIC_LOGISTICS");

    /// <summary>
    /// UBI Smart Parcel.
    /// </summary>
    public static readonly ShipmentCarrier UbiLogistics = new("UBI_LOGISTICS");

    /// <summary>
    /// FedEx Cross Border.
    /// </summary>
    public static readonly ShipmentCarrier FedexCrossborder = new("FEDEX_CROSSBORDER");

    /// <summary>
    /// A1Post.
    /// </summary>
    public static readonly ShipmentCarrier A1Post = new("A1POST");

    /// <summary>
    /// Tazmanian Freight Systems.
    /// </summary>
    public static readonly ShipmentCarrier TazmanianFreight = new("TAZMANIAN_FREIGHT");

    /// <summary>
    /// CJ International malaysia.
    /// </summary>
    public static readonly ShipmentCarrier CjIntMy = new("CJ_INT_MY");

    /// <summary>
    /// Saia LTL Freight.
    /// </summary>
    public static readonly ShipmentCarrier SaiaFreight = new("SAIA_FREIGHT");

    /// <summary>
    /// Qxpress.
    /// </summary>
    public static readonly ShipmentCarrier SgQxpress = new("SG_QXPRESS");

    /// <summary>
    /// Nhans Solutions.
    /// </summary>
    public static readonly ShipmentCarrier NhansSolutions = new("NHANS_SOLUTIONS");

    /// <summary>
    /// DPD France.
    /// </summary>
    public static readonly ShipmentCarrier DpdFr = new("DPD_FR");

    /// <summary>
    /// Coordinadora.
    /// </summary>
    public static readonly ShipmentCarrier Coordinadora = new("COORDINADORA");

    /// <summary>
    /// Grupo logistico Andreani.
    /// </summary>
    public static readonly ShipmentCarrier Andreani = new("ANDREANI");

    /// <summary>
    /// Doora Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Doora = new("DOORA");

    /// <summary>
    /// Interparcel New Zealand.
    /// </summary>
    public static readonly ShipmentCarrier InterparcelNz = new("INTERPARCEL_NZ");

    /// <summary>
    /// Jam Express Philippines.
    /// </summary>
    public static readonly ShipmentCarrier PhlJamexpress = new("PHL_JAMEXPRESS");

    /// <summary>
    /// bel_belgium_post.
    /// </summary>
    public static readonly ShipmentCarrier BelBelgiumPost = new("BEL_BELGIUM_POST");

    /// <summary>
    /// us_apc.
    /// </summary>
    public static readonly ShipmentCarrier UsApc = new("US_APC");

    /// <summary>
    /// idn_pos.
    /// </summary>
    public static readonly ShipmentCarrier IdnPos = new("IDN_POS");

    /// <summary>
    /// fr_mondial.
    /// </summary>
    public static readonly ShipmentCarrier FrMondial = new("FR_MONDIAL");

    /// <summary>
    /// DE DHL.
    /// </summary>
    public static readonly ShipmentCarrier DeDhl = new("DE_DHL");

    /// <summary>
    /// hk_rpx.
    /// </summary>
    public static readonly ShipmentCarrier HkRpx = new("HK_RPX");

    /// <summary>
    /// dhl_pieceid.
    /// </summary>
    public static readonly ShipmentCarrier DhlPieceid = new("DHL_PIECEID");

    /// <summary>
    /// vnpost_ems.
    /// </summary>
    public static readonly ShipmentCarrier VnpostEms = new("VNPOST_EMS");

    /// <summary>
    /// rrdonnelley.
    /// </summary>
    public static readonly ShipmentCarrier Rrdonnelley = new("RRDONNELLEY");

    /// <summary>
    /// dpd_de.
    /// </summary>
    public static readonly ShipmentCarrier DpdDe = new("DPD_DE");

    /// <summary>
    /// delcart_in.
    /// </summary>
    public static readonly ShipmentCarrier DelcartIn = new("DELCART_IN");

    /// <summary>
    /// imexglobalsolutions.
    /// </summary>
    public static readonly ShipmentCarrier Imexglobalsolutions = new("IMEXGLOBALSOLUTIONS");

    /// <summary>
    /// ACOMMERCE.
    /// </summary>
    public static readonly ShipmentCarrier Acommerce = new("ACOMMERCE");

    /// <summary>
    /// eurodis.
    /// </summary>
    public static readonly ShipmentCarrier Eurodis = new("EURODIS");

    /// <summary>
    /// CANPAR.
    /// </summary>
    public static readonly ShipmentCarrier Canpar = new("CANPAR");

    /// <summary>
    /// GLS.
    /// </summary>
    public static readonly ShipmentCarrier Gls = new("GLS");

    /// <summary>
    /// Ecom Express.
    /// </summary>
    public static readonly ShipmentCarrier IndEcom = new("IND_ECOM");

    /// <summary>
    /// Envialia.
    /// </summary>
    public static readonly ShipmentCarrier EspEnvialia = new("ESP_ENVIALIA");

    /// <summary>
    /// dhl UK.
    /// </summary>
    public static readonly ShipmentCarrier DhlUk = new("DHL_UK");

    /// <summary>
    /// SMSA Express.
    /// </summary>
    public static readonly ShipmentCarrier SmsaExpress = new("SMSA_EXPRESS");

    /// <summary>
    /// TNT France.
    /// </summary>
    public static readonly ShipmentCarrier TntFr = new("TNT_FR");

    /// <summary>
    /// DEX-I courier.
    /// </summary>
    public static readonly ShipmentCarrier DexI = new("DEX_I");

    /// <summary>
    /// Budbee courier.
    /// </summary>
    public static readonly ShipmentCarrier BudbeeWebhook = new("BUDBEE_WEBHOOK");

    /// <summary>
    /// Copa Airlines Courier.
    /// </summary>
    public static readonly ShipmentCarrier CopaCourier = new("COPA_COURIER");

    /// <summary>
    /// Vietnam Post.
    /// </summary>
    public static readonly ShipmentCarrier VnmVietnamPost = new("VNM_VIETNAM_POST");

    /// <summary>
    /// DPD HongKong.
    /// </summary>
    public static readonly ShipmentCarrier DpdHk = new("DPD_HK");

    /// <summary>
    /// Toll New Zealand.
    /// </summary>
    public static readonly ShipmentCarrier TollNz = new("TOLL_NZ");

    /// <summary>
    /// Echo courier.
    /// </summary>
    public static readonly ShipmentCarrier Echo = new("ECHO");

    /// <summary>
    /// FedEx® Freight.
    /// </summary>
    public static readonly ShipmentCarrier FedexFr = new("FEDEX_FR");

    /// <summary>
    /// Border Express.
    /// </summary>
    public static readonly ShipmentCarrier Borderexpress = new("BORDEREXPRESS");

    /// <summary>
    /// MailPlus (Japan).
    /// </summary>
    public static readonly ShipmentCarrier MailplusJpn = new("MAILPLUS_JPN");

    /// <summary>
    /// TNT UK Reference.
    /// </summary>
    public static readonly ShipmentCarrier TntUkRefr = new("TNT_UK_REFR");

    /// <summary>
    /// KEC courier.
    /// </summary>
    public static readonly ShipmentCarrier Kec = new("KEC");

    /// <summary>
    /// DPD Romania.
    /// </summary>
    public static readonly ShipmentCarrier DpdRo = new("DPD_RO");

    /// <summary>
    /// TNT_JP.
    /// </summary>
    public static readonly ShipmentCarrier TntJp = new("TNT_JP");

    /// <summary>
    /// TH_CJ.
    /// </summary>
    public static readonly ShipmentCarrier ThCj = new("TH_CJ");

    /// <summary>
    /// EC_CN.
    /// </summary>
    public static readonly ShipmentCarrier EcCn = new("EC_CN");

    /// <summary>
    /// FASTWAY_UK.
    /// </summary>
    public static readonly ShipmentCarrier FastwayUk = new("FASTWAY_UK");

    /// <summary>
    /// FASTWAY_US.
    /// </summary>
    public static readonly ShipmentCarrier FastwayUs = new("FASTWAY_US");

    /// <summary>
    /// GLS_DE.
    /// </summary>
    public static readonly ShipmentCarrier GlsDe = new("GLS_DE");

    /// <summary>
    /// GLS_ES.
    /// </summary>
    public static readonly ShipmentCarrier GlsEs = new("GLS_ES");

    /// <summary>
    /// GLS_FR.
    /// </summary>
    public static readonly ShipmentCarrier GlsFr = new("GLS_FR");

    /// <summary>
    /// MONDIAL_BE.
    /// </summary>
    public static readonly ShipmentCarrier MondialBe = new("MONDIAL_BE");

    /// <summary>
    /// SGT_IT.
    /// </summary>
    public static readonly ShipmentCarrier SgtIt = new("SGT_IT");

    /// <summary>
    /// TNT_CN.
    /// </summary>
    public static readonly ShipmentCarrier TntCn = new("TNT_CN");

    /// <summary>
    /// TNT_DE.
    /// </summary>
    public static readonly ShipmentCarrier TntDe = new("TNT_DE");

    /// <summary>
    /// TNT_ES.
    /// </summary>
    public static readonly ShipmentCarrier TntEs = new("TNT_ES");

    /// <summary>
    /// TNT_PL.
    /// </summary>
    public static readonly ShipmentCarrier TntPl = new("TNT_PL");

    /// <summary>
    /// PARCELFORCE.
    /// </summary>
    public static readonly ShipmentCarrier Parcelforce = new("PARCELFORCE");

    /// <summary>
    /// SWISS POST.
    /// </summary>
    public static readonly ShipmentCarrier SwissPost = new("SWISS_POST");

    /// <summary>
    /// TOLL IPEC.
    /// </summary>
    public static readonly ShipmentCarrier TollIpec = new("TOLL_IPEC");

    /// <summary>
    /// AIR 21.
    /// </summary>
    public static readonly ShipmentCarrier Air21 = new("AIR_21");

    /// <summary>
    /// AIRSPEED.
    /// </summary>
    public static readonly ShipmentCarrier Airspeed = new("AIRSPEED");

    /// <summary>
    /// BERT.
    /// </summary>
    public static readonly ShipmentCarrier Bert = new("BERT");

    /// <summary>
    /// BLUEDART.
    /// </summary>
    public static readonly ShipmentCarrier Bluedart = new("BLUEDART");

    /// <summary>
    /// COLLECTPLUS.
    /// </summary>
    public static readonly ShipmentCarrier Collectplus = new("COLLECTPLUS");

    /// <summary>
    /// COURIERPLUS.
    /// </summary>
    public static readonly ShipmentCarrier Courierplus = new("COURIERPLUS");

    /// <summary>
    /// COURIER POST.
    /// </summary>
    public static readonly ShipmentCarrier CourierPost = new("COURIER_POST");

    /// <summary>
    /// dhl_global_mail.
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalMail = new("DHL_GLOBAL_MAIL");

    /// <summary>
    /// dpd_uk.
    /// </summary>
    public static readonly ShipmentCarrier DpdUk = new("DPD_UK");

    /// <summary>
    /// DELTEC DE.
    /// </summary>
    public static readonly ShipmentCarrier DeltecDe = new("DELTEC_DE");

    /// <summary>
    /// deutsche_de.
    /// </summary>
    public static readonly ShipmentCarrier DeutscheDe = new("DEUTSCHE_DE");

    /// <summary>
    /// DOTZOT.
    /// </summary>
    public static readonly ShipmentCarrier Dotzot = new("DOTZOT");

    /// <summary>
    /// elta_gr.
    /// </summary>
    public static readonly ShipmentCarrier EltaGr = new("ELTA_GR");

    /// <summary>
    /// ems_cn.
    /// </summary>
    public static readonly ShipmentCarrier EmsCn = new("EMS_CN");

    /// <summary>
    /// ECARGO.
    /// </summary>
    public static readonly ShipmentCarrier Ecargo = new("ECARGO");

    /// <summary>
    /// ENSENDA.
    /// </summary>
    public static readonly ShipmentCarrier Ensenda = new("ENSENDA");

    /// <summary>
    /// fercam_it.
    /// </summary>
    public static readonly ShipmentCarrier FercamIt = new("FERCAM_IT");

    /// <summary>
    /// fastway_za.
    /// </summary>
    public static readonly ShipmentCarrier FastwayZa = new("FASTWAY_ZA");

    /// <summary>
    /// fastway_au.
    /// </summary>
    public static readonly ShipmentCarrier FastwayAu = new("FASTWAY_AU");

    /// <summary>
    /// first_logisitcs.
    /// </summary>
    public static readonly ShipmentCarrier FirstLogisitcs = new("FIRST_LOGISITCS");

    /// <summary>
    /// GEODIS.
    /// </summary>
    public static readonly ShipmentCarrier Geodis = new("GEODIS");

    /// <summary>
    /// GLOBEGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Globegistics = new("GLOBEGISTICS");

    /// <summary>
    /// GREYHOUND.
    /// </summary>
    public static readonly ShipmentCarrier Greyhound = new("GREYHOUND");

    /// <summary>
    /// jetship_my.
    /// </summary>
    public static readonly ShipmentCarrier JetshipMy = new("JETSHIP_MY");

    /// <summary>
    /// LION PARCEL.
    /// </summary>
    public static readonly ShipmentCarrier LionParcel = new("LION_PARCEL");

    /// <summary>
    /// AEROFLASH.
    /// </summary>
    public static readonly ShipmentCarrier Aeroflash = new("AEROFLASH");

    /// <summary>
    /// ONTRAC.
    /// </summary>
    public static readonly ShipmentCarrier Ontrac = new("ONTRAC");

    /// <summary>
    /// SAGAWA.
    /// </summary>
    public static readonly ShipmentCarrier Sagawa = new("SAGAWA");

    /// <summary>
    /// SIODEMKA.
    /// </summary>
    public static readonly ShipmentCarrier Siodemka = new("SIODEMKA");

    /// <summary>
    /// startrack.
    /// </summary>
    public static readonly ShipmentCarrier Startrack = new("STARTRACK");

    /// <summary>
    /// tnt_au.
    /// </summary>
    public static readonly ShipmentCarrier TntAu = new("TNT_AU");

    /// <summary>
    /// tnt_it.
    /// </summary>
    public static readonly ShipmentCarrier TntIt = new("TNT_IT");

    /// <summary>
    /// TRANSMISSION.
    /// </summary>
    public static readonly ShipmentCarrier Transmission = new("TRANSMISSION");

    /// <summary>
    /// YAMATO.
    /// </summary>
    public static readonly ShipmentCarrier Yamato = new("YAMATO");

    /// <summary>
    /// dhl_it.
    /// </summary>
    public static readonly ShipmentCarrier DhlIt = new("DHL_IT");

    /// <summary>
    /// dhl_at.
    /// </summary>
    public static readonly ShipmentCarrier DhlAt = new("DHL_AT");

    /// <summary>
    /// LOGISTICSWORLDWIDE KR.
    /// </summary>
    public static readonly ShipmentCarrier LogisticsworldwideKr = new("LOGISTICSWORLDWIDE_KR");

    /// <summary>
    /// gls_spain.
    /// </summary>
    public static readonly ShipmentCarrier GlsSpain = new("GLS_SPAIN");

    /// <summary>
    /// amazon_uk_api.
    /// </summary>
    public static readonly ShipmentCarrier AmazonUkApi = new("AMAZON_UK_API");

    /// <summary>
    /// dpd_fr_reference.
    /// </summary>
    public static readonly ShipmentCarrier DpdFrReference = new("DPD_FR_REFERENCE");

    /// <summary>
    /// dhlparcel_uk.
    /// </summary>
    public static readonly ShipmentCarrier DhlparcelUk = new("DHLPARCEL_UK");

    /// <summary>
    /// megasave.
    /// </summary>
    public static readonly ShipmentCarrier Megasave = new("MEGASAVE");

    /// <summary>
    /// qualitypost.
    /// </summary>
    public static readonly ShipmentCarrier Qualitypost = new("QUALITYPOST");

    /// <summary>
    /// ids_logistics.
    /// </summary>
    public static readonly ShipmentCarrier IdsLogistics = new("IDS_LOGISTICS");

    /// <summary>
    /// joyingbox.
    /// </summary>
    public static readonly ShipmentCarrier Joyingbox = new("JOYINGBOX");

    /// <summary>
    /// panther_order_number.
    /// </summary>
    public static readonly ShipmentCarrier PantherOrderNumber = new("PANTHER_ORDER_NUMBER");

    /// <summary>
    /// watkins_shepard.
    /// </summary>
    public static readonly ShipmentCarrier WatkinsShepard = new("WATKINS_SHEPARD");

    /// <summary>
    /// fasttrack.
    /// </summary>
    public static readonly ShipmentCarrier Fasttrack = new("FASTTRACK");

    /// <summary>
    /// up_express.
    /// </summary>
    public static readonly ShipmentCarrier UpExpress = new("UP_EXPRESS");

    /// <summary>
    /// elogistica.
    /// </summary>
    public static readonly ShipmentCarrier Elogistica = new("ELOGISTICA");

    /// <summary>
    /// ecourier.
    /// </summary>
    public static readonly ShipmentCarrier Ecourier = new("ECOURIER");

    /// <summary>
    /// cj_philippines.
    /// </summary>
    public static readonly ShipmentCarrier CjPhilippines = new("CJ_PHILIPPINES");

    /// <summary>
    /// speedex.
    /// </summary>
    public static readonly ShipmentCarrier Speedex = new("SPEEDEX");

    /// <summary>
    /// orangeconnex.
    /// </summary>
    public static readonly ShipmentCarrier Orangeconnex = new("ORANGECONNEX");

    /// <summary>
    /// tecor.
    /// </summary>
    public static readonly ShipmentCarrier Tecor = new("TECOR");

    /// <summary>
    /// saee.
    /// </summary>
    public static readonly ShipmentCarrier Saee = new("SAEE");

    /// <summary>
    /// gls_italy_ftp.
    /// </summary>
    public static readonly ShipmentCarrier GlsItalyFtp = new("GLS_ITALY_FTP");

    /// <summary>
    /// delivere.
    /// </summary>
    public static readonly ShipmentCarrier Delivere = new("DELIVERE");

    /// <summary>
    /// yycom.
    /// </summary>
    public static readonly ShipmentCarrier Yycom = new("YYCOM");

    /// <summary>
    /// Adicional Logistics.
    /// </summary>
    public static readonly ShipmentCarrier AdicionalPt = new("ADICIONAL_PT");

    /// <summary>
    /// DKSH.
    /// </summary>
    public static readonly ShipmentCarrier Dksh = new("DKSH");

    /// <summary>
    /// Nippon Express.
    /// </summary>
    public static readonly ShipmentCarrier NipponExpressFtp = new("NIPPON_EXPRESS_FTP");

    /// <summary>
    /// GO Logistics &amp; Storage.
    /// </summary>
    public static readonly ShipmentCarrier Gols = new("GOLS");

    /// <summary>
    /// FUJIE EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Fujexp = new("FUJEXP");

    /// <summary>
    /// QTrack.
    /// </summary>
    public static readonly ShipmentCarrier Qtrack = new("QTRACK");

    /// <summary>
    /// OM LOGISTICS LTD.
    /// </summary>
    public static readonly ShipmentCarrier OmlogisticsApi = new("OMLOGISTICS_API");

    /// <summary>
    /// GDPharm Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Gdpharm = new("GDPHARM");

    /// <summary>
    /// MISUMI Group Inc..
    /// </summary>
    public static readonly ShipmentCarrier MisumiCn = new("MISUMI_CN");

    /// <summary>
    /// Rivo.
    /// </summary>
    public static readonly ShipmentCarrier AirCanada = new("AIR_CANADA");

    /// <summary>
    /// City Express.
    /// </summary>
    public static readonly ShipmentCarrier City56Webhook = new("CITY56_WEBHOOK");

    /// <summary>
    /// Sagawa.
    /// </summary>
    public static readonly ShipmentCarrier SagawaApi = new("SAGAWA_API");

    /// <summary>
    /// KedaEX.
    /// </summary>
    public static readonly ShipmentCarrier Kedaex = new("KEDAEX");

    /// <summary>
    /// Pgeon.
    /// </summary>
    public static readonly ShipmentCarrier PgeonApi = new("PGEON_API");

    /// <summary>
    /// We World Express.
    /// </summary>
    public static readonly ShipmentCarrier Weworldexpress = new("WEWORLDEXPRESS");

    /// <summary>
    /// J&amp;T International logistics.
    /// </summary>
    public static readonly ShipmentCarrier JtLogistics = new("JT_LOGISTICS");

    /// <summary>
    /// Trusk France.
    /// </summary>
    public static readonly ShipmentCarrier Trusk = new("TRUSK");

    /// <summary>
    /// ViaXpress.
    /// </summary>
    public static readonly ShipmentCarrier Viaxpress = new("VIAXPRESS");

    /// <summary>
    /// DHL Supply Chain Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplychainId = new("DHL_SUPPLYCHAIN_ID");

    /// <summary>
    /// Zuellig Pharma Korea.
    /// </summary>
    public static readonly ShipmentCarrier ZuelligpharmaSftp = new("ZUELLIGPHARMA_SFTP");

    /// <summary>
    /// Meest.
    /// </summary>
    public static readonly ShipmentCarrier Meest = new("MEEST");

    /// <summary>
    /// Toll Priority.
    /// </summary>
    public static readonly ShipmentCarrier TollPriority = new("TOLL_PRIORITY");

    /// <summary>
    /// Mothership.
    /// </summary>
    public static readonly ShipmentCarrier MothershipApi = new("MOTHERSHIP_API");

    /// <summary>
    /// Capital Transport.
    /// </summary>
    public static readonly ShipmentCarrier Capital = new("CAPITAL");

    /// <summary>
    /// Europacket+.
    /// </summary>
    public static readonly ShipmentCarrier EuropaketApi = new("EUROPAKET_API");

    /// <summary>
    /// HFD.
    /// </summary>
    public static readonly ShipmentCarrier Hfd = new("HFD");

    /// <summary>
    /// Tourline Express.
    /// </summary>
    public static readonly ShipmentCarrier TourlineReference = new("TOURLINE_REFERENCE");

    /// <summary>
    /// GIO Express Inc.
    /// </summary>
    public static readonly ShipmentCarrier GioEcourier = new("GIO_ECOURIER");

    /// <summary>
    /// CN Logistics.
    /// </summary>
    public static readonly ShipmentCarrier CnLogistics = new("CN_LOGISTICS");

    /// <summary>
    /// Pandion.
    /// </summary>
    public static readonly ShipmentCarrier Pandion = new("PANDION");

    /// <summary>
    /// Bpost API.
    /// </summary>
    public static readonly ShipmentCarrier BpostApi = new("BPOST_API");

    /// <summary>
    /// Passport Shipping.
    /// </summary>
    public static readonly ShipmentCarrier Passportshipping = new("PASSPORTSHIPPING");

    /// <summary>
    /// Pakajo World.
    /// </summary>
    public static readonly ShipmentCarrier Pakajo = new("PAKAJO");

    /// <summary>
    /// DACHSER.
    /// </summary>
    public static readonly ShipmentCarrier Dachser = new("DACHSER");

    /// <summary>
    /// Yusen Logistics.
    /// </summary>
    public static readonly ShipmentCarrier YusenSftp = new("YUSEN_SFTP");

    /// <summary>
    /// Shypmax.
    /// </summary>
    public static readonly ShipmentCarrier Shyplite = new("SHYPLITE");

    /// <summary>
    /// Xingyunyi Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Xyy = new("XYY");

    /// <summary>
    /// Metropolitan Warehouse &amp; Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Mwd = new("MWD");

    /// <summary>
    /// Faxe Cargo.
    /// </summary>
    public static readonly ShipmentCarrier Faxecargo = new("FAXECARGO");

    /// <summary>
    /// Groupe Mazet.
    /// </summary>
    public static readonly ShipmentCarrier Mazet = new("MAZET");

    /// <summary>
    /// First Logistics.
    /// </summary>
    public static readonly ShipmentCarrier FirstLogisticsApi = new("FIRST_LOGISTICS_API");

    /// <summary>
    /// SPRINT PACK.
    /// </summary>
    public static readonly ShipmentCarrier SprintPack = new("SPRINT_PACK");

    /// <summary>
    /// Hermes Germany.
    /// </summary>
    public static readonly ShipmentCarrier HermesDeFtp = new("HERMES_DE_FTP");

    /// <summary>
    /// Concise.
    /// </summary>
    public static readonly ShipmentCarrier Concise = new("CONCISE");

    /// <summary>
    /// Kerry Express TaiWan.
    /// </summary>
    public static readonly ShipmentCarrier KerryExpressTwApi = new("KERRY_EXPRESS_TW_API");

    /// <summary>
    /// EWE Global Express.
    /// </summary>
    public static readonly ShipmentCarrier Ewe = new("EWE");

    /// <summary>
    /// Fast Despatch Logistics Limited.
    /// </summary>
    public static readonly ShipmentCarrier Fastdespatch = new("FASTDESPATCH");

    /// <summary>
    /// AB Custom Group.
    /// </summary>
    public static readonly ShipmentCarrier AbcustomSftp = new("ABCUSTOM_SFTP");

    /// <summary>
    /// Chazki.
    /// </summary>
    public static readonly ShipmentCarrier Chazki = new("CHAZKI");

    /// <summary>
    /// Shippie.
    /// </summary>
    public static readonly ShipmentCarrier Shippie = new("SHIPPIE");

    /// <summary>
    /// GEODIS - Distribution &amp; Express.
    /// </summary>
    public static readonly ShipmentCarrier GeodisApi = new("GEODIS_API");

    /// <summary>
    /// Naqel Express.
    /// </summary>
    public static readonly ShipmentCarrier NaqelExpress = new("NAQEL_EXPRESS");

    /// <summary>
    /// Papa.
    /// </summary>
    public static readonly ShipmentCarrier PapaWebhook = new("PAPA_WEBHOOK");

    /// <summary>
    /// Forward Air.
    /// </summary>
    public static readonly ShipmentCarrier Forwardair = new("FORWARDAIR");

    /// <summary>
    /// Dialogo Logistica.
    /// </summary>
    public static readonly ShipmentCarrier DialogoLogisticaApi = new("DIALOGO_LOGISTICA_API");

    /// <summary>
    /// Lalamove.
    /// </summary>
    public static readonly ShipmentCarrier LalamoveApi = new("LALAMOVE_API");

    /// <summary>
    /// Tomydoor.
    /// </summary>
    public static readonly ShipmentCarrier Tomydoor = new("TOMYDOOR");

    /// <summary>
    /// Kronos Express.
    /// </summary>
    public static readonly ShipmentCarrier KronosWebhook = new("KRONOS_WEBHOOK");

    /// <summary>
    /// J&amp;T CARGO.
    /// </summary>
    public static readonly ShipmentCarrier Jtcargo = new("JTCARGO");

    /// <summary>
    /// T-cat.
    /// </summary>
    public static readonly ShipmentCarrier TCat = new("T_CAT");

    /// <summary>
    /// Concise.
    /// </summary>
    public static readonly ShipmentCarrier ConciseWebhook = new("CONCISE_WEBHOOK");

    /// <summary>
    /// Teleport.
    /// </summary>
    public static readonly ShipmentCarrier TeleportWebhook = new("TELEPORT_WEBHOOK");

    /// <summary>
    /// The Custom Companies.
    /// </summary>
    public static readonly ShipmentCarrier CustomcoApi = new("CUSTOMCO_API");

    /// <summary>
    /// Shopee Xpress.
    /// </summary>
    public static readonly ShipmentCarrier SpxTh = new("SPX_TH");

    /// <summary>
    /// Bollore Logistics.
    /// </summary>
    public static readonly ShipmentCarrier BolloreLogistics = new("BOLLORE_LOGISTICS");

    /// <summary>
    /// ClickLink.
    /// </summary>
    public static readonly ShipmentCarrier ClicklinkSftp = new("CLICKLINK_SFTP");

    /// <summary>
    /// M3 Logistics.
    /// </summary>
    public static readonly ShipmentCarrier M3Logistics = new("M3LOGISTICS");

    /// <summary>
    /// Vietnam Post.
    /// </summary>
    public static readonly ShipmentCarrier VnpostApi = new("VNPOST_API");

    /// <summary>
    /// Axlehire.
    /// </summary>
    public static readonly ShipmentCarrier AxlehireFtp = new("AXLEHIRE_FTP");

    /// <summary>
    /// Shadowfax.
    /// </summary>
    public static readonly ShipmentCarrier Shadowfax = new("SHADOWFAX");

    /// <summary>
    /// EVRi.
    /// </summary>
    public static readonly ShipmentCarrier MyhermesUkApi = new("MYHERMES_UK_API");

    /// <summary>
    /// Daiichi Freight System Inc.
    /// </summary>
    public static readonly ShipmentCarrier Daiichi = new("DAIICHI");

    /// <summary>
    /// Mensajeros Urbanos.
    /// </summary>
    public static readonly ShipmentCarrier MensajerosurbanosApi = new("MENSAJEROSURBANOS_API");

    /// <summary>
    /// PolarSpeed Inc.
    /// </summary>
    public static readonly ShipmentCarrier Polarspeed = new("POLARSPEED");

    /// <summary>
    /// iDexpress Indonesia.
    /// </summary>
    public static readonly ShipmentCarrier IdexpressId = new("IDEXPRESS_ID");

    /// <summary>
    /// Payo.
    /// </summary>
    public static readonly ShipmentCarrier Payo = new("PAYO");

    /// <summary>
    /// Whistl.
    /// </summary>
    public static readonly ShipmentCarrier WhistlSftp = new("WHISTL_SFTP");

    /// <summary>
    /// INTEX Paketdienst GmbH.
    /// </summary>
    public static readonly ShipmentCarrier IntexDe = new("INTEX_DE");

    /// <summary>
    /// Trans2u.
    /// </summary>
    public static readonly ShipmentCarrier Trans2U = new("TRANS2U");

    /// <summary>
    /// Product Care Services Limited.
    /// </summary>
    public static readonly ShipmentCarrier ProductcaregroupSftp = new("PRODUCTCAREGROUP_SFTP");

    /// <summary>
    /// Big Smart.
    /// </summary>
    public static readonly ShipmentCarrier Bigsmart = new("BIGSMART");

    /// <summary>
    /// Expeditors API Reference.
    /// </summary>
    public static readonly ShipmentCarrier ExpeditorsApiRef = new("EXPEDITORS_API_REF");

    /// <summary>
    /// AIT.
    /// </summary>
    public static readonly ShipmentCarrier AitworldwideApi = new("AITWORLDWIDE_API");

    /// <summary>
    /// World Courier.
    /// </summary>
    public static readonly ShipmentCarrier Worldcourier = new("WORLDCOURIER");

    /// <summary>
    /// Quiqup.
    /// </summary>
    public static readonly ShipmentCarrier Quiqup = new("QUIQUP");

    /// <summary>
    /// Agediss.
    /// </summary>
    public static readonly ShipmentCarrier AgedissSftp = new("AGEDISS_SFTP");

    /// <summary>
    /// Andreani.
    /// </summary>
    public static readonly ShipmentCarrier AndreaniApi = new("ANDREANI_API");

    /// <summary>
    /// CRL Express.
    /// </summary>
    public static readonly ShipmentCarrier Crlexpress = new("CRLEXPRESS");

    /// <summary>
    /// SMARTCAT.
    /// </summary>
    public static readonly ShipmentCarrier Smartcat = new("SMARTCAT");

    /// <summary>
    /// Crossflight Limited.
    /// </summary>
    public static readonly ShipmentCarrier Crossflight = new("CROSSFLIGHT");

    /// <summary>
    /// Pro Carrier.
    /// </summary>
    public static readonly ShipmentCarrier Procarrier = new("PROCARRIER");

    /// <summary>
    /// DHL (Reference number).
    /// </summary>
    public static readonly ShipmentCarrier DhlReferenceApi = new("DHL_REFERENCE_API");

    /// <summary>
    /// Seino.
    /// </summary>
    public static readonly ShipmentCarrier SeinoApi = new("SEINO_API");

    /// <summary>
    /// WSP Express.
    /// </summary>
    public static readonly ShipmentCarrier Wspexpress = new("WSPEXPRESS");

    /// <summary>
    /// Kronos Express.
    /// </summary>
    public static readonly ShipmentCarrier Kronos = new("KRONOS");

    /// <summary>
    /// Total Express.
    /// </summary>
    public static readonly ShipmentCarrier TotalExpressApi = new("TOTAL_EXPRESS_API");

    /// <summary>
    /// PARCLL.
    /// </summary>
    public static readonly ShipmentCarrier Parcll = new("PARCLL");

    /// <summary>
    /// Xpedigo.
    /// </summary>
    public static readonly ShipmentCarrier Xpedigo = new("XPEDIGO");

    /// <summary>
    /// StarTrack.
    /// </summary>
    public static readonly ShipmentCarrier StarTrackWebhook = new("STAR_TRACK_WEBHOOK");

    /// <summary>
    /// Georgian Post.
    /// </summary>
    public static readonly ShipmentCarrier Gpost = new("GPOST");

    /// <summary>
    /// UCS.
    /// </summary>
    public static readonly ShipmentCarrier Ucs = new("UCS");

    /// <summary>
    /// DMF.
    /// </summary>
    public static readonly ShipmentCarrier Dmfgroup = new("DMFGROUP");

    /// <summary>
    /// Coordinadora.
    /// </summary>
    public static readonly ShipmentCarrier CoordinadoraApi = new("COORDINADORA_API");

    /// <summary>
    /// Marken.
    /// </summary>
    public static readonly ShipmentCarrier Marken = new("MARKEN");

    /// <summary>
    /// NTL logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ntl = new("NTL");

    /// <summary>
    /// Red je Pakketje.
    /// </summary>
    public static readonly ShipmentCarrier Redjepakketje = new("REDJEPAKKETJE");

    /// <summary>
    /// Allied Express (FTP).
    /// </summary>
    public static readonly ShipmentCarrier AlliedExpressFtp = new("ALLIED_EXPRESS_FTP");

    /// <summary>
    /// Mondial Relay Spain(Punto Pack).
    /// </summary>
    public static readonly ShipmentCarrier MondialrelayEs = new("MONDIALRELAY_ES");

    /// <summary>
    /// Naeko Logistics.
    /// </summary>
    public static readonly ShipmentCarrier NaekoFtp = new("NAEKO_FTP");

    /// <summary>
    /// Mhi.
    /// </summary>
    public static readonly ShipmentCarrier Mhi = new("MHI");

    /// <summary>
    /// Shippify, Inc.
    /// </summary>
    public static readonly ShipmentCarrier Shippify = new("SHIPPIFY");

    /// <summary>
    /// Malca Amit.
    /// </summary>
    public static readonly ShipmentCarrier MalcaAmitApi = new("MALCA_AMIT_API");

    /// <summary>
    /// J&amp;T Express Singapore.
    /// </summary>
    public static readonly ShipmentCarrier JtexpressSgApi = new("JTEXPRESS_SG_API");

    /// <summary>
    /// DACHSER.
    /// </summary>
    public static readonly ShipmentCarrier DachserWeb = new("DACHSER_WEB");

    /// <summary>
    /// Flight Logistics Group.
    /// </summary>
    public static readonly ShipmentCarrier Flightlg = new("FLIGHTLG");

    /// <summary>
    /// Cago.
    /// </summary>
    public static readonly ShipmentCarrier Cago = new("CAGO");

    /// <summary>
    /// ComOne Express.
    /// </summary>
    public static readonly ShipmentCarrier Com1Express = new("COM1EXPRESS");

    /// <summary>
    /// Tonami.
    /// </summary>
    public static readonly ShipmentCarrier TonamiFtp = new("TONAMI_FTP");

    /// <summary>
    /// PACKFLEET.
    /// </summary>
    public static readonly ShipmentCarrier Packfleet = new("PACKFLEET");

    /// <summary>
    /// Purolator International.
    /// </summary>
    public static readonly ShipmentCarrier PurolatorInternational = new("PUROLATOR_INTERNATIONAL");

    /// <summary>
    /// Wineshipping.
    /// </summary>
    public static readonly ShipmentCarrier WineshippingWebhook = new("WINESHIPPING_WEBHOOK");

    /// <summary>
    /// DHL Spain Domestic.
    /// </summary>
    public static readonly ShipmentCarrier DhlEsSftp = new("DHL_ES_SFTP");

    /// <summary>
    /// 網家速配股份有限公司.
    /// </summary>
    public static readonly ShipmentCarrier PchomeApi = new("PCHOME_API");

    /// <summary>
    /// Czech Post.
    /// </summary>
    public static readonly ShipmentCarrier CeskapostaApi = new("CESKAPOSTA_API");

    /// <summary>
    /// Go Rush.
    /// </summary>
    public static readonly ShipmentCarrier Gorush = new("GORUSH");

    /// <summary>
    /// HomeRunner.
    /// </summary>
    public static readonly ShipmentCarrier Homerunner = new("HOMERUNNER");

    /// <summary>
    /// Amazon order.
    /// </summary>
    public static readonly ShipmentCarrier AmazonOrder = new("AMAZON_ORDER");

    /// <summary>
    /// Estes Forwarding Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier EfwnowApi = new("EFWNOW_API");

    /// <summary>
    /// CBL Logistica (API).
    /// </summary>
    public static readonly ShipmentCarrier CblLogisticaApi = new("CBL_LOGISTICA_API");

    /// <summary>
    /// NimbusPost.
    /// </summary>
    public static readonly ShipmentCarrier Nimbuspost = new("NIMBUSPOST");

    /// <summary>
    /// Logwin Logistics.
    /// </summary>
    public static readonly ShipmentCarrier LogwinLogistics = new("LOGWIN_LOGISTICS");

    /// <summary>
    /// Sequoialog.
    /// </summary>
    public static readonly ShipmentCarrier NowlogApi = new("NOWLOG_API");

    /// <summary>
    /// DPD Netherlands.
    /// </summary>
    public static readonly ShipmentCarrier DpdNl = new("DPD_NL");

    /// <summary>
    /// Dependable Supply Chain Services.
    /// </summary>
    public static readonly ShipmentCarrier Godependable = new("GODEPENDABLE");

    /// <summary>
    /// Top Ideal Express.
    /// </summary>
    public static readonly ShipmentCarrier Esdex = new("ESDEX");

    /// <summary>
    /// Kiitäjät.
    /// </summary>
    public static readonly ShipmentCarrier LogisystemsSftp = new("LOGISYSTEMS_SFTP");

    /// <summary>
    /// Expeditors.
    /// </summary>
    public static readonly ShipmentCarrier Expeditors = new("EXPEDITORS");

    /// <summary>
    /// Snt Global Etrax.
    /// </summary>
    public static readonly ShipmentCarrier SntglobalApi = new("SNTGLOBAL_API");

    /// <summary>
    /// ShipX.
    /// </summary>
    public static readonly ShipmentCarrier Shipx = new("SHIPX");

    /// <summary>
    /// Quickstat Courier LLC.
    /// </summary>
    public static readonly ShipmentCarrier QintlApi = new("QINTL_API");

    /// <summary>
    /// Packs.
    /// </summary>
    public static readonly ShipmentCarrier Packs = new("PACKS");

    /// <summary>
    /// PostNL International.
    /// </summary>
    public static readonly ShipmentCarrier PostnlInternational = new("POSTNL_INTERNATIONAL");

    /// <summary>
    /// Amazon.
    /// </summary>
    public static readonly ShipmentCarrier AmazonEmailPush = new("AMAZON_EMAIL_PUSH");

    /// <summary>
    /// DHL.
    /// </summary>
    public static readonly ShipmentCarrier DhlApi = new("DHL_API");

    /// <summary>
    /// Shopee Express.
    /// </summary>
    public static readonly ShipmentCarrier Spx = new("SPX");

    /// <summary>
    /// AxleHire.
    /// </summary>
    public static readonly ShipmentCarrier Axlehire = new("AXLEHIRE");

    /// <summary>
    /// ICS COURIER.
    /// </summary>
    public static readonly ShipmentCarrier Icscourier = new("ICSCOURIER");

    /// <summary>
    /// Dialogo Logistica.
    /// </summary>
    public static readonly ShipmentCarrier DialogoLogistica = new("DIALOGO_LOGISTICA");

    /// <summary>
    /// ShunBang Express.
    /// </summary>
    public static readonly ShipmentCarrier ShunbangExpress = new("SHUNBANG_EXPRESS");

    /// <summary>
    /// TCS.
    /// </summary>
    public static readonly ShipmentCarrier TcsApi = new("TCS_API");

    /// <summary>
    /// SF Express China.
    /// </summary>
    public static readonly ShipmentCarrier SfExpressCn = new("SF_EXPRESS_CN");

    /// <summary>
    /// Packeta.
    /// </summary>
    public static readonly ShipmentCarrier Packeta = new("PACKETA");

    /// <summary>
    /// Teliway SIC Express.
    /// </summary>
    public static readonly ShipmentCarrier SicTeliway = new("SIC_TELIWAY");

    /// <summary>
    /// Mondial Relay France.
    /// </summary>
    public static readonly ShipmentCarrier MondialrelayFr = new("MONDIALRELAY_FR");

    /// <summary>
    /// InTime.
    /// </summary>
    public static readonly ShipmentCarrier IntimeFtp = new("INTIME_FTP");

    /// <summary>
    /// 京东物流.
    /// </summary>
    public static readonly ShipmentCarrier JdExpress = new("JD_EXPRESS");

    /// <summary>
    /// Fastbox.
    /// </summary>
    public static readonly ShipmentCarrier Fastbox = new("FASTBOX");

    /// <summary>
    /// Patheon Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Patheon = new("PATHEON");

    /// <summary>
    /// India Post Domestic.
    /// </summary>
    public static readonly ShipmentCarrier IndiaPost = new("INDIA_POST");

    /// <summary>
    /// Tipsa Reference.
    /// </summary>
    public static readonly ShipmentCarrier TipsaRef = new("TIPSA_REF");

    /// <summary>
    /// Eco Freight.
    /// </summary>
    public static readonly ShipmentCarrier Ecofreight = new("ECOFREIGHT");

    /// <summary>
    /// VOX SOLUCION EMPRESARIAL SRL.
    /// </summary>
    public static readonly ShipmentCarrier Vox = new("VOX");

    /// <summary>
    /// Direct Freight Express.
    /// </summary>
    public static readonly ShipmentCarrier DirectfreightAuRef = new("DIRECTFREIGHT_AU_REF");

    /// <summary>
    /// Best Transport.
    /// </summary>
    public static readonly ShipmentCarrier BesttransportSftp = new("BESTTRANSPORT_SFTP");

    /// <summary>
    /// Australia Post.
    /// </summary>
    public static readonly ShipmentCarrier AustraliaPostApi = new("AUSTRALIA_POST_API");

    /// <summary>
    /// FragilePAK.
    /// </summary>
    public static readonly ShipmentCarrier FragilepakSftp = new("FRAGILEPAK_SFTP");

    /// <summary>
    /// FlipXpress.
    /// </summary>
    public static readonly ShipmentCarrier Flipxp = new("FLIPXP");

    /// <summary>
    /// Value Logistics.
    /// </summary>
    public static readonly ShipmentCarrier ValueWebhook = new("VALUE_WEBHOOK");

    /// <summary>
    /// Daeshin.
    /// </summary>
    public static readonly ShipmentCarrier Daeshin = new("DAESHIN");

    /// <summary>
    /// Sherpa.
    /// </summary>
    public static readonly ShipmentCarrier Sherpa = new("SHERPA");

    /// <summary>
    /// Metropolitan Warehouse &amp; Delivery.
    /// </summary>
    public static readonly ShipmentCarrier MwdApi = new("MWD_API");

    /// <summary>
    /// SmartKargo.
    /// </summary>
    public static readonly ShipmentCarrier Smartkargo = new("SMARTKARGO");

    /// <summary>
    /// DNJ Express.
    /// </summary>
    public static readonly ShipmentCarrier DnjExpress = new("DNJ_EXPRESS");

    /// <summary>
    /// Go People.
    /// </summary>
    public static readonly ShipmentCarrier Gopeople = new("GOPEOPLE");

    /// <summary>
    /// mySendle.
    /// </summary>
    public static readonly ShipmentCarrier MysendleApi = new("MYSENDLE_API");

    /// <summary>
    /// Aramex.
    /// </summary>
    public static readonly ShipmentCarrier AramexApi = new("ARAMEX_API");

    /// <summary>
    /// Pidge.
    /// </summary>
    public static readonly ShipmentCarrier Pidge = new("PIDGE");

    /// <summary>
    /// TP Logistic.
    /// </summary>
    public static readonly ShipmentCarrier Thaiparcels = new("THAIPARCELS");

    /// <summary>
    /// Panther Reference.
    /// </summary>
    public static readonly ShipmentCarrier PantherReferenceApi = new("PANTHER_REFERENCE_API");

    /// <summary>
    /// Posta Plus.
    /// </summary>
    public static readonly ShipmentCarrier Postaplus = new("POSTAPLUS");

    /// <summary>
    /// BUFFALO.
    /// </summary>
    public static readonly ShipmentCarrier Buffalo = new("BUFFALO");

    /// <summary>
    /// U-ENVIOS.
    /// </summary>
    public static readonly ShipmentCarrier UEnvios = new("U_ENVIOS");

    /// <summary>
    /// Elite Express.
    /// </summary>
    public static readonly ShipmentCarrier EliteCo = new("ELITE_CO");

    /// <summary>
    /// Roche Internal Courier.
    /// </summary>
    public static readonly ShipmentCarrier RocheInternalSftp = new("ROCHE_INTERNAL_SFTP");

    /// <summary>
    /// DB Schenker Iceland.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerIceland = new("DBSCHENKER_ICELAND");

    /// <summary>
    /// TNT France Reference.
    /// </summary>
    public static readonly ShipmentCarrier TntFrReference = new("TNT_FR_REFERENCE");

    /// <summary>
    /// Newgistics API.
    /// </summary>
    public static readonly ShipmentCarrier Newgisticsapi = new("NEWGISTICSAPI");

    /// <summary>
    /// Glovo.
    /// </summary>
    public static readonly ShipmentCarrier Glovo = new("GLOVO");

    /// <summary>
    /// G.I.G.
    /// </summary>
    public static readonly ShipmentCarrier GwlogisApi = new("GWLOGIS_API");

    /// <summary>
    /// Spreetail.
    /// </summary>
    public static readonly ShipmentCarrier SpreetailApi = new("SPREETAIL_API");

    /// <summary>
    /// Moova.
    /// </summary>
    public static readonly ShipmentCarrier Moova = new("MOOVA");

    /// <summary>
    /// Plycon Transportation Group.
    /// </summary>
    public static readonly ShipmentCarrier Plycongroup = new("PLYCONGROUP");

    /// <summary>
    /// USPS Informed Visibility - Webhook.
    /// </summary>
    public static readonly ShipmentCarrier UspsWebhook = new("USPS_WEBHOOK");

    /// <summary>
    /// maergo.
    /// </summary>
    public static readonly ShipmentCarrier Reimaginedelivery = new("REIMAGINEDELIVERY");

    /// <summary>
    /// Eurodifarm.
    /// </summary>
    public static readonly ShipmentCarrier EdfFtp = new("EDF_FTP");

    /// <summary>
    /// DAO365.
    /// </summary>
    public static readonly ShipmentCarrier Dao365 = new("DAO365");

    /// <summary>
    /// BioCair.
    /// </summary>
    public static readonly ShipmentCarrier BiocairFtp = new("BIOCAIR_FTP");

    /// <summary>
    /// Ransa.
    /// </summary>
    public static readonly ShipmentCarrier RansaWebhook = new("RANSA_WEBHOOK");

    /// <summary>
    /// SHIPXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Shipxpres = new("SHIPXPRES");

    /// <summary>
    /// Courant Plus.
    /// </summary>
    public static readonly ShipmentCarrier CourantPlusApi = new("COURANT_PLUS_API");

    /// <summary>
    /// SHIPA.
    /// </summary>
    public static readonly ShipmentCarrier Shipa = new("SHIPA");

    /// <summary>
    /// Home Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Homelogistics = new("HOMELOGISTICS");

    /// <summary>
    /// DX.
    /// </summary>
    public static readonly ShipmentCarrier Dx = new("DX");

    /// <summary>
    /// Poste Italiane Paccocelere.
    /// </summary>
    public static readonly ShipmentCarrier PosteItalianePaccocelere = new("POSTE_ITALIANE_PACCOCELERE");

    /// <summary>
    /// Toll Group.
    /// </summary>
    public static readonly ShipmentCarrier TollWebhook = new("TOLL_WEBHOOK");

    /// <summary>
    /// LCT do Brasil.
    /// </summary>
    public static readonly ShipmentCarrier LctbrApi = new("LCTBR_API");

    /// <summary>
    /// DX Freight.
    /// </summary>
    public static readonly ShipmentCarrier DxFreight = new("DX_FREIGHT");

    /// <summary>
    /// DHL Express.
    /// </summary>
    public static readonly ShipmentCarrier DhlSftp = new("DHL_SFTP");

    /// <summary>
    /// Shiprocket X.
    /// </summary>
    public static readonly ShipmentCarrier Shiprocket = new("SHIPROCKET");

    /// <summary>
    /// Uber.
    /// </summary>
    public static readonly ShipmentCarrier UberWebhook = new("UBER_WEBHOOK");

    /// <summary>
    /// Stat Overnight.
    /// </summary>
    public static readonly ShipmentCarrier Statovernight = new("STATOVERNIGHT");

    /// <summary>
    /// Burd Delivery.
    /// </summary>
    public static readonly ShipmentCarrier Burd = new("BURD");

    /// <summary>
    /// Fastship Express.
    /// </summary>
    public static readonly ShipmentCarrier Fastship = new("FASTSHIP");

    /// <summary>
    /// IB Venture.
    /// </summary>
    public static readonly ShipmentCarrier IbventureWebhook = new("IBVENTURE_WEBHOOK");

    /// <summary>
    /// Gati-KWE.
    /// </summary>
    public static readonly ShipmentCarrier GatiKweApi = new("GATI_KWE_API");

    /// <summary>
    /// CryoPDP.
    /// </summary>
    public static readonly ShipmentCarrier CryopdpFtp = new("CRYOPDP_FTP");

    /// <summary>
    /// HUBBED.
    /// </summary>
    public static readonly ShipmentCarrier Hubbed = new("HUBBED");

    /// <summary>
    /// Tipsa API.
    /// </summary>
    public static readonly ShipmentCarrier TipsaApi = new("TIPSA_API");

    /// <summary>
    /// Aras Cargo.
    /// </summary>
    public static readonly ShipmentCarrier Araskargo = new("ARASKARGO");

    /// <summary>
    /// Thijs Logistiek.
    /// </summary>
    public static readonly ShipmentCarrier ThijsNl = new("THIJS_NL");

    /// <summary>
    /// ATS Healthcare.
    /// </summary>
    public static readonly ShipmentCarrier AtshealthcareReference = new("ATSHEALTHCARE_REFERENCE");

    /// <summary>
    /// 99minutos.
    /// </summary>
    public static readonly ShipmentCarrier _99Minutos = new("99MINUTOS");

    /// <summary>
    /// Hellenic (Greece) Post.
    /// </summary>
    public static readonly ShipmentCarrier HellenicPost = new("HELLENIC_POST");

    /// <summary>
    /// HSM Global.
    /// </summary>
    public static readonly ShipmentCarrier HsmGlobal = new("HSM_GLOBAL");

    /// <summary>
    /// MNX.
    /// </summary>
    public static readonly ShipmentCarrier Mnx = new("MNX");

    /// <summary>
    /// N&amp;M Transfer Co., Inc..
    /// </summary>
    public static readonly ShipmentCarrier Nmtransfer = new("NMTRANSFER");

    /// <summary>
    /// Logysto.
    /// </summary>
    public static readonly ShipmentCarrier Logysto = new("LOGYSTO");

    /// <summary>
    /// India Post International.
    /// </summary>
    public static readonly ShipmentCarrier IndiaPostInt = new("INDIA_POST_INT");

    /// <summary>
    /// Swiship IN.
    /// </summary>
    public static readonly ShipmentCarrier AmazonFbaSwishipIn = new("AMAZON_FBA_SWISHIP_IN");

    /// <summary>
    /// SRT Transport.
    /// </summary>
    public static readonly ShipmentCarrier SrtTransport = new("SRT_TRANSPORT");

    /// <summary>
    /// Bomi Group.
    /// </summary>
    public static readonly ShipmentCarrier Bomi = new("BOMI");

    /// <summary>
    /// Deliverr.
    /// </summary>
    public static readonly ShipmentCarrier DeliverrSftp = new("DELIVERR_SFTP");

    /// <summary>
    /// HSDEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Hsdexpress = new("HSDEXPRESS");

    /// <summary>
    /// SimpleTire.
    /// </summary>
    public static readonly ShipmentCarrier SimpletireWebhook = new("SIMPLETIRE_WEBHOOK");

    /// <summary>
    /// Hunter Express.
    /// </summary>
    public static readonly ShipmentCarrier HunterExpressSftp = new("HUNTER_EXPRESS_SFTP");

    /// <summary>
    /// UPS.
    /// </summary>
    public static readonly ShipmentCarrier UpsApi = new("UPS_API");

    /// <summary>
    /// WOO YOUNG LOGISTICS CO.,LTD..
    /// </summary>
    public static readonly ShipmentCarrier WooyoungLogisticsSftp = new("WOOYOUNG_LOGISTICS_SFTP");

    /// <summary>
    /// PHSE.
    /// </summary>
    public static readonly ShipmentCarrier PhseApi = new("PHSE_API");

    /// <summary>
    /// Wish.
    /// </summary>
    public static readonly ShipmentCarrier WishEmailPush = new("WISH_EMAIL_PUSH");

    /// <summary>
    /// Northline.
    /// </summary>
    public static readonly ShipmentCarrier Northline = new("NORTHLINE");

    /// <summary>
    /// Med Africa Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Medafrica = new("MEDAFRICA");

    /// <summary>
    /// DPD Austria.
    /// </summary>
    public static readonly ShipmentCarrier DpdAtSftp = new("DPD_AT_SFTP");

    /// <summary>
    /// Anteraja.
    /// </summary>
    public static readonly ShipmentCarrier Anteraja = new("ANTERAJA");

    /// <summary>
    /// DHL Global Forwarding API.
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalForwardingApi = new("DHL_GLOBAL_FORWARDING_API");

    /// <summary>
    /// LBC EXPRESS INC..
    /// </summary>
    public static readonly ShipmentCarrier LbcexpressApi = new("LBCEXPRESS_API");

    /// <summary>
    /// Sims Global.
    /// </summary>
    public static readonly ShipmentCarrier Simsglobal = new("SIMSGLOBAL");

    /// <summary>
    /// CDL Last Mile.
    /// </summary>
    public static readonly ShipmentCarrier Cdldelivers = new("CDLDELIVERS");

    /// <summary>
    /// TYP.
    /// </summary>
    public static readonly ShipmentCarrier Typ = new("TYP");

    /// <summary>
    /// Testing Courier.
    /// </summary>
    public static readonly ShipmentCarrier TestingCourierWebhook = new("TESTING_COURIER_WEBHOOK");

    /// <summary>
    /// Pandago.
    /// </summary>
    public static readonly ShipmentCarrier PandagoApi = new("PANDAGO_API");

    /// <summary>
    /// Royal Mail.
    /// </summary>
    public static readonly ShipmentCarrier RoyalMailFtp = new("ROYAL_MAIL_FTP");

    /// <summary>
    /// Thunder Express Australia.
    /// </summary>
    public static readonly ShipmentCarrier Thunderexpress = new("THUNDEREXPRESS");

    /// <summary>
    /// Secretlab.
    /// </summary>
    public static readonly ShipmentCarrier SecretlabWebhook = new("SECRETLAB_WEBHOOK");

    /// <summary>
    /// Setel Express.
    /// </summary>
    public static readonly ShipmentCarrier Setel = new("SETEL");

    /// <summary>
    /// JD Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier JdWorldwide = new("JD_WORLDWIDE");

    /// <summary>
    /// DPD Russia.
    /// </summary>
    public static readonly ShipmentCarrier DpdRuApi = new("DPD_RU_API");

    /// <summary>
    /// Argents Express Group.
    /// </summary>
    public static readonly ShipmentCarrier ArgentsWebhook = new("ARGENTS_WEBHOOK");

    /// <summary>
    /// Post ONE.
    /// </summary>
    public static readonly ShipmentCarrier Postone = new("POSTONE");

    /// <summary>
    /// Tusk Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Tusklogistics = new("TUSKLOGISTICS");

    /// <summary>
    /// Rhenus Logistics UK.
    /// </summary>
    public static readonly ShipmentCarrier RhenusUkApi = new("RHENUS_UK_API");

    /// <summary>
    /// Yamato Singapore.
    /// </summary>
    public static readonly ShipmentCarrier TaqbinSgApi = new("TAQBIN_SG_API");

    /// <summary>
    /// Inntralog GmbH.
    /// </summary>
    public static readonly ShipmentCarrier InntralogSftp = new("INNTRALOG_SFTP");

    /// <summary>
    /// Day &amp; Ross.
    /// </summary>
    public static readonly ShipmentCarrier Dayross = new("DAYROSS");

    /// <summary>
    /// Correos Express (API).
    /// </summary>
    public static readonly ShipmentCarrier CorreosexpressApi = new("CORREOSEXPRESS_API");

    /// <summary>
    /// International Seur API.
    /// </summary>
    public static readonly ShipmentCarrier InternationalSeurApi = new("INTERNATIONAL_SEUR_API");

    /// <summary>
    /// Yodel API.
    /// </summary>
    public static readonly ShipmentCarrier YodelApi = new("YODEL_API");

    /// <summary>
    /// Hero Express.
    /// </summary>
    public static readonly ShipmentCarrier Heroexpress = new("HEROEXPRESS");

    /// <summary>
    /// DHL supply chain India.
    /// </summary>
    public static readonly ShipmentCarrier DhlSupplychainIn = new("DHL_SUPPLYCHAIN_IN");

    /// <summary>
    /// Urgent Cargus.
    /// </summary>
    public static readonly ShipmentCarrier UrgentCargus = new("URGENT_CARGUS");

    /// <summary>
    /// FRONTdoor Collective.
    /// </summary>
    public static readonly ShipmentCarrier Frontdoorcorp = new("FRONTDOORCORP");

    /// <summary>
    /// J&amp;T Express Philippines.
    /// </summary>
    public static readonly ShipmentCarrier JtexpressPh = new("JTEXPRESS_PH");

    /// <summary>
    /// Parcelstars.
    /// </summary>
    public static readonly ShipmentCarrier ParcelstarsWebhook = new("PARCELSTARS_WEBHOOK");

    /// <summary>
    /// DPD Slovakia.
    /// </summary>
    public static readonly ShipmentCarrier DpdSkSftp = new("DPD_SK_SFTP");

    /// <summary>
    /// Movianto.
    /// </summary>
    public static readonly ShipmentCarrier Movianto = new("MOVIANTO");

    /// <summary>
    /// Ozeparts Shipping.
    /// </summary>
    public static readonly ShipmentCarrier OzepartsShipping = new("OZEPARTS_SHIPPING");

    /// <summary>
    /// KargomKolay (CargoMini).
    /// </summary>
    public static readonly ShipmentCarrier Kargomkolay = new("KARGOMKOLAY");

    /// <summary>
    /// Trunkrs.
    /// </summary>
    public static readonly ShipmentCarrier Trunkrs = new("TRUNKRS");

    /// <summary>
    /// Omni Returns.
    /// </summary>
    public static readonly ShipmentCarrier OmnirpsWebhook = new("OMNIRPS_WEBHOOK");

    /// <summary>
    /// Chile Express.
    /// </summary>
    public static readonly ShipmentCarrier Chilexpress = new("CHILEXPRESS");

    /// <summary>
    /// Testing Courier.
    /// </summary>
    public static readonly ShipmentCarrier TestingCourier = new("TESTING_COURIER");

    /// <summary>
    /// JNE (API).
    /// </summary>
    public static readonly ShipmentCarrier JneApi = new("JNE_API");

    /// <summary>
    /// BJS Distribution, Storage &amp; Couriers - FTP.
    /// </summary>
    public static readonly ShipmentCarrier BjshomedeliveryFtp = new("BJSHOMEDELIVERY_FTP");

    /// <summary>
    /// D Express.
    /// </summary>
    public static readonly ShipmentCarrier DexpressWebhook = new("DEXPRESS_WEBHOOK");

    /// <summary>
    /// USPS API.
    /// </summary>
    public static readonly ShipmentCarrier UspsApi = new("USPS_API");

    /// <summary>
    /// TransVirtual.
    /// </summary>
    public static readonly ShipmentCarrier Transvirtual = new("TRANSVIRTUAL");

    /// <summary>
    /// solistica.
    /// </summary>
    public static readonly ShipmentCarrier SolisticaApi = new("SOLISTICA_API");

    /// <summary>
    /// Chienventure.
    /// </summary>
    public static readonly ShipmentCarrier ChienventureWebhook = new("CHIENVENTURE_WEBHOOK");

    /// <summary>
    /// DPD UK.
    /// </summary>
    public static readonly ShipmentCarrier DpdUkSftp = new("DPD_UK_SFTP");

    /// <summary>
    /// InPost.
    /// </summary>
    public static readonly ShipmentCarrier InpostUk = new("INPOST_UK");

    /// <summary>
    /// Javit.
    /// </summary>
    public static readonly ShipmentCarrier Javit = new("JAVIT");

    /// <summary>
    /// ZTO Express China.
    /// </summary>
    public static readonly ShipmentCarrier ZtoDomestic = new("ZTO_DOMESTIC");

    /// <summary>
    /// DHL Global Forwarding Guatemala.
    /// </summary>
    public static readonly ShipmentCarrier DhlGtApi = new("DHL_GT_API");

    /// <summary>
    /// CEVA Package.
    /// </summary>
    public static readonly ShipmentCarrier CevaTracking = new("CEVA_TRACKING");

    /// <summary>
    /// Komon Express.
    /// </summary>
    public static readonly ShipmentCarrier KomonExpress = new("KOMON_EXPRESS");

    /// <summary>
    /// East West Courier Pte Ltd.
    /// </summary>
    public static readonly ShipmentCarrier EastwestcourierFtp = new("EASTWESTCOURIER_FTP");

    /// <summary>
    /// Danniao.
    /// </summary>
    public static readonly ShipmentCarrier Danniao = new("DANNIAO");

    /// <summary>
    /// Spectran.
    /// </summary>
    public static readonly ShipmentCarrier Spectran = new("SPECTRAN");

    /// <summary>
    /// Deliver-iT.
    /// </summary>
    public static readonly ShipmentCarrier DeliverIt = new("DELIVER_IT");

    /// <summary>
    /// Relais Colis.
    /// </summary>
    public static readonly ShipmentCarrier Relaiscolis = new("RELAISCOLIS");

    /// <summary>
    /// GLS Spain.
    /// </summary>
    public static readonly ShipmentCarrier GlsSpainApi = new("GLS_SPAIN_API");

    /// <summary>
    /// PostPlus.
    /// </summary>
    public static readonly ShipmentCarrier Postplus = new("POSTPLUS");

    /// <summary>
    /// Airterra.
    /// </summary>
    public static readonly ShipmentCarrier Airterra = new("AIRTERRA");

    /// <summary>
    /// GIO Express Ecourier.
    /// </summary>
    public static readonly ShipmentCarrier GioEcourierApi = new("GIO_ECOURIER_API");

    /// <summary>
    /// DPD Switzerland.
    /// </summary>
    public static readonly ShipmentCarrier DpdChSftp = new("DPD_CH_SFTP");

    /// <summary>
    /// FedEx®.
    /// </summary>
    public static readonly ShipmentCarrier FedexApi = new("FEDEX_API");

    /// <summary>
    /// INTERSMARTTRANS &amp; SOLUTIONS SL.
    /// </summary>
    public static readonly ShipmentCarrier Intersmarttrans = new("INTERSMARTTRANS");

    /// <summary>
    /// Hermes UK.
    /// </summary>
    public static readonly ShipmentCarrier HermesUkSftp = new("HERMES_UK_SFTP");

    /// <summary>
    /// Exelot Ltd..
    /// </summary>
    public static readonly ShipmentCarrier ExelotFtp = new("EXELOT_FTP");

    /// <summary>
    /// DHL GLOBAL FORWARDING PANAMÁ.
    /// </summary>
    public static readonly ShipmentCarrier DhlPaApi = new("DHL_PA_API");

    /// <summary>
    /// Vir Transport.
    /// </summary>
    public static readonly ShipmentCarrier VirtransportSftp = new("VIRTRANSPORT_SFTP");

    /// <summary>
    /// Worldnet Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Worldnet = new("WORLDNET");

    /// <summary>
    /// Instabox.
    /// </summary>
    public static readonly ShipmentCarrier InstaboxWebhook = new("INSTABOX_WEBHOOK");

    /// <summary>
    /// Keuhne + Nagel Global.
    /// </summary>
    public static readonly ShipmentCarrier Kng = new("KNG");

    /// <summary>
    /// Flash Express.
    /// </summary>
    public static readonly ShipmentCarrier FlashexpressWebhook = new("FLASHEXPRESS_WEBHOOK");

    /// <summary>
    /// Magyar Posta.
    /// </summary>
    public static readonly ShipmentCarrier MagyarPostaApi = new("MAGYAR_POSTA_API");

    /// <summary>
    /// WeShip.
    /// </summary>
    public static readonly ShipmentCarrier WeshipApi = new("WESHIP_API");

    /// <summary>
    /// Ohi.
    /// </summary>
    public static readonly ShipmentCarrier OhiWebhook = new("OHI_WEBHOOK");

    /// <summary>
    /// MUDITA.
    /// </summary>
    public static readonly ShipmentCarrier Mudita = new("MUDITA");

    /// <summary>
    /// Bluedart.
    /// </summary>
    public static readonly ShipmentCarrier BluedartApi = new("BLUEDART_API");

    /// <summary>
    /// T-cat.
    /// </summary>
    public static readonly ShipmentCarrier TCatApi = new("T_CAT_API");

    /// <summary>
    /// ADS Express.
    /// </summary>
    public static readonly ShipmentCarrier Ads = new("ADS");

    /// <summary>
    /// HR Parcel.
    /// </summary>
    public static readonly ShipmentCarrier HermesIt = new("HERMES_IT");

    /// <summary>
    /// FitzMark.
    /// </summary>
    public static readonly ShipmentCarrier FitzmarkApi = new("FITZMARK_API");

    /// <summary>
    /// Posti API.
    /// </summary>
    public static readonly ShipmentCarrier PostiApi = new("POSTI_API");

    /// <summary>
    /// SMSA Express.
    /// </summary>
    public static readonly ShipmentCarrier SmsaExpressWebhook = new("SMSA_EXPRESS_WEBHOOK");

    /// <summary>
    /// Tamer Logistics.
    /// </summary>
    public static readonly ShipmentCarrier TamergroupWebhook = new("TAMERGROUP_WEBHOOK");

    /// <summary>
    /// Livrapide.
    /// </summary>
    public static readonly ShipmentCarrier Livrapide = new("LIVRAPIDE");

    /// <summary>
    /// Nippon Express.
    /// </summary>
    public static readonly ShipmentCarrier NipponExpress = new("NIPPON_EXPRESS");

    /// <summary>
    /// Better Trucks.
    /// </summary>
    public static readonly ShipmentCarrier Bettertrucks = new("BETTERTRUCKS");

    /// <summary>
    /// FAN COURIER EXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Fan = new("FAN");

    /// <summary>
    /// USPS Flats (Pitney Bowes).
    /// </summary>
    public static readonly ShipmentCarrier PbUspsflatsFtp = new("PB_USPSFLATS_FTP");

    /// <summary>
    /// Parcel Right.
    /// </summary>
    public static readonly ShipmentCarrier Parcelright = new("PARCELRIGHT");

    /// <summary>
    /// iThink Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ithinklogistics = new("ITHINKLOGISTICS");

    /// <summary>
    /// Kerry Logistics.
    /// </summary>
    public static readonly ShipmentCarrier KerryExpressThWebhook = new("KERRY_EXPRESS_TH_WEBHOOK");

    /// <summary>
    /// eCoutier.
    /// </summary>
    public static readonly ShipmentCarrier Ecoutier = new("ECOUTIER");

    /// <summary>
    /// SENHONG INTERNATIONAL LOGISTICS.
    /// </summary>
    public static readonly ShipmentCarrier Showl = new("SHOWL");

    /// <summary>
    /// BRT Bartolini API.
    /// </summary>
    public static readonly ShipmentCarrier BrtItApi = new("BRT_IT_API");

    /// <summary>
    /// Rixon Logistics.
    /// </summary>
    public static readonly ShipmentCarrier RixonhkApi = new("RIXONHK_API");

    /// <summary>
    /// DB Schenker.
    /// </summary>
    public static readonly ShipmentCarrier DbschenkerApi = new("DBSCHENKER_API");

    /// <summary>
    /// Ilyang logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ilyanglogis = new("ILYANGLOGIS");

    /// <summary>
    /// Mail Boxes Etc..
    /// </summary>
    public static readonly ShipmentCarrier MailBoxEtc = new("MAIL_BOX_ETC");

    /// <summary>
    /// WeShip.
    /// </summary>
    public static readonly ShipmentCarrier Weship = new("WESHIP");

    /// <summary>
    /// DHL eCommerce Solutions.
    /// </summary>
    public static readonly ShipmentCarrier DhlGlobalMailApi = new("DHL_GLOBAL_MAIL_API");

    /// <summary>
    /// Activos24.
    /// </summary>
    public static readonly ShipmentCarrier Activos24Api = new("ACTIVOS24_API");

    /// <summary>
    /// ATS Healthcare.
    /// </summary>
    public static readonly ShipmentCarrier Atshealthcare = new("ATSHEALTHCARE");

    /// <summary>
    /// Luwjistik.
    /// </summary>
    public static readonly ShipmentCarrier Luwjistik = new("LUWJISTIK");

    /// <summary>
    /// Gebrüder Weiss.
    /// </summary>
    public static readonly ShipmentCarrier GwWorld = new("GW_WORLD");

    /// <summary>
    /// fairsenden.
    /// </summary>
    public static readonly ShipmentCarrier FairsendenApi = new("FAIRSENDEN_API");

    /// <summary>
    /// SerVIP.
    /// </summary>
    public static readonly ShipmentCarrier ServipWebhook = new("SERVIP_WEBHOOK");

    /// <summary>
    /// Swiship.
    /// </summary>
    public static readonly ShipmentCarrier Swiship = new("SWISHIP");

    /// <summary>
    /// Transport Ambientales.
    /// </summary>
    public static readonly ShipmentCarrier Tanet = new("TANET");

    /// <summary>
    /// SHENZHEN HOTSIN CARGO INT'L FORWARDING CO.,LTD.
    /// </summary>
    public static readonly ShipmentCarrier HotsinCargo = new("HOTSIN_CARGO");

    /// <summary>
    /// Direx.
    /// </summary>
    public static readonly ShipmentCarrier Direx = new("DIREX");

    /// <summary>
    /// HuanTong.
    /// </summary>
    public static readonly ShipmentCarrier Huantong = new("HUANTONG");

    /// <summary>
    /// iMile.
    /// </summary>
    public static readonly ShipmentCarrier ImileApi = new("IMILE_API");

    /// <summary>
    /// Au Express.
    /// </summary>
    public static readonly ShipmentCarrier Auexpress = new("AUEXPRESS");

    /// <summary>
    /// NYT SUPPLY CHAIN LOGISTICS Co.,LTD.
    /// </summary>
    public static readonly ShipmentCarrier Nytlogistics = new("NYTLOGISTICS");

    /// <summary>
    /// DSV Futurewave.
    /// </summary>
    public static readonly ShipmentCarrier DsvReference = new("DSV_REFERENCE");

    /// <summary>
    /// Novofarma.
    /// </summary>
    public static readonly ShipmentCarrier NovofarmaWebhook = new("NOVOFARMA_WEBHOOK");

    /// <summary>
    /// AIT.
    /// </summary>
    public static readonly ShipmentCarrier AitworldwideSftp = new("AITWORLDWIDE_SFTP");

    /// <summary>
    /// Olive.
    /// </summary>
    public static readonly ShipmentCarrier Shopolive = new("SHOPOLIVE");

    /// <summary>
    /// Fast &amp; Furious.
    /// </summary>
    public static readonly ShipmentCarrier FnfZa = new("FNF_ZA");

    /// <summary>
    /// DHL eCommerce Greater China.
    /// </summary>
    public static readonly ShipmentCarrier DhlEcommerceGc = new("DHL_ECOMMERCE_GC");

    /// <summary>
    /// Fetchr.
    /// </summary>
    public static readonly ShipmentCarrier Fetchr = new("FETCHR");

    /// <summary>
    /// Starlinks Global.
    /// </summary>
    public static readonly ShipmentCarrier StarlinksApi = new("STARLINKS_API");

    /// <summary>
    /// YYEXPRESS.
    /// </summary>
    public static readonly ShipmentCarrier Yyexpress = new("YYEXPRESS");

    /// <summary>
    /// Servientrega.
    /// </summary>
    public static readonly ShipmentCarrier Servientrega = new("SERVIENTREGA");

    /// <summary>
    /// HanJin.
    /// </summary>
    public static readonly ShipmentCarrier Hanjin = new("HANJIN");

    /// <summary>
    /// Spanish Seur.
    /// </summary>
    public static readonly ShipmentCarrier SpanishSeurFtp = new("SPANISH_SEUR_FTP");

    /// <summary>
    /// DX (B2B).
    /// </summary>
    public static readonly ShipmentCarrier DxB2BConnum = new("DX_B2B_CONNUM");

    /// <summary>
    /// Helthjem.
    /// </summary>
    public static readonly ShipmentCarrier HelthjemApi = new("HELTHJEM_API");

    /// <summary>
    /// Inexpost.
    /// </summary>
    public static readonly ShipmentCarrier Inexpost = new("INEXPOST");

    /// <summary>
    /// A2B Express Logistics.
    /// </summary>
    public static readonly ShipmentCarrier A2BBa = new("A2B_BA");

    /// <summary>
    /// Rhenus Logistics.
    /// </summary>
    public static readonly ShipmentCarrier RhenusGroup = new("RHENUS_GROUP");

    /// <summary>
    /// Sber Logistics.
    /// </summary>
    public static readonly ShipmentCarrier SberlogisticsRu = new("SBERLOGISTICS_RU");

    /// <summary>
    /// Malca-Amit.
    /// </summary>
    public static readonly ShipmentCarrier MalcaAmit = new("MALCA_AMIT");

    /// <summary>
    /// Professional Parcel Logistics.
    /// </summary>
    public static readonly ShipmentCarrier Ppl = new("PPL");

    /// <summary>
    /// OSM Worldwide.
    /// </summary>
    public static readonly ShipmentCarrier OsmWorldwideSftp = new("OSM_WORLDWIDE_SFTP");

    /// <summary>
    /// ACI Logistix.
    /// </summary>
    public static readonly ShipmentCarrier Acilogistix = new("ACILOGISTIX");

    /// <summary>
    /// Optima Courier.
    /// </summary>
    public static readonly ShipmentCarrier Optimacourier = new("OPTIMACOURIER");

    /// <summary>
    /// Nova Poshta API.
    /// </summary>
    public static readonly ShipmentCarrier NovaPoshtaApi = new("NOVA_POSHTA_API");

    /// <summary>
    /// Loggi.
    /// </summary>
    public static readonly ShipmentCarrier Loggi = new("LOGGI");

    /// <summary>
    /// YiFan Express.
    /// </summary>
    public static readonly ShipmentCarrier Yifan = new("YIFAN");

    /// <summary>
    /// My DynaLogic.
    /// </summary>
    public static readonly ShipmentCarrier Mydynalogic = new("MYDYNALOGIC");

    /// <summary>
    /// Morning Global.
    /// </summary>
    public static readonly ShipmentCarrier Morninglobal = new("MORNINGLOBAL");

    /// <summary>
    /// Concise.
    /// </summary>
    public static readonly ShipmentCarrier ConciseApi = new("CONCISE_API");

    /// <summary>
    /// Falcon Express.
    /// </summary>
    public static readonly ShipmentCarrier Fxtran = new("FXTRAN");

    /// <summary>
    /// Deliver Your Parcel.
    /// </summary>
    public static readonly ShipmentCarrier DeliveryourparcelZa = new("DELIVERYOURPARCEL_ZA");

    /// <summary>
    /// uParcel.
    /// </summary>
    public static readonly ShipmentCarrier Uparcel = new("UPARCEL");

    /// <summary>
    /// Mobi Logistica.
    /// </summary>
    public static readonly ShipmentCarrier MobiBr = new("MOBI_BR");

    /// <summary>
    /// T&amp;W Delivery.
    /// </summary>
    public static readonly ShipmentCarrier LoginextWebhook = new("LOGINEXT_WEBHOOK");

    /// <summary>
    /// EMS.
    /// </summary>
    public static readonly ShipmentCarrier Ems = new("EMS");

    /// <summary>
    /// Speedy.
    /// </summary>
    public static readonly ShipmentCarrier Speedy = new("SPEEDY");

    /// <summary>
    /// Zoom.
    /// </summary>
    public static readonly ShipmentCarrier ZoomRed = new("ZOOM_RED");

    /// <summary>
    /// Navlungo.
    /// </summary>
    public static readonly ShipmentCarrier Navlungo = new("NAVLUNGO");

    /// <summary>
    /// Castle Parcels.
    /// </summary>
    public static readonly ShipmentCarrier Castleparcels = new("CASTLEPARCELS");

    /// <summary>
    /// Weee.
    /// </summary>
    public static readonly ShipmentCarrier Weee = new("WEEE");

    /// <summary>
    /// Packaly.
    /// </summary>
    public static readonly ShipmentCarrier Packaly = new("PACKALY");

    /// <summary>
    /// Yunhuipost.
    /// </summary>
    public static readonly ShipmentCarrier Yunhuipost = new("YUNHUIPOST");

    /// <summary>
    /// YouParcel.
    /// </summary>
    public static readonly ShipmentCarrier Youparcel = new("YOUPARCEL");

    /// <summary>
    /// Leman.
    /// </summary>
    public static readonly ShipmentCarrier Leman = new("LEMAN");

    /// <summary>
    /// Moovin.
    /// </summary>
    public static readonly ShipmentCarrier Moovin = new("MOOVIN");

    /// <summary>
    /// Urb-it.
    /// </summary>
    public static readonly ShipmentCarrier UrbIt = new("URB_IT");

    /// <summary>
    /// Multientrega.
    /// </summary>
    public static readonly ShipmentCarrier Multientregapanama = new("MULTIENTREGAPANAMA");

    /// <summary>
    /// Jusdasr.
    /// </summary>
    public static readonly ShipmentCarrier Jusdasr = new("JUSDASR");

    /// <summary>
    /// Discount Post.
    /// </summary>
    public static readonly ShipmentCarrier Discountpost = new("DISCOUNTPOST");

    /// <summary>
    /// Rhenus Logistics UK.
    /// </summary>
    public static readonly ShipmentCarrier RhenusUk = new("RHENUS_UK");

    /// <summary>
    /// Swiship JP.
    /// </summary>
    public static readonly ShipmentCarrier SwishipJp = new("SWISHIP_JP");

    /// <summary>
    /// GLS USA.
    /// </summary>
    public static readonly ShipmentCarrier GlsUs = new("GLS_US");

    /// <summary>
    /// Southwestern Motor Transport. Inc.
    /// </summary>
    public static readonly ShipmentCarrier Smtl = new("SMTL");

    /// <summary>
    /// Discount Post Emega.
    /// </summary>
    public static readonly ShipmentCarrier Emega = new("EMEGA");

    /// <summary>
    /// EXPRESSONE Slovenia.
    /// </summary>
    public static readonly ShipmentCarrier ExpressoneSv = new("EXPRESSONE_SV");

    /// <summary>
    /// hepsiJET.
    /// </summary>
    public static readonly ShipmentCarrier Hepsijet = new("HEPSIJET");

    /// <summary>
    /// Welivery.
    /// </summary>
    public static readonly ShipmentCarrier Welivery = new("WELIVERY");

    /// <summary>
    /// Bringer Parcel Services.
    /// </summary>
    public static readonly ShipmentCarrier Bringer = new("BRINGER");

    /// <summary>
    /// EasyRoutes.
    /// </summary>
    public static readonly ShipmentCarrier Easyroutes = new("EASYROUTES");

    /// <summary>
    /// MRW.
    /// </summary>
    public static readonly ShipmentCarrier Mrw = new("MRW");

    /// <summary>
    /// RPM.
    /// </summary>
    public static readonly ShipmentCarrier Rpm = new("RPM");

    /// <summary>
    /// DPD Portugal.
    /// </summary>
    public static readonly ShipmentCarrier DpdPrt = new("DPD_PRT");

    /// <summary>
    /// GLS Romania.
    /// </summary>
    public static readonly ShipmentCarrier GlsRomania = new("GLS_ROMANIA");

    /// <summary>
    /// LM Parcel.
    /// </summary>
    public static readonly ShipmentCarrier Lmparcel = new("LMPARCEL");

    /// <summary>
    /// GTA GSM.
    /// </summary>
    public static readonly ShipmentCarrier Gtagsm = new("GTAGSM");

    /// <summary>
    /// DOMINO.
    /// </summary>
    public static readonly ShipmentCarrier Domino = new("DOMINO");

    /// <summary>
    /// eShipper.
    /// </summary>
    public static readonly ShipmentCarrier Eshipper = new("ESHIPPER");

    /// <summary>
    /// Transpak Inc..
    /// </summary>
    public static readonly ShipmentCarrier Transpak = new("TRANSPAK");

    /// <summary>
    /// Xindus.
    /// </summary>
    public static readonly ShipmentCarrier Xindus = new("XINDUS");

    /// <summary>
    /// Aoyue.
    /// </summary>
    public static readonly ShipmentCarrier Aoyue = new("AOYUE");

    /// <summary>
    /// Easyparcel.
    /// </summary>
    public static readonly ShipmentCarrier Easyparcel = new("EASYPARCEL");

    /// <summary>
    /// EXPRESSONE.
    /// </summary>
    public static readonly ShipmentCarrier Expressone = new("EXPRESSONE");

    /// <summary>
    /// Sendeo Kargo.
    /// </summary>
    public static readonly ShipmentCarrier SendeoKargo = new("SENDEO_KARGO");

    /// <summary>
    /// Speedaf Express.
    /// </summary>
    public static readonly ShipmentCarrier Speedaf = new("SPEEDAF");

    /// <summary>
    /// eTower.
    /// </summary>
    public static readonly ShipmentCarrier Etower = new("ETOWER");

    /// <summary>
    /// GC Express.
    /// </summary>
    public static readonly ShipmentCarrier Gcx = new("GCX");

    /// <summary>
    /// Ninjavan Vietnam.
    /// </summary>
    public static readonly ShipmentCarrier NinjavanVn = new("NINJAVAN_VN");

    /// <summary>
    /// Allegro.
    /// </summary>
    public static readonly ShipmentCarrier Allegro = new("ALLEGRO");

    /// <summary>
    /// Jumppoint.
    /// </summary>
    public static readonly ShipmentCarrier Jumppoint = new("JUMPPOINT");

    /// <summary>
    /// ShipGlobal.
    /// </summary>
    public static readonly ShipmentCarrier ShipglobalUs = new("SHIPGLOBAL_US");

    /// <summary>
    /// Kinisi Transport Pty Ltd.
    /// </summary>
    public static readonly ShipmentCarrier Kinisi = new("KINISI");

    /// <summary>
    /// Oakh Harbour Freight Lines.
    /// </summary>
    public static readonly ShipmentCarrier Oakh = new("OAKH");

    /// <summary>
    /// American West.
    /// </summary>
    public static readonly ShipmentCarrier Awest = new("AWEST");

    /// <summary>
    /// Barsan Global Lojistik.
    /// </summary>
    public static readonly ShipmentCarrier Barsan = new("BARSAN");

    /// <summary>
    /// Energo Logistic.
    /// </summary>
    public static readonly ShipmentCarrier Energologistic = new("ENERGOLOGISTIC");

    /// <summary>
    /// Madrooex.
    /// </summary>
    public static readonly ShipmentCarrier Madrooex = new("MADROOEX");

    /// <summary>
    /// GoBolt.
    /// </summary>
    public static readonly ShipmentCarrier Gobolt = new("GOBOLT");

    /// <summary>
    /// Swiss Universal Express.
    /// </summary>
    public static readonly ShipmentCarrier SwissUniversalExpress = new("SWISS_UNIVERSAL_EXPRESS");

    /// <summary>
    /// IOR Direct Solutions.
    /// </summary>
    public static readonly ShipmentCarrier Iordirect = new("IORDIRECT");

    /// <summary>
    /// xmszm.
    /// </summary>
    public static readonly ShipmentCarrier Xmszm = new("XMSZM");

    /// <summary>
    /// GLS Hungary.
    /// </summary>
    public static readonly ShipmentCarrier GlsHun = new("GLS_HUN");

    /// <summary>
    /// Sendy Express.
    /// </summary>
    public static readonly ShipmentCarrier Sendy = new("SENDY");

    /// <summary>
    /// Brauns Express.
    /// </summary>
    public static readonly ShipmentCarrier Braunsexpress = new("BRAUNSEXPRESS");

    /// <summary>
    /// Grand Slam Express.
    /// </summary>
    public static readonly ShipmentCarrier Grandslamexpress = new("GRANDSLAMEXPRESS");

    /// <summary>
    /// XGS.
    /// </summary>
    public static readonly ShipmentCarrier Xgs = new("XGS");

    /// <summary>
    /// OTS.
    /// </summary>
    public static readonly ShipmentCarrier Otschile = new("OTSCHILE");

    /// <summary>
    /// Pack-Up.
    /// </summary>
    public static readonly ShipmentCarrier PackUp = new("PACK_UP");

    /// <summary>
    /// Parcelstars.
    /// </summary>
    public static readonly ShipmentCarrier Parcelstars = new("PARCELSTARS");

    /// <summary>
    /// Team Express Service LLC.
    /// </summary>
    public static readonly ShipmentCarrier Teamexpressllc = new("TEAMEXPRESSLLC");

    /// <summary>
    /// Asyad Express.
    /// </summary>
    public static readonly ShipmentCarrier Asyadexpress = new("ASYADEXPRESS");

    /// <summary>
    /// TDN.
    /// </summary>
    public static readonly ShipmentCarrier Tdn = new("TDN");

    /// <summary>
    /// Early Bird.
    /// </summary>
    public static readonly ShipmentCarrier Earlybird = new("EARLYBIRD");

    /// <summary>
    /// Cacesa.
    /// </summary>
    public static readonly ShipmentCarrier Cacesa = new("CACESA");

    /// <summary>
    /// Parceljet.
    /// </summary>
    public static readonly ShipmentCarrier Parceljet = new("PARCELJET");

    /// <summary>
    /// MNG Kargo.
    /// </summary>
    public static readonly ShipmentCarrier MngKargo = new("MNG_KARGO");

    /// <summary>
    /// Super Pac Line.
    /// </summary>
    public static readonly ShipmentCarrier Superpackline = new("SUPERPACKLINE");

    /// <summary>
    /// SpeedX.
    /// </summary>
    public static readonly ShipmentCarrier Speedx = new("SPEEDX");

    /// <summary>
    /// Vesyl.
    /// </summary>
    public static readonly ShipmentCarrier Vesyl = new("VESYL");

    /// <summary>
    /// Sky King.
    /// </summary>
    public static readonly ShipmentCarrier Skyking = new("SKYKING");

    /// <summary>
    /// DIR.
    /// </summary>
    public static readonly ShipmentCarrier Dirmensajeria = new("DIRMENSAJERIA");

    /// <summary>
    /// Netlogix.
    /// </summary>
    public static readonly ShipmentCarrier Netlogixgroup = new("NETLOGIXGROUP");

    /// <summary>
    /// ZYEX.
    /// </summary>
    public static readonly ShipmentCarrier Zyou = new("ZYOU");

    /// <summary>
    /// Jawar.
    /// </summary>
    public static readonly ShipmentCarrier Jawar = new("JAWAR");

    /// <summary>
    /// Associate Global Systems.
    /// </summary>
    public static readonly ShipmentCarrier Agsystems = new("AGSYSTEMS");

    /// <summary>
    /// GPS.
    /// </summary>
    public static readonly ShipmentCarrier Gps = new("GPS");

    /// <summary>
    /// PTT Kargo.
    /// </summary>
    public static readonly ShipmentCarrier PttKargo = new("PTT_KARGO");

    /// <summary>
    /// Maergo.
    /// </summary>
    public static readonly ShipmentCarrier Maergo = new("MAERGO");

    /// <summary>
    /// AICS.
    /// </summary>
    public static readonly ShipmentCarrier Arihantcourier = new("ARIHANTCOURIER");

    /// <summary>
    /// VicTas Freight Express.
    /// </summary>
    public static readonly ShipmentCarrier Vtfe = new("VTFE");

    /// <summary>
    /// Yunant.
    /// </summary>
    public static readonly ShipmentCarrier Yunant = new("YUNANT");

    /// <summary>
    /// Urbify.
    /// </summary>
    public static readonly ShipmentCarrier Urbify = new("URBIFY");

    /// <summary>
    /// pack-man.
    /// </summary>
    public static readonly ShipmentCarrier PackMan = new("PACK_MAN");

    /// <summary>
    /// LIEFERGRUN.
    /// </summary>
    public static readonly ShipmentCarrier Liefergrun = new("LIEFERGRUN");

    /// <summary>
    /// Obibox.
    /// </summary>
    public static readonly ShipmentCarrier Obibox = new("OBIBOX");

    /// <summary>
    /// Paikeda.
    /// </summary>
    public static readonly ShipmentCarrier Paikeda = new("PAIKEDA");

    /// <summary>
    /// Scotty.
    /// </summary>
    public static readonly ShipmentCarrier Scotty = new("SCOTTY");

    /// <summary>
    /// Intelcom.
    /// </summary>
    public static readonly ShipmentCarrier IntelcomCa = new("INTELCOM_CA");

    /// <summary>
    /// swe.
    /// </summary>
    public static readonly ShipmentCarrier Swe = new("SWE");

    /// <summary>
    /// Asendia Global.
    /// </summary>
    public static readonly ShipmentCarrier Asendia = new("ASENDIA");

    /// <summary>
    /// DPD Austria.
    /// </summary>
    public static readonly ShipmentCarrier DpdAt = new("DPD_AT");

    /// <summary>
    /// Relay.
    /// </summary>
    public static readonly ShipmentCarrier Relay = new("RELAY");

    /// <summary>
    /// ATA.
    /// </summary>
    public static readonly ShipmentCarrier Ata = new("ATA");

    /// <summary>
    /// SkyExpress Internationals.
    /// </summary>
    public static readonly ShipmentCarrier SkyexpressInternational = new("SKYEXPRESS_INTERNATIONAL");

    /// <summary>
    /// Surat Kargo.
    /// </summary>
    public static readonly ShipmentCarrier SuratKargo = new("SURAT_KARGO");

    /// <summary>
    /// SG LINK.
    /// </summary>
    public static readonly ShipmentCarrier Sglink = new("SGLINK");

    /// <summary>
    /// FleetOptics.
    /// </summary>
    public static readonly ShipmentCarrier Fleetopticsinc = new("FLEETOPTICSINC");

    /// <summary>
    /// shopline.
    /// </summary>
    public static readonly ShipmentCarrier Shopline = new("SHOPLINE");

    /// <summary>
    /// PIGGYSHIP.
    /// </summary>
    public static readonly ShipmentCarrier Piggyship = new("PIGGYSHIP");

    /// <summary>
    /// LogoiX.
    /// </summary>
    public static readonly ShipmentCarrier Logoix = new("LOGOIX");

    /// <summary>
    /// Kolay Gelsin.
    /// </summary>
    public static readonly ShipmentCarrier KolayGelsin = new("KOLAY_GELSIN");

    /// <summary>
    /// Associated Couriers.
    /// </summary>
    public static readonly ShipmentCarrier AssociatedCouriers = new("ASSOCIATED_COURIERS");

    /// <summary>
    /// ups-checker.
    /// </summary>
    public static readonly ShipmentCarrier UpsChecker = new("UPS_CHECKER");

    /// <summary>
    /// Wineshipping.
    /// </summary>
    public static readonly ShipmentCarrier Wineshipping = new("WINESHIPPING");

    /// <summary>
    /// Spedisci online.
    /// </summary>
    public static readonly ShipmentCarrier Spedisci = new("SPEDISCI");

    /// <summary>
    /// Fourkites.
    /// </summary>
    public static readonly ShipmentCarrier Fourkites = new("FOURKITES");

    /// <summary>
    /// Etonas.
    /// </summary>
    public static readonly ShipmentCarrier Etonas = new("ETONAS");

    /// <summary>
    /// Fin Mile.
    /// </summary>
    public static readonly ShipmentCarrier Finmile = new("FINMILE");

    /// <summary>
    /// Uniuni.
    /// </summary>
    public static readonly ShipmentCarrier Uniuni = new("UNIUNI");

    /// <summary>
    /// Rodonaves.
    /// </summary>
    public static readonly ShipmentCarrier Rodonaves = new("RODONAVES");

    /// <summary>
    /// Inpost Italy.
    /// </summary>
    public static readonly ShipmentCarrier InpostIt = new("INPOST_IT");

    /// <summary>
    /// Tforce Freight.
    /// </summary>
    public static readonly ShipmentCarrier TforceFreight = new("TFORCE_FREIGHT");

    /// <summary>
    /// Rich Mom.
    /// </summary>
    public static readonly ShipmentCarrier Richmom = new("RICHMOM");

    /// <summary>
    /// Corriere Franco.
    /// </summary>
    public static readonly ShipmentCarrier Franco = new("FRANCO");

    /// <summary>
    /// Ecparcel.
    /// </summary>
    public static readonly ShipmentCarrier Ecparcel = new("ECPARCEL");

    /// <summary>
    /// Fedex China.
    /// </summary>
    public static readonly ShipmentCarrier FedexChina = new("FEDEX_CHINA");

    /// <summary>
    /// Gofo Express.
    /// </summary>
    public static readonly ShipmentCarrier GofoExpress = new("GOFO_EXPRESS");

    /// <summary>
    /// Shipbob.
    /// </summary>
    public static readonly ShipmentCarrier Shipbob = new("SHIPBOB");

    /// <summary>
    /// Jersey Post Group.
    /// </summary>
    public static readonly ShipmentCarrier JerseypostAtlas = new("JERSEYPOST_ATLAS");

    /// <summary>
    /// Coretrails.
    /// </summary>
    public static readonly ShipmentCarrier Coretrails = new("CORETRAILS");

    /// <summary>
    /// Rhenus Logistics Italy.
    /// </summary>
    public static readonly ShipmentCarrier RhenusItaly = new("RHENUS_ITALY");

    /// <summary>
    /// Jadlog.
    /// </summary>
    public static readonly ShipmentCarrier Jadlog = new("JADLOG");

    /// <summary>
    /// Jitsu.
    /// </summary>
    public static readonly ShipmentCarrier Jitsu = new("JITSU");

    /// <summary>
    /// Yanwen Express.
    /// </summary>
    public static readonly ShipmentCarrier YanwenExpress = new("YANWEN_EXPRESS");

    /// <summary>
    /// Dashlink.
    /// </summary>
    public static readonly ShipmentCarrier Dashlink = new("DASHLINK");

    /// <summary>
    /// Seino Super Express.
    /// </summary>
    public static readonly ShipmentCarrier SeinoSuperExpress = new("SEINO_SUPER_EXPRESS");

    /// <summary>
    /// Floship.
    /// </summary>
    public static readonly ShipmentCarrier Floship = new("FLOSHIP");

    /// <summary>
    /// Metro Supply Chain.
    /// </summary>
    public static readonly ShipmentCarrier Metroscg = new("METROSCG");

    /// <summary>
    /// Sendparcel.
    /// </summary>
    public static readonly ShipmentCarrier Sendparcel = new("SENDPARCEL");

    /// <summary>
    /// P2p.
    /// </summary>
    public static readonly ShipmentCarrier P2P = new("P2P");

    /// <summary>
    /// Cn Express.
    /// </summary>
    public static readonly ShipmentCarrier CnExpress = new("CN_EXPRESS");

    /// <summary>
    /// Cirro Track.
    /// </summary>
    public static readonly ShipmentCarrier Cirrotrack = new("CIRROTRACK");

    /// <summary>
    /// Land Logistics.
    /// </summary>
    public static readonly ShipmentCarrier LandLogistics = new("LAND_LOGISTICS");

    /// <summary>
    /// Veho.
    /// </summary>
    public static readonly ShipmentCarrier Veho = new("VEHO");

    /// <summary>
    /// Medline.
    /// </summary>
    public static readonly ShipmentCarrier Medline = new("MEDLINE");

    /// <summary>
    /// Vdtrack.
    /// </summary>
    public static readonly ShipmentCarrier Vdtrack = new("VDTRACK");

    /// <summary>
    /// Sino Scm.
    /// </summary>
    public static readonly ShipmentCarrier SinoScm = new("SINO_SCM");

    /// <summary>
    /// 3pe Express.
    /// </summary>
    public static readonly ShipmentCarrier _3PeExpress = new("3PE_EXPRESS");

    /// <summary>
    /// Swiftx.
    /// </summary>
    public static readonly ShipmentCarrier Swiftx = new("SWIFTX");

    /// <summary>
    /// Sfyd Express.
    /// </summary>
    public static readonly ShipmentCarrier Sfydexpress = new("SFYDEXPRESS");

    /// <summary>
    /// Toptrans.
    /// </summary>
    public static readonly ShipmentCarrier Toptrans = new("TOPTRANS");

    public static ShipmentCarrier FromValue(string value) => FromValueCore(value);
}
