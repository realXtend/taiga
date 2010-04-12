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

    }


}
