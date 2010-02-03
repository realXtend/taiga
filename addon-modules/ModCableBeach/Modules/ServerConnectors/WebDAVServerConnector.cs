/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using log4net;
using Nini.Config;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using CableBeachMessages;

namespace ModCableBeach.ServerConnectors
{
    public class WebDAVServerConnector : ServiceConnector
    {
        const string CONFIG_NAME = "WebDAVService";

        private static readonly ILog m_log = LogManager.GetLogger("CableBeachWebDAVServer");

        private IInventoryService m_InventoryService;

        public WebDAVServerConnector(IConfigSource config, IHttpServer server) :
            base(config, server, CONFIG_NAME)
        {
            IConfig serverConfig = config.Configs["InventoryService"];
            if (serverConfig == null)
                throw new Exception("No InventoryService section in config file");

            string inventoryService = serverConfig.GetString("LocalServiceModule", String.Empty);

            Object[] args = new Object[] { config };
            m_InventoryService = ServerUtils.LoadPlugin<IInventoryService>(inventoryService, args);

            if (m_InventoryService == null)
                throw new Exception("Failed to load IInventoryService \"" + inventoryService + "\"");

            // Avatar WebDAV service endpoint
            WebDAVListener avatarWebdav = new WebDAVListener(server, "/avatar");
            avatarWebdav.Authentication = AuthenticationType.None;
            //avatarWebdav.OnPropFind += AvatarPropFindHandler;
            //avatarWebdav.OnLock += AvatarLockHandler;
            //avatarWebdav.OnUnlock += AvatarUnlockHandler;

            // Inventory WebDAV service endpoint
            WebDAVListener inventoryWebdav = new WebDAVListener(server, "/inventory");
            inventoryWebdav.Authentication = AuthenticationType.Digest;
            //inventoryWebdav.OnPropFind += InventoryPropFindHandler;
            //inventoryWebdav.OnLock += InventoryLockHandler;
            //inventoryWebdav.OnUnlock += InventoryUnlockHandler;
            //inventoryWebdav.OnNewCol += InventoryNewColHandler;
            //inventoryWebdav.OnMove += InventoryMoveHandler;
            //inventoryWebdav.OnGet += InventoryGetHandler;
            //inventoryWebdav.OnPut += InventoryPutHandler;
            //inventoryWebdav.OnDelete += InventoryDeleteHandler;
            //inventoryWebdav.OnCopy += InventoryCopyHandler;
            //inventoryWebdav.OnPropPatch += InventoryPropPatchHandler;
            //inventoryWebdav.OnDigestAuthenticate += InventoryOnDigestAuthenticateHandler;

            // Register this server connector as a Cable Beach service
            CableBeachServerState.RegisterService(new Uri(CableBeachServices.FILESYSTEM), CreateCapabilitiesHandler);

            CableBeachServerState.Log.Info("[CABLE BEACH WEBDAV]: WebDAVServerConnector is running");
        }

        void CreateCapabilitiesHandler(UUID sessionID, Uri identity, ref Dictionary<Uri, Uri> capabilities)
        {
        }
    }
}
