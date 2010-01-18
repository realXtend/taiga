/*
 * Copyright (c) Intel Corporation
 * All rights reserved.
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 *
 * -- Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * -- Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 * -- Neither the name of the Intel Corporation nor the names of its
 *    contributors may be used to endorse or promote products derived from
 *    this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 * ``AS IS'' AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 * LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A
 * PARTICULAR PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE INTEL OR ITS
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
 * EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
 * PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
 * PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF
 * LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Capabilities;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Http;
using CableBeachMessages;

using OSDMap = OpenMetaverse.StructuredData.OSDMap;
using Caps = OpenSim.Framework.Capabilities.Caps;
using RegionInfo = CableBeachMessages.RegionInfo;

[assembly: Addin("ModCableBeach", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace ModCableBeach
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class ModCableBeach : ISharedRegionModule
    {
        private static readonly ILog m_Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Dictionary<string, string> DEFAULT_CAPS_PATHS;

        private bool m_Enabled = false;
        private Dictionary<ulong, WorldServiceConnector> m_regionWorldServiceConnectors = new Dictionary<ulong, WorldServiceConnector>();

        public Type ReplaceableInterface { get { return null; } }
        public bool IsSharedModule { get { return true; } }

        /// <summary>Name of this region module</summary>
        public string Name
        {
            get { return "ModCableBeach"; }
        }

        static ModCableBeach()
        {
            DEFAULT_CAPS_PATHS = new Dictionary<string, string>();
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.ASSET_CREATE_ASSET, "/assets/create_asset");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_CREATE_FILESYSTEM, "/filesystem/create_filesystem");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_CREATE_OBJECT, "/filesystem/create_object");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_ACTIVE_GESTURES, "/filesystem/get_active_gestures");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_FILESYSTEM, "/filesystem/get_filesystem");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_FILESYSTEM_SKELETON, "/filesystem/get_filesystem_skeleton");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_FOLDER_CONTENTS, "/filesystem/get_folder_contents");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_FOLDER_FOR_TYPE, "/filesystem/get_folder_for_type");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_OBJECT, "/filesystem/get_object");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_GET_ROOT_FOLDER, "/filesystem/get_root_folder");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_PURGE_FOLDER, "/filesystem/purge_folder");
            DEFAULT_CAPS_PATHS.Add(CableBeachServices.FILESYSTEM_DELETE_OBJECT, "/filesystem/delete_object");
        }

        public void Initialise(IConfigSource source)
        {
            WorldServiceConnector.OnWorldServiceConnectorLoaded += WorldServiceConnectorLoadedHandler;

            m_Log.Info("[CABLE BEACH MOD]: ModCableBeach initialized");
        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
            if (m_Enabled)
            {
                m_Enabled = false;

                MainServer.Instance.RemoveLLSDHandler("/enable_client", EnableClientMessageHandler);
                MainServer.Instance.RemoveLLSDHandler("/close_agent_connection", CloseAgentConnectionHandler);
            }
        }

        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
            {
                if (MainServer.Instance != null)
                {
                    m_Enabled = true;

                    MainServer.Instance.AddLLSDHandler("/enable_client", EnableClientMessageHandler);
                    MainServer.Instance.AddLLSDHandler("/close_agent_connection", CloseAgentConnectionHandler);

                    m_Log.Info("[CABLE BEACH MOD]: Cable Beach service endpoints initialized");
                }
                else
                {
                    m_Log.Error("[CABLE BEACH MOD]: No running HTTP server, Cable Beach service endpoints will not be available");
                }
            }
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public static bool TryGetDefaultCap(Uri baseUri, string capIdentifier, out Uri capability)
        {
            capability = null;

            if (baseUri == null || String.IsNullOrEmpty(capIdentifier))
                return false;

            string fragment;
            if (DEFAULT_CAPS_PATHS.TryGetValue(capIdentifier, out fragment))
            {
                capability = new Uri(baseUri, fragment);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Event handler that is fired when the world service connector is loaded
        /// </summary>
        /// <param name="instance">Reference to the world service connector</param>
        private void WorldServiceConnectorLoadedHandler(WorldServiceConnector instance)
        {
            m_regionWorldServiceConnectors[instance.Scene.RegionInfo.RegionHandle] = instance;
            m_Log.Info("[CABLE BEACH MOD]: Registered region " + instance.Scene.RegionInfo.RegionName);
        }

        #region LLSD Handlers

        private OSD EnableClientMessageHandler(string path, OSD request, string endpoint)
        {
            EnableClientMessage message = new EnableClientMessage();
            EnableClientReplyMessage reply = new EnableClientReplyMessage();

            if (request.Type == OSDType.Map)
            {
                message.Deserialize((OSDMap)request);

                WorldServiceConnector wsConnector;
                if (m_regionWorldServiceConnectors.TryGetValue(message.RegionHandle, out wsConnector))
                {
                    Scene scene = wsConnector.Scene;

                    AgentCircuitData agentData = new AgentCircuitData();
                    agentData.AgentID = message.AgentID;
                    agentData.BaseFolder = UUID.Zero; // TODO: What is this?
                    agentData.CapsPath = CapsUtil.GetRandomCapsObjectPath();
                    agentData.child = message.ChildAgent;
                    agentData.circuitcode = (uint)message.CircuitCode;
                    agentData.firstname = GetStringAttribute(message.Attributes, AvatarAttributes.FIRST_NAME);
                    agentData.lastname = GetStringAttribute(message.Attributes, AvatarAttributes.LAST_NAME);
                    agentData.SecureSessionID = message.SecureSessionID;
                    agentData.SessionID = message.SessionID;
                    agentData.startpos = GetVector3Attribute(message.Attributes, AvatarAttributes.LAST_POSITION);

                    UserAgentData useragent = new UserAgentData();
                    useragent.AgentIP = message.IP.ToString();
                    useragent.AgentOnline = true;
                    useragent.AgentPort = 0u;
                    useragent.Handle = scene.RegionInfo.RegionHandle;
                    useragent.InitialRegion = scene.RegionInfo.RegionID;
                    useragent.LoginTime = Util.UnixTimeSinceEpoch();
                    useragent.LogoutTime = 0;
                    useragent.Position = agentData.startpos;
                    useragent.Region = useragent.InitialRegion;
                    useragent.SecureSessionID = agentData.SecureSessionID;
                    useragent.SessionID = agentData.SessionID;

                    UserProfileData userProfile = new UserProfileData();
                    userProfile.AboutText = GetStringAttribute(message.Attributes, AvatarAttributes.BIOGRAPHY);
                    userProfile.CanDoMask = (uint)GetIntegerAttribute(message.Attributes, AvatarAttributes.CAN_DO);
                    userProfile.Created = (int)Utils.DateTimeToUnixTime(GetDateAttribute(message.Attributes, AvatarAttributes.BIRTH_DATE));
                    userProfile.CurrentAgent = useragent;
                    userProfile.CustomType = "CableBeach";
                    userProfile.FirstLifeAboutText = GetStringAttribute(message.Attributes, AvatarAttributes.FIRST_LIFE_BIOGRAPHY);
                    userProfile.FirstLifeImage = GetUUIDAttribute(message.Attributes, AvatarAttributes.FIRST_LIFE_IMAGE_ID);
                    userProfile.FirstName = agentData.firstname;
                    userProfile.GodLevel = GetIntegerAttribute(message.Attributes, AvatarAttributes.GOD_LEVEL);
                    userProfile.HomeLocation = GetVector3Attribute(message.Attributes, AvatarAttributes.HOME_POSITION);
                    userProfile.HomeLocationX = userProfile.HomeLocation.X;
                    userProfile.HomeLocationY = userProfile.HomeLocation.Y;
                    userProfile.HomeLocationZ = userProfile.HomeLocation.Z;
                    userProfile.HomeLookAt = GetVector3Attribute(message.Attributes, AvatarAttributes.HOME_LOOKAT);
                    userProfile.HomeLookAtX = userProfile.HomeLookAt.X;
                    userProfile.HomeLookAtY = userProfile.HomeLookAt.Y;
                    userProfile.HomeLookAtZ = userProfile.HomeLookAt.Z;
                    userProfile.HomeRegionID = GetUUIDAttribute(message.Attributes, AvatarAttributes.HOME_REGION_ID);
                    userProfile.HomeRegionX = (uint)GetIntegerAttribute(message.Attributes, AvatarAttributes.HOME_REGION_X);
                    userProfile.HomeRegionY = (uint)GetIntegerAttribute(message.Attributes, AvatarAttributes.HOME_REGION_Y);
                    userProfile.HomeRegion = Utils.UIntsToLong(userProfile.HomeRegionX, userProfile.HomeRegionY);
                    userProfile.ID = agentData.AgentID;
                    userProfile.Image = UUID.Zero;
                    userProfile.LastLogin = useragent.LoginTime;
                    userProfile.Partner = GetUUIDAttribute(message.Attributes, AvatarAttributes.PARTNER_ID);
                    userProfile.PasswordHash = "$1$";
                    userProfile.PasswordSalt = String.Empty;
                    userProfile.RootInventoryFolderID = GetUUIDAttribute(message.Attributes, AvatarAttributes.DEFAULT_INVENTORY);
                    userProfile.SurName = agentData.lastname;
                    userProfile.UserFlags = GetIntegerAttribute(message.Attributes, AvatarAttributes.USER_FLAGS);
                    userProfile.WantDoMask = (uint)GetIntegerAttribute(message.Attributes, AvatarAttributes.WANT_DO);
                    userProfile.WebLoginKey = UUID.Zero;
                    // Cable Beach does not tie all endpoints for a service under a single URL, so these won't do
                    userProfile.UserAssetURI = String.Empty;
                    userProfile.UserInventoryURI = String.Empty;

                    // Stick our user data in the cache so the region will know something about us
                    scene.CommsManager.UserProfileCacheService.PreloadUserCache(userProfile);

                    // Add this incoming EnableClient message to the database of active sessions
                    wsConnector.EnableClientMessages.Add(message.Identity, message.AgentID, message);

                    // Call 'new user' event handler
                    string reason;
                    if (wsConnector.NewUserConnection(agentData, out reason))
                    {
                        string capsSeedPath = CapsUtil.GetCapsSeedPath(
                            scene.CapsModule.GetCapsHandlerForUser(agentData.AgentID).CapsObjectPath);

                        // Set the response message to successful
                        reply.Success = true;
                        reply.SeedCapability = wsConnector.GetServiceEndpoint(capsSeedPath);

                        m_Log.Info("[CABLE BEACH MOD]: enable_client succeeded for " + userProfile.Name);
                    }
                    else
                    {
                        reply.Message = "Connection refused: " + reason;
                        m_Log.Error("[CABLE BEACH MOD]: enable_client failed: " + reason);
                    }
                }
                else
                {
                    m_Log.Error("[CABLE BEACH MOD]: enable_client received an unrecognized region handle " + message.RegionHandle);
                }
            }

            return reply.Serialize();
        }

        private OSD CloseAgentConnectionHandler(string path, OSD request, string endpoint)
        {
            // FIXME: Implement this
            return new OSD();
        }

        #endregion LLSD Handlers

        #region Attribute Fetching

        private static UUID GetUUIDAttribute(Dictionary<Uri, OSD> attributes, Uri attribute)
        {
            OSD attributeData;
            if (attributes.TryGetValue(attribute, out attributeData))
                return attributeData.AsUUID();
            return UUID.Zero;
        }

        private static string GetStringAttribute(Dictionary<Uri, OSD> attributes, Uri attribute)
        {
            OSD attributeData;
            if (attributes.TryGetValue(attribute, out attributeData))
                return attributeData.AsString();
            return "<EMPTY>";
        }

        private static int GetIntegerAttribute(Dictionary<Uri, OSD> attributes, Uri attribute)
        {
            OSD attributeData;
            if (attributes.TryGetValue(attribute, out attributeData))
                return attributeData.AsInteger();
            return 0;
        }

        private static DateTime GetDateAttribute(Dictionary<Uri, OSD> attributes, Uri attribute)
        {
            OSD attributeData;
            if (attributes.TryGetValue(attribute, out attributeData))
                return attributeData.AsDate();
            return Utils.Epoch;
        }

        private static Vector3 GetVector3Attribute(Dictionary<Uri, OSD> attributes, Uri attribute)
        {
            OSD attributeData;
            if (attributes.TryGetValue(attribute, out attributeData))
                return attributeData.AsVector3();
            return new Vector3(128f, 128f, 100f);
        }

        #endregion Attribute Fetching
    }
}