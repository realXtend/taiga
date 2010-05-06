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

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    public class LoginSwitch : RexLoginModule
    {
        protected static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // RexLogin module declares m_log private so it cant be used here
        // protected static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        // private RexLoginModule m_RexLoginModule = new RexLoginModule();

        private UserLoginService m_UserLoginService;

        protected RealXtendLogin m_RealXtendLogin;

        // storing avatar uuid corresponding session_id
        protected static Dictionary<UUID,UUID> m_LoggingInClients = new Dictionary<UUID,UUID>();


        public LoginSwitch( UserLoginService service,
                            IInterServiceInventoryServices interInventoryService,
                            IInventoryService inventoryService,
                            IGridService gridService,
                            UserConfig config)
        {
            m_UserLoginService = service;
            m_RealXtendLogin = new RealXtendLogin(service, interInventoryService, inventoryService, this, gridService, config);
        }

        public XmlRpcResponse XmlRpcLoginMethodSwitch(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];

            bool RexXML = (requestData.Contains("account") && requestData.Contains("sessionhash"));
            if (RexXML) { return this.m_RealXtendLogin.XmlRpcLoginMethod(request, remoteClient); }

            XmlRpcResponse response = m_UserLoginService.XmlRpcLoginMethod(request, remoteClient);

            if (requestData.Contains("version"))
            {
                if (((string)requestData["version"]).StartsWith("realXtend"))
                {
                    ((Hashtable)response.Value)["rex"] = "running rex mode";
                }
            }

            // Ask inv service the endpoint url for webdav inventory, return in login response
            string inventoryWebdavUrl = GetUserWebdavBaseUrl(((Hashtable)response.Value));
            if (inventoryWebdavUrl != string.Empty)
                ((Hashtable)response.Value)["webdav_inventory"] = inventoryWebdavUrl;

            // Send the webdav avatar appearance url to sim to broadcast to all viewers
            BroadCastWebDavAvatarAppearanceUrl(((Hashtable)response.Value));
            
            return response;
        }

        public XmlRpcResponse XmlRPCCheckAuthSessionSwitch(XmlRpcRequest request, IPEndPoint remoteClient)
        {
            Hashtable requestData = (Hashtable)request.Params[0];
            XmlRpcResponse response = new XmlRpcResponse();
            Hashtable responseData = new Hashtable();
            if (requestData.Contains("avatar_uuid") && requestData.Contains("session_id"))
            {
                UUID avatar;
                UUID sessionID;
                if (UUID.TryParse((string)requestData["avatar_uuid"], out avatar) &&
                    UUID.TryParse((string)requestData["session_id"], out sessionID)) 
                {
                    if (m_LoggingInClients.ContainsKey(avatar))
                    {
                        if (m_LoggingInClients[avatar] == sessionID)
                        {
                            responseData["auth_session"] = "TRUE";
                        }
                        else
                        {
                            responseData["auth_session"] = "FALSE";
                        }
                        m_LoggingInClients.Remove(avatar);
                        response.Value = responseData;
                        return response;
                    }
                    else 
                    {
                        // try normal OpenSim authentication 
                        return m_UserLoginService.XmlRPCCheckAuthSession(request, remoteClient);
                    }
                }
            }
            responseData["auth_session"] = "FALSE";
            response.Value = responseData;
            return response;
        }

        public bool AddLoggingInClient(UUID uuid, UUID sessionId)
        {
            if (m_LoggingInClients.ContainsKey(uuid))
            {
                return false;
            }
            else
            {
                m_LoggingInClients.Add(uuid, sessionId);
                return true;
            }
        }

        public string GetUserWebdavBaseUrl(Hashtable responseDataTable)
        {
            OpenMetaverse.UUID agentUuid;
            if (OpenMetaverse.UUID.TryParse(responseDataTable["agent_id"].ToString(), out agentUuid))
            {
                WebRequest request;
                WebResponse response;
                string url = m_RealXtendLogin.m_UserConfig.GridServerURL.ToString() + "get_inventory_webdav_url";

                request = WebRequest.Create(url);
                request.Headers.Add("Avatar-UUID", agentUuid.ToString());

                response = request.GetResponse();
                string inventoryWebdavUrl = response.Headers.Get("Inventory-Webdav-Url");
                if (inventoryWebdavUrl != string.Empty)
                {
                    m_log.Info("[LOGIN]: Agent inventory webdav url retrieved");
                    return inventoryWebdavUrl;
                }
                else
                {
                    m_log.Info("[LOGIN]: Failed to retrieve agent inventory webdav url for " + agentUuid.ToString());
                }
            }
            return string.Empty;
        }

        public void BroadCastWebDavAvatarAppearanceUrl(Hashtable responseDataTable)
        {
            OpenMetaverse.UUID agentUuid;
            if (OpenMetaverse.UUID.TryParse(responseDataTable["agent_id"].ToString(), out agentUuid))
            {
                WebRequest request;
                WebResponse response;
                string url = m_RealXtendLogin.m_UserConfig.GridServerURL.ToString() + "get_avatar_webdav_url";

                request = WebRequest.Create(url);
                request.Headers.Add("Avatar-UUID", agentUuid.ToString());

                response = request.GetResponse();
                string appearanceWebdavUrl = response.Headers.Get("Avatar-Webdav-Url");
                if (appearanceWebdavUrl != string.Empty)
                {
                    m_log.Info("[LOGIN]: Agent appearance webdav url retrieved");

                    // If the responses http port is 0 we have to hack it from seed cap
                    int sim_http_port = (int)responseDataTable["http_port"];
                    if (sim_http_port == 0 && responseDataTable.Contains("seed_capability"))
                    {
                        Uri uri = new Uri(responseDataTable["seed_capability"].ToString());
                        sim_http_port = uri.Port;
                    }
                    RexLogin.RealXtendLogin.SendAvatarUrlXmlRpc(responseDataTable["sim_ip"].ToString(), sim_http_port, agentUuid, appearanceWebdavUrl);
                }
                else
                {
                    m_log.Info("[LOGIN]: Failed to retrieve agent appearance webdav url for " + agentUuid.ToString());
                }
            }
        }
    }

}
