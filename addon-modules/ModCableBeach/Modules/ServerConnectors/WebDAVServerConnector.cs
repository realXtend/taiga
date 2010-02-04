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
using System.Web;
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
            avatarWebdav.OnPropFind += AvatarPropFindHandler;
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

        private void CreateCapabilitiesHandler(UUID sessionID, Uri identity, ref Dictionary<Uri, Uri> capabilities)
        {
        }

        private IList<IWebDAVResource> AvatarPropFindHandler(string username_from_request, string path, DepthHeader depth)
        {
            string[] parts = path.Split('/');
            if (parts.Length >= 3 && parts[1] == "avatar")
            {
                string agentIDStr = parts[2];
                string localPath = "/avatar";
                for (int i = 3; i < parts.Length; i++)
                {
                    string part = HttpUtility.UrlDecode(parts[i]);
                    localPath += "/" + part;
                }

                UUID agentID;
                if (UUID.TryParse(agentIDStr, out agentID))
                {
                    List<IWebDAVResource> davEntries = new List<IWebDAVResource>();

                    InventoryNodeBase invObject = PathToInventory(agentID, localPath);
                    if (invObject == null)
                        return davEntries;

                    path = HttpUtility.UrlPathEncode(path);
                    IWebDAVResource resource = InventoryToDAV(path, invObject);

                    // Only add the root to response if the client wants it
                    if (depth != DepthHeader.InfinityNoRoot && depth != DepthHeader.OneNoRoot)
                        davEntries.Add(resource);

                    if (invObject is InventoryFolderImpl && (depth == DepthHeader.One || depth == DepthHeader.Infinity))
                    {
                        InventoryFolderImpl folder = (InventoryFolderImpl)invObject;

                        // Iterate over the child items
                        foreach (InventoryItemBase child in folder.RequestListOfItems())
                        {
                            string name = child.Name;
                            name = HttpUtility.UrlPathEncode(name); //encode spaces to %20 etc

                            string resourcePath;
                            if (path.EndsWith("/"))
                                resourcePath = path + name;
                            else
                                resourcePath = path + "/" + name;

                            resource = InventoryToDAV(resourcePath, child);
                            if (resource != null)
                                davEntries.Add(resource);
                        }

                        // TODO: Iterate over the child folders
                    }

                    return davEntries;
                }
            }

            if (parts.Length == 2 || (parts.Length == 3 && parts[2] == String.Empty))
            {
                // Client requested PROPFIND for avatar/
                // We need to return the avatar folder so some clients (eg. windows explorer) can access its subfolders
                List<IWebDAVResource> davEntries = new List<IWebDAVResource>();
                //TODO: Change the DateTimes to something meaningful
                davEntries.Add(new WebDAVFolder(path, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, false));
                return davEntries;
            }

            return new List<IWebDAVResource>(0);
        }

        private InventoryFolderBase GetRootFolder(InventoryCollection inventory)
        {
            foreach (InventoryFolderBase folder in inventory.Folders)
            {
                if (folder.ParentID == UUID.Zero)
                    return folder;
            }

            return null;
        }

        private InventoryNodeBase FindNode(string name, InventoryFolderBase parent, InventoryCollection inventory)
        {
            foreach (InventoryFolderBase folder in inventory.Folders)
            {
                if (folder.ParentID == parent.ID && folder.Name == name)
                    return folder;
            }

            foreach (InventoryItemBase item in inventory.Items)
            {
                if (item.Folder == parent.ID && item.Name == name)
                    return item;
            }

            return null;
        }

        private InventoryNodeBase PathToInventory(UUID userID, string path)
        {
            if (String.IsNullOrEmpty(path) || path == "/")
                return m_InventoryService.GetRootFolder(userID);

            InventoryCollection inventory = m_InventoryService.GetUserInventory(userID);
            string[] pathElements = path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            // Start at the root folder
            InventoryFolderBase currentFolder = GetRootFolder(inventory);
            if (currentFolder != null)
            {
            }

            // Iterate over each node in the path looking for the matching inventory object
            for (int i = 0; i < pathElements.Length; i++)
            {
                InventoryNodeBase node = FindNode(pathElements[i], currentFolder, inventory);
                if (node == null || i == pathElements.Length - 1)
                    return node;

                if (node is InventoryFolderBase)
                    currentFolder = (InventoryFolderBase)node;
                else
                    return null;
            }

            return null;
        }

        public static string Base64DecodeFromNet(string data)
        {
            try
            {
                data = data.Replace('-', '+');
                data = data.Replace('_', '/');
                byte[] decbuff = Convert.FromBase64String(data);
                return Encoding.UTF8.GetString(decbuff);
            }
            catch (FormatException)
            {
                return String.Empty;
            }
        }

        public static string Base64EncodeToNet(string str)
        {
            byte[] encbuff = System.Text.Encoding.UTF8.GetBytes(str);
            string data = Convert.ToBase64String(encbuff);
            data = data.Replace('/', '_');
            data = data.Replace('+', '-');
            return data;
        }

        public static IWebDAVResource InventoryToDAV(string path, InventoryNodeBase invObject)
        {
            if (invObject is InventoryFolderImpl)
            {
                InventoryFolderImpl folder = (InventoryFolderImpl)invObject;
                return new WebDAVFolder(path, Utils.Epoch, Utils.Epoch, DateTime.UtcNow, false);
            }
            else if (invObject is InventoryItemBase)
            {
                InventoryItemBase item = (InventoryItemBase)invObject;
                DateTime creationDate = Utils.UnixTimeToDateTime(item.CreationDate);
                string contentType = CableBeachUtils.SLAssetTypeToContentType(item.AssetType);
                return new WebDAVFile(path, contentType, 0, creationDate, creationDate, DateTime.UtcNow, false, false);
            }
            else
            {
                return null;
            }
        }
    }
}
