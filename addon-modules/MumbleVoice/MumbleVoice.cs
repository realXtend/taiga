using System;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;
using System.Collections.Generic;

namespace MumbleVoice
{
    /// Offer Mumble server information via REST method.
    /// Request headers:
    ///   - avatar_uuid ... The uuid of the avatar requesting voice
    /// 
    /// @todo Return xml insted of reponse headers
    /// @todo Access control using eq. user specifig passwords 
    public class MumbleVoiceModule : IRegionModule
    {
        private static readonly ILog m_Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public Type ReplaceableInterface { get { return null; } }

        private string m_server_address = null;
        private string m_server_password = "";
        private string m_server_version = "";
        private string m_default_channel = "Root";
        private string m_context = "Mumbe voice system";
        private static string SERVICE_REST_URL = "/mumble_server_info";
        private bool m_enabled = false;

        private List<Scene> m_scenes = new List<Scene>();

        public MumbleVoiceModule()
        {
        }

        public void Initialise(Scene scene, IConfigSource source)
        {
            ReadConfig(source);

            m_scenes.Add(scene);

            //scene.EventManager.OnMakeChildAgent += OnMakeChildAgent;
            //scene.EventManager.OnMakeRootAgent += OnMakeRootAgent;

            m_Log.Info("[MUMBLE VOIP]: MumbleVoiceModule initialized");
	    }

        private void ReadConfig(IConfigSource source)
        {
            try
            {
                if (source.Configs["mumblevoice"] != null)
                {
                    m_enabled = source.Configs["mumblevoice"].GetBoolean("enabled", false);
                    m_server_address = source.Configs["mumblevoice"].GetString("server_address", "");
                    m_server_password = source.Configs["mumblevoice"].GetString("server_password", "");
                    m_server_version = source.Configs["mumblevoice"].GetString("server_version", "");
                    if (source.Configs["mumblevoice"].Contains("channel"))
                        m_default_channel = source.Configs["mumblevoice"].GetString("channel", "Root");
                    else
                        m_default_channel = null;
                }
            }
            catch (Exception)
            {
                m_Log.Error("[MUMBLE VOIP]: Cannot find server configuration");
            }
        }

        public void PostInitialise()
        {
            if (m_enabled)
            {
                foreach (Scene s in m_scenes)
                {
                    s.EventManager.OnRegisterCaps += new EventManager.RegisterCapsEvent(EventManager_OnRegisterCaps);
                }
            }
        }

        void EventManager_OnRegisterCaps(OpenMetaverse.UUID agentID, OpenSim.Framework.Capabilities.Caps caps)
        {
            UUID capID = UUID.Random();
            caps.RegisterHandler("mumble_server_info", new RestStreamHandler("GET", "/CAPS/" + capID, RestGetMumbleServerInfo));
        }

        /// <summary>
        /// Returns information about a mumble server via a REST Request
        /// </summary>
        /// <param name="request"></param>
        /// <param name="path"></param>
        /// <param name="param">A string representing the sim's UUID</param>
        /// <param name="httpRequest">HTTP request header object</param>
        /// <param name="httpResponse">HTTP response header object</param>
        /// <returns>Information about the mumble server in http response headers</returns>
        public string RestGetMumbleServerInfo(string request, string path, string param,
                                       OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            if (m_server_address == null)
            {
                httpResponse.StatusCode = 404;
                httpResponse.StatusDescription = "Not Found";

                string message = "[MUMBLE VOIP]: Server info request from " + httpRequest.RemoteIPEndPoint.Address + ". Cannot send response, module is not configured properly.";
                m_Log.Warn(message);
                return "Mumble server info is not available.";
            }
            if (httpRequest.Headers.GetValues("avatar_uuid") == null)
            {
                httpResponse.StatusCode = 400;
                httpResponse.StatusDescription = "Bad Request";

                string message = "[MUMBLE VOIP]: Invalid server info request from " + httpRequest.RemoteIPEndPoint.Address +"";
                m_Log.Warn(message);
                return "avatar_uuid header is missing";
            }

            string avatar_uuid = httpRequest.Headers.GetValues("avatar_uuid")[0];
            string responseBody = String.Empty;
            UUID avatarId;
            if (UUID.TryParse(avatar_uuid, out avatarId))
            {
                string channel;
                string regionId = GetRegionUuid(avatarId);
                if (regionId == null)
                {
                    //user has not logged in?
                    regionId = "Root";
                }

                if (m_default_channel == null)
                    channel = regionId;
                else
                    channel = m_default_channel;

                httpResponse.AddHeader("Mumble-Server", m_server_address);
                httpResponse.AddHeader("Mumble-Version", m_server_version);
                httpResponse.AddHeader("Mumble-Channel", channel);
                httpResponse.AddHeader("Mumble-User", avatar_uuid);
                httpResponse.AddHeader("Mumble-Password", m_server_password);
                httpResponse.AddHeader("Mumble-Avatar-Id", avatar_uuid);
                httpResponse.AddHeader("Mumble-Context-Id", m_context);

                responseBody += "Mumble-Server: " + m_server_address + "\n";
                responseBody += "Mumble-Version: " + m_server_version + "\n";
                responseBody += "Mumble-Channel: " + channel + "\n";
                responseBody += "Mumble-User: " + avatar_uuid + "\n";
                responseBody += "Mumble-Password: " + m_server_password + "\n";
                responseBody += "Mumble-Avatar-Id: " + avatar_uuid + "\n";
                responseBody += "Mumble-Context-Id: " + m_context + "\n";

                string log_message = "[MUMBLE VOIP]: Server info request handled for " + httpRequest.RemoteIPEndPoint.Address + "";
                m_Log.Info(log_message);
            }
            else
            {
                httpResponse.StatusCode = 400;
                httpResponse.StatusDescription = "Bad Request";

                m_Log.Warn("[MUMBLE VOIP]: Could not parse avatar uuid from request");
                return "could not parse avatar_uuid header";
            }

            return responseBody;
        }

        string GetRegionUuid(UUID userId)
        {
            foreach (Scene s in m_scenes)
            {
                ScenePresence avatar = s.GetScenePresence(userId);
                if (avatar != null && !(avatar.IsChildAgent))
                {
                    return s.RegionInfo.RegionID.ToString();
                }
            }

            return null;
        }

        public void Close()
	    {
            if (m_enabled)
            {
                foreach (Scene s in m_scenes)
                {
                    s.EventManager.OnRegisterCaps -= new EventManager.RegisterCapsEvent(EventManager_OnRegisterCaps);
                }
            }
	    }
    	
        public string Name
        {
            get { return "Mumble Voice Module"; }
        }
        
        public bool IsSharedModule
        {
            get { return true; }
        }

        private void OnMakeRootAgent(ScenePresence presence)
        {
            m_Log.Info("[MUMBLE VOIP]: new root agent.");
        }

        private void OnMakeChildAgent(ScenePresence presence)
        {
            m_Log.Info("[MUMBLE VOIP]: new child agent.");
        }
    }
}