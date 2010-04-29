using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Services;
//using OpenSim.Region.Framework.Interfaces;
//using OpenSim.Region.Framework.Scenes;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Framework.Servers;
using ModularRex.RexDBObjects;
using OpenSim.Framework.Servers.HttpServer;
using System.Net;
using ModularRex.RexNetwork.RexLogin;
using ModularRex.RexNetwork;
using OpenSim.Services.Interfaces;
using CableBeachMessages;
using OpenMetaverse.Http;
//using Mono.Security.X509;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using OpenSim.Framework.Statistics;


namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    public partial class RealXtendLogin : RexLoginModule
    {
        protected static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public const int REQUEST_TIMEOUT = 1000 * 100;

        protected uint m_defaultHomeX;
        protected uint m_defaultHomeY;

        public X509Certificate2 HttpCertificate;
        private UserLoginService m_UserLoginService;
        // TODO: get from LoginService
        protected int m_minLoginLevel = 0;
        protected bool m_warn_already_logged = true;
        protected Mutex m_loginMutex = new Mutex(false);
        protected IGridService m_GridService;
        public UserConfig m_UserConfig;


        protected static IInterServiceInventoryServices m_interInventoryService;
        protected static IInventoryService m_InventoryService;
        protected LoginSwitch m_LoginSwitch;
        protected static OpenSimMap m_OpenSimMap;

        public RealXtendLogin(UserLoginService service,
                            IInterServiceInventoryServices interInventoryService,
                            IInventoryService inventoryService,
                            LoginSwitch loginSwitch,
                            IGridService gridService,
                            UserConfig config)
        {
            m_UserLoginService = service;
            m_interInventoryService = interInventoryService;
            m_InventoryService = inventoryService;
            m_LoginSwitch = loginSwitch;
            m_GridService = gridService;
            m_UserConfig = config;
            m_defaultHomeX = m_UserConfig.DefaultX;
            m_defaultHomeY = m_UserConfig.DefaultY;
            m_OpenSimMap = new OpenSimMap(config.GridServerURL, m_GridService);
        }

        public override XmlRpcResponse XmlRpcLoginMethod(XmlRpcRequest request, IPEndPoint client)
        {
            m_loginMutex.WaitOne();
            try
            {
                #region Authenticate & check if login is wellformed
                m_log.Info("[LOGIN]: Attempting login in realXtend mode...");
                XmlRpcResponse response = new XmlRpcResponse();
                Hashtable requestData = (Hashtable)request.Params[0];

                bool GoodLogin;

                string startLocationRequest = "last";

                LoginResponse logResponse = new LoginResponse();

                string account;
                string sessionHash;
                string clientVersion = "Unknown";

                if (requestData.Contains("version"))
                {
                    clientVersion = (string)requestData["version"];
                }

                account = (string)requestData["account"];
                sessionHash = (string)requestData["sessionhash"];

                m_log.InfoFormat(
                    "[REX LOGIN BEGIN]: XMLRPC Received login request message from user '{0}' '{1}'",
                    account, sessionHash);

                if (requestData.Contains("start"))
                {
                    startLocationRequest = (string)requestData["start"];
                }

                m_log.DebugFormat(
                    "[REXLOGIN]: XMLRPC Client is {0}, start location is {1}", clientVersion, startLocationRequest);

                GoodLogin = AuthenticateUser(account, sessionHash);

                if (!GoodLogin)
                {
                    m_log.InfoFormat("[LOGIN END]: XMLRPC  User {0} ({1}) failed authentication", account, sessionHash);
                    return logResponse.CreateLoginFailedResponse();
                }
                try
                {
                    string actName = account.Split('@')[0];
                    string actSrv = account.Split('@')[1];

                    RexUserProfileData userProfile = AuthenticationService.GetUserByAccount(actName, actSrv);

                    userProfile.PasswordHash = "$1$";
                    userProfile.PasswordSalt = "";


                    UUID agentID = userProfile.ID;

                    // Used to transmit the login URL to the 
                    // RexAvatar class when it connects.
                    m_userData[agentID] = userProfile;

                    logResponse.CircuitCode = Util.RandomClass.Next();

                    logResponse.Lastname = "<" + account + ">";
                    logResponse.Firstname = userProfile.FirstName + " " + userProfile.SurName;
                    logResponse.AgentID = agentID;

                    logResponse.Message = m_UserConfig.DefaultStartupMsg;

                #endregion Authenticate & check if login is wellformed

                    if (userProfile.GodLevel < m_minLoginLevel)
                    {
                        return logResponse.CreateLoginBlockedResponse();
                    }
                    else
                    {
                        #region If we already have a session...
                        
                        // agent is probably newer null because it's realXtend login, so there's probably no need for checking this,
                        // but online check is still valid
                        if (userProfile.CurrentAgent != null && userProfile.CurrentAgent.AgentOnline)
                        {
                            userProfile.CurrentAgent.AgentOnline = false;

                            // Commiting online status to userserver:
                            AuthenticationService.UpdateUserAgent(agentID.ToString(),
                                userProfile.CurrentAgent.Handle.ToString(), userProfile.CurrentAgent.Position.ToString(),
                                userProfile.GridUrl.ToString(), userProfile.AuthUrl);

                            //try to tell the region that their user is dead.
                            m_UserLoginService.LogOffUser(userProfile, " XMLRPC You were logged off because you logged in from another location");

                            if (m_warn_already_logged)
                            {
                                m_log.InfoFormat(
                                    "[LOGIN END]: XMLRPC Notifying user {0} that they are already logged in",
                                    userProfile.Name);
                                return logResponse.CreateAlreadyLoggedInResponse();
                            }

                            // This is behavior for standalone (silent logout of last hung session)
                            m_log.InfoFormat(
                                "[LOGIN]: XMLRPC User {0} is already logged in, not notifying user, kicking old presence and starting new login.",
                                userProfile.Name);
                        }

                        #endregion //If we already have a session...
                    }


                    try
                    {
                        LoginService.InventoryData inventData = null;

                        try
                        {
                            inventData = CableBeachState.LoginService.GetInventorySkeleton(agentID);
                            //inventData = GetInventorySkeleton(agentID);
                        }
                        catch (Exception e)
                        {
                            m_log.ErrorFormat(
                                "[LOGIN END]: Error retrieving inventory skeleton of agent {0} - {1}",
                                agentID, e);

                            // Let's not panic
                            if (!AllowLoginWithoutInventory())
                                return logResponse.CreateLoginInventoryFailedResponse();
                        }

                        if (inventData != null)
                        {
                            ArrayList AgentInventoryArray = inventData.InventoryArray;

                            Hashtable InventoryRootHash = new Hashtable();
                            InventoryRootHash["folder_id"] = inventData.RootFolderID.ToString();
                            ArrayList InventoryRoot = new ArrayList();
                            InventoryRoot.Add(InventoryRootHash);

                            logResponse.InventoryRoot = InventoryRoot;
                            logResponse.InventorySkeleton = AgentInventoryArray;
                        }

                        // Inventory Library Section
                        Hashtable InventoryLibRootHash = new Hashtable();
                        InventoryLibRootHash["folder_id"] = "00000112-000f-0000-0000-000100bba000"; //UUID.Zero.ToString(); 
                        ArrayList InventoryLibRoot = new ArrayList();
                        InventoryLibRoot.Add(InventoryLibRootHash);

                        logResponse.InventoryLibRoot = InventoryLibRoot;
                        
                        //logResponse.InventoryLibraryOwner = GetLibraryOwner();
                        logResponse.InventoryLibraryOwner = CableBeachState.LoginService.GetLibraryOwner();
                        //logResponse.InventoryLibrary = GetInventoryLibrary();
                        logResponse.InventoryLibrary = CableBeachState.LoginService.GetInventoryLibrary();
            
                        logResponse.CircuitCode = Util.RandomClass.Next();
                        logResponse.Lastname = userProfile.SurName;
                        logResponse.Firstname = userProfile.FirstName;
                        logResponse.AgentID = agentID;
                        logResponse.SessionID = userProfile.CurrentAgent.SessionID = UUID.Random();
                        logResponse.SecureSessionID = userProfile.CurrentAgent.SecureSessionID = UUID.Random();

                        // get inventory
                        // TODO: Fetch avatar storage global inventory
                        // possibly debricated:
                        //logResponse.BuddList = ConvertFriendListItem(m_userManager.GetUserFriendList(agentID));
                        //List<RexFriendListItem> friendList = AuthenticationService.GetUserFriendList(agentID.ToString(), userProfile.AuthUrl);
                        //TODO: convert List<RexFriendListItem> to Buddlist

                        logResponse.StartLocation = startLocationRequest;

                        //TODO: if already trying to log in..
                        bool added = m_LoginSwitch.AddLoggingInClient(userProfile.ID, userProfile.CurrentAgent.SessionID);

                        // setup avatar
                        // then get region

                        OpenSim.Grid.UserServer.Modules.RexLogin.Avatar avatar
                            = SetUpAvatar(account, userProfile.AuthUrl, userProfile.CurrentAgent.SessionID, userProfile);

                        userProfile.Account = account;


                        // Identity: <authentication server uri>/users/<account>
                        if (!Uri.TryCreate("http://" + userProfile.AuthUrl + "/users/" + account.Split('@')[0] //+ "." + userProfile.AuthUrl
                            , UriKind.Absolute, out avatar.Identity))
                        {
                            m_log.Error("[RealXtendLogin]: Failed to parse avatar identity ");
                            OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginHelper.CreateFailureResponse(
                                "Failed to parse avatar identity", "Failed to parse avatar identity for " + account, false);
                        }

                        OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginData loginData = SetUpLindenLoginData(avatar, account,
                            logResponse.Message, userProfile.CurrentAgent.SessionID);

                        CableBeachMessages.RegionInfo startReg;
                        Vector3 startPosition;

                        if (m_UserLoginService.CustomiseResponse(logResponse, (UserProfileData)userProfile, startLocationRequest, client))
                        {
                            userProfile.LastLogin = userProfile.CurrentAgent.LoginTime;
                            UserProfileData t_userData = (UserProfileData)userProfile;
                            m_UserLoginService.CommitAgent(ref t_userData);
                            userProfile = (RexUserProfileData)t_userData;

                            if (StatsManager.UserStats != null)
                                StatsManager.UserStats.AddSuccessfulLogin();

                            try
                            {

                                if (!String.IsNullOrEmpty(logResponse.SeedCapability))
                                {
                                    Uri uri = new Uri(logResponse.SeedCapability);
                                    logResponse.SimHttpPort = (uint)uri.Port;
                                }

                                SendAvatarUrlXmlRpc(logResponse.SimAddress, (int)logResponse.SimHttpPort, logResponse.AgentID, avatar.Attributes[RexAvatarAttributes.AVATAR_STORAGE_URL].AsString());
                            }
                            catch (Exception e)
                            {
                                m_log.ErrorFormat("[REXLOGIN]: Failed to send avatar url to simulator, because of exception: {0}", e.Message);
                            }

                            return logResponse.ToXmlRpcResponse();
                        }
                        else
                        {
                            return OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginHelper.CreateLoginNoRegionResponse();
                        }

                        //if (TryGetStartingRegion(avatar, startLocationRequest, ref loginData, out startReg, out startPosition))
                        //{
                        //    string simIp = startReg.IP.ToString();
                        //    logResponse.SimPort = (uint)startReg.Port;
                        //    logResponse.SimAddress = simIp;
                        //    string error;

                        //    if (TryPrepareLogin(avatar,
                        //        startReg, startPosition, clientVersion, client.Address, ref loginData, HttpCertificate, logResponse.CircuitCode, out error))
                        //    {
                        //        m_log.Info("[RealXtendLogin] Login to " + startReg.Name + " prepared for " + avatar.Identity + ", returning response");
                                
                        //        userProfile.CurrentAgent.AgentOnline = true;
                        //        AuthenticationService.UpdateUserAgent(agentID.ToString(),
                        //            startReg.Handle.ToString(), userProfile.CurrentAgent.Position.ToString(),
                        //            startReg.ToString(), userProfile.AuthUrl);
                                
                        //        return logResponse.ToXmlRpcResponse();
                        //    }
                        //    else
                        //    {
                        //        m_log.Info("[RealXtendLogin] Preparing Login to " + startReg.Name + " failed " + avatar.Identity
                        //            + ", returning failure response, " + error);
                        //        XmlRpcResponse rep = OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginHelper.CreateFailureResponse(
                        //            "Preparing login fail", "Preparing Login to " + startReg.Name + " failed: " + error, false);
                        //        return rep;
                        //    }
                        //}
                        //else
                        //{
                        //    m_log.ErrorFormat("[LOGIN END]: XMLRPC informing user {0} that login failed due to an unavailable region", userProfile.Name);
                        //    return OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginHelper.CreateLoginNoRegionResponse();
                        //}
                        return OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginHelper.CreateLoginInternalErrorResponse();
                    }
                    catch (Exception e)
                    {
                        m_log.Error("[LOGIN END]: XMLRPC Login failed: " + e.StackTrace + "\nSending back blank XMLRPC response.");
                        m_log.Error(e.StackTrace);
                        return response;
                    }
                }
                catch (Exception e) {
                    m_log.Error("[LOGIN END]: XMLRPC Login failed: " + e.StackTrace + "\nSending back blank XMLRPC response.");
                    return response;                
                }
            }
            finally
            {
                m_loginMutex.ReleaseMutex();
            }

        }

        public void SendAvatarUrlXmlRpc(string ip, int port, UUID id, string url)
        {
            Hashtable parms = new Hashtable();
            parms.Add("AgentID", id.ToString());
            parms.Add("AvatarURL", url);
            XmlRpcResponse resp = Util.XmlRpcCommand("http://"+ip + ":" + port.ToString(), "realXtend_avatar_url", parms);
            if (resp.Value is Hashtable)
            {
                Hashtable respData = (Hashtable)resp.Value;
                if (respData.ContainsKey("SUCCESS"))
                {
                    bool success;
                    if (bool.TryParse(respData["SUCCESS"].ToString(), out success))
                    {
                        if (success == true)
                        {
                            m_log.Info("[REXLOGIN]: Avatar url sent to simulator succesfully");
                        }
                    }
                    else
                    {
                        string error = "Unknown";
                        if (respData.ContainsKey("ERROR"))
                        {
                            error = respData["ERROR"].ToString();
                        }
                        m_log.ErrorFormat("[REXLOGIN]: Could not deliver avatar url to simulator because of error: {0}", error);
                    }
                }
            }
        }

        protected virtual bool AllowLoginWithoutInventory()
        {
            return false;
        }

        private bool TryPrepareLogin(Avatar avatar, CableBeachMessages.RegionInfo startRegion, Vector3 startPosition,
            string clientVersion, System.Net.IPAddress clientIP, ref LindenLoginData response, X509Certificate2 httpCertificate,
            int circuitCode, out string error)
        {
            error = string.Empty;
            EnableClientMessage message = new EnableClientMessage();
            message.Identity = avatar.Identity;
            message.AgentID = avatar.ID;
            message.Attributes = avatar.Attributes;
            message.CallbackUri = null;
            message.ChildAgent = false;
            //message.CircuitCode = LindenLoginHelper.CreateCircuitCode();
            message.CircuitCode = circuitCode;
            message.ClientVersion = clientVersion;
            message.IP = clientIP;
            message.RegionHandle = startRegion.Handle;
            message.SecureSessionID = response.SecureSessionID;
            message.Services = avatar.Services.ToMessageDictionary();
            Dictionary<Uri, Uri> avStrgDict = new Dictionary<Uri, Uri>();
            avStrgDict.Add(RexAvatarAttributes.AVATAR_STORAGE_URL, avatar.Attributes[RexAvatarAttributes.AVATAR_STORAGE_URL].AsUri());
            message.Services.Add(RexAvatarAttributes.AVATAR_STORAGE_URL, avStrgDict);
            message.SessionID = response.SessionID;

            Uri enableClientCap;
            if (startRegion.Capabilities.TryGetValue(new Uri(CableBeachServices.SIMULATOR_ENABLE_CLIENT), out enableClientCap))
            {
                CapsClient request = (httpCertificate != null) ?
                new CapsClient(enableClientCap, httpCertificate) :
                new CapsClient(enableClientCap);

                OSDMap responseMap = request.GetResponse(message.Serialize(), OSDFormat.Json, LindenLoginHelper.REQUEST_TIMEOUT) as OSDMap;

                if (responseMap != null)
                {
                    EnableClientReplyMessage reply = new EnableClientReplyMessage();
                    reply.Deserialize(responseMap);

                    if (reply.SeedCapability != null)
                    {
                        m_log.Debug("enable_client succeeded, sent circuit code " + message.CircuitCode + " and received seed capability " +
                            reply.SeedCapability + " from " + enableClientCap);

                        response.CircuitCode = message.CircuitCode;
                        response.SeedCapability = reply.SeedCapability.ToString();
                        return true;
                    }
                    else
                    {
                        error = reply.Message;
                        m_log.Error("[LindenLoginHelper] enable_client call to region " + startRegion.Name + " for login from " + avatar.Identity +
                            " failed, did not return a seed capability");
                    }
                }
                else
                {
                    error = "could not contact or invalid response";
                    m_log.Error("[LindenLoginHelper] enable_client call to region " + startRegion.Name + " for login from " + avatar.Identity +
                        " failed, could not contact or invalid response");
                }
            }
            else
            {
                error = "region does not have an enable_client capability";
                m_log.Error("[LindenLoginHelper] enable_client call failed, region " + startRegion.Name +
                    " does not have an enable_client capability");
            }

            return false;
        }


    }
}
