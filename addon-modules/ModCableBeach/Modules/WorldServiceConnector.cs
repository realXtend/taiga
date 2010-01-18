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

namespace ModCableBeach
{
    public delegate void WorldServiceConnectorLoaded(WorldServiceConnector instance);

    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class WorldServiceConnector : INonSharedRegionModule
    {
        /// <summary>Maximum length of time (in milliseconds) before a request to a foreign Cable
        /// Beach service times out</summary>
        const int REQUEST_TIMEOUT = 1000 * 30;

        /// <summary>This event is fired when this module is initialized</summary>
        public static event WorldServiceConnectorLoaded OnWorldServiceConnectorLoaded;

        /// <summary>A reference to the scene this module is attached to</summary>
        public Scene Scene;
        /// <summary>EnableClient messages for all of the active sessions</summary>
        public DoubleDictionary<Uri, UUID, EnableClientMessage> EnableClientMessages = new DoubleDictionary<Uri, UUID, EnableClientMessage>();

        private static readonly ILog m_Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private bool m_Enabled = false;

        public Type ReplaceableInterface { get { return null; } }

        /// <summary>Name of this region module</summary>
        public string Name
        {
            get { return "CableBeachWorldServiceConnector"; }
        }

        /// <summary>
        /// First step in region module loading
        /// </summary>
        public void Initialise(IConfigSource source)
        {
            m_Enabled = true;
            m_Log.Info("[CABLE BEACH WORLD]: Cable Beach enable_client handler initialized");
        }

        public void Close()
        {
        }

        /// <summary>
        /// Second step in region module loading. Fetches several important capabilities from the map
        /// service and registers handlers for the simulator Cable Beach service endpoints
        /// </summary>
        public void AddRegion(Scene scene)
        {
            if (!m_Enabled)
                return;

            Scene = scene;

            // Fire the callback to pass this WorldServiceConnector reference to the other modules
            if (OnWorldServiceConnectorLoaded != null)
                OnWorldServiceConnectorLoaded(this);

            m_Log.Info("[CABLE BEACH WORLD]: Cable Beach world connector enabled for region " + scene.RegionInfo.RegionName);
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Log.Info("[CABLE BEACH WORLD] Region " + scene.RegionInfo.RegionName + " is online");
        }

        #region Public Cable Beach Methods

        public Uri GetIdentity(UUID userID)
        {
            EnableClientMessage ecMessage;
            if (EnableClientMessages.TryGetValue(userID, out ecMessage))
                return ecMessage.Identity;

            return null;
        }

        public Uri GetServiceCapability(Uri identity, Uri serviceIdentifier, Uri serviceMethodIdentifier)
        {
            EnableClientMessage ecMessage;
            if (EnableClientMessages.TryGetValue(identity, out ecMessage))
            {
                Dictionary<Uri, Uri> services;
                if (ecMessage.Services.TryGetValue(serviceIdentifier, out services))
                {
                    Uri capability;
                    if (services.TryGetValue(serviceMethodIdentifier, out capability))
                        return capability;
                }
            }

            return null;
        }

        public Uri GetServiceCapability(UUID userID, Uri serviceIdentifier, Uri serviceMethodIdentifier)
        {
            EnableClientMessage ecMessage;
            if (EnableClientMessages.TryGetValue(userID, out ecMessage))
            {
                Dictionary<Uri, Uri> services;
                if (ecMessage.Services.TryGetValue(serviceIdentifier, out services))
                {
                    Uri capability;
                    if (services.TryGetValue(serviceMethodIdentifier, out capability))
                    {
                        return capability;
                    }
                    else
                    {
                        m_Log.Error("[CABLE BEACH WORLD] Failed looking up capability " + serviceMethodIdentifier +
                            " for avatar " + userID + ". This avatar service has " + services.Count + " capabilities");
                    }
                }
                else
                {
                    m_Log.Error("[CABLE BEACH WORLD] Failed looking up service " + serviceIdentifier +
                        " for avatar " + userID + ". This avatar has " + ecMessage.Services.Count + " services");
                }
            }

            return null;
        }

        #endregion Public Cable Beach Methods

        public bool NewUserConnection(AgentCircuitData agent, out string reason)
        {
            reason = String.Empty;

            m_Log.InfoFormat(
                "[CABLE BEACH WORLD]: Region {0} authenticated and authorized incoming {1} agent {2} {3} {4} (circuit code {5})",
                Scene.RegionInfo.RegionName, (agent.child ? "child" : "root"), agent.firstname, agent.lastname,
                agent.AgentID, agent.circuitcode);

            Scene.CapsModule.NewUserConnection(agent);

            ScenePresence sp = Scene.GetScenePresence(agent.AgentID);
            if (sp != null)
            {
                m_Log.WarnFormat("[CABLE BEACH WORLD]: Adjusting known seeds for existing agent {0} in {1}",
                    agent.AgentID, Scene.RegionInfo.RegionName);

                sp.AdjustKnownSeeds();

                return true;
            }

            Scene.CapsModule.AddCapsHandler(agent.AgentID);

            if (!agent.child)
            {
                // Honor parcel landing type and position.
                ILandObject land = Scene.LandChannel.GetLandObject(agent.startpos.X, agent.startpos.Y);
                if (land != null)
                {
                    if (land.landData.LandingType == (byte)1 && land.landData.UserLocation != Vector3.Zero)
                    {
                        agent.startpos = land.landData.UserLocation;
                    }
                }
            }

            Scene.AuthenticateHandler.AddNewCircuit(agent.circuitcode, agent);

            // rewrite session_id
            CachedUserInfo userinfo = Scene.CommsManager.UserProfileCacheService.GetUserDetails(agent.AgentID);
            if (userinfo != null)
            {
                userinfo.SessionID = agent.SessionID;
            }
            else
            {
                m_Log.WarnFormat("[CABLE BEACH WORLD]: We couldn't find a User Info record for {0}. " +
                    "This is usually an indication that the UUID we're looking up is invalid", agent.AgentID);
            }

            return true;
        }

        public Uri GetServiceEndpoint(string methodName)
        {
            if (!methodName.StartsWith("/"))
                methodName = "/" + methodName;

            string scheme = MainServer.Instance.UseSSL ? "https" : "http";
            string hostname = Scene.RegionInfo.ExternalHostName;
            uint port = MainServer.Instance.Port;
            if (MainServer.Instance.UseSSL)
            {
                if (!String.IsNullOrEmpty(MainServer.Instance.SSLCommonName))
                    hostname = MainServer.Instance.SSLCommonName;
                port = MainServer.Instance.SSLPort;
            }
            string authority = (port == 80) ? hostname : hostname + ":" + port;

            return new Uri(scheme + "://" + authority + methodName);
        }

        /// <summary>
        /// Gets the region flags set by the estate module for this scene. If no estate module is
        /// loaded, a default set of flags is returned
        /// </summary>
        /// <returns>RegionFlags for the current scene</returns>
        private uint GetRegionFlags()
        {
            IEstateModule estateModule = Scene.RequestModuleInterface<IEstateModule>();

            if (estateModule != null)
                return estateModule.GetRegionFlags();
            else
                return 72458694; // Found this undocumented number in OpenSim
        }
    }
}
