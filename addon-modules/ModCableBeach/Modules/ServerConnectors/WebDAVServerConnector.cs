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
using Nwc.XmlRpc;
using System.Collections;

namespace ModCableBeach.ServerConnectors
{
    public class WebDAVServerConnector : ServiceConnector
    {
        const string CONFIG_NAME = "WebDAVService";
        
        private const string AVATAR_FOLDER_PATH = "/Avatar/";

        private static readonly ILog m_log = LogManager.GetLogger("CableBeachWebDAVServer");

        private delegate HttpStatusCode CopyMoveItemHandler(UUID agent_id, string local_destionation, InventoryNodeBase source_node,
                                                      Dictionary<string, HttpStatusCode> status_values,
                                                      string destName);
        private delegate HttpStatusCode CopyMoveFolderHandler(UUID agent_id, string local_destionation, InventoryNodeBase source_node,
                                                      DepthHeader depth,
                                                      Dictionary<string, HttpStatusCode> status_values,
                                                      string destName);

        private IInventoryService m_InventoryService;
        private IAssetService m_AssetService;
        private IPropertyProvider m_PropertyProvider;

        private WebDAVPropertyHandler m_AvatarPropertyHandler;
        private WebDAVPropertyHandler m_InventoryPropertyHandler;
        private WebDAVLockHandler     m_InventoryLockHandler;
        private WebDAVTimeOutHandler  m_InventoryLockTimeOutHandler;

        private string m_ServiceUrl;
        private string m_OpenIDProvider;
        private string m_UserService;
        private int m_ServiceTimeout;

        private bool m_UseLocks;
        private bool m_UseLockTimeouts;

        public WebDAVServerConnector(IConfigSource config, IHttpServer server) :
            base(config, server, CONFIG_NAME)
        {
            IConfig serverConfig = config.Configs["InventoryService"];
            if (serverConfig == null)
                throw new Exception("No InventoryService section in config file");

            string inventoryService = serverConfig.GetString("LocalServiceModule", String.Empty);

            IConfig assetConfig = config.Configs["AssetService"];
            if (assetConfig == null)
                throw new Exception("No InventoryService section in config file");

            string assetService = assetConfig.GetString("LocalServiceModule", String.Empty);

            Object[] args = new Object[] { config };
            m_InventoryService = ServerUtils.LoadPlugin<IInventoryService>(inventoryService, args);
            m_AssetService = ServerUtils.LoadPlugin<IAssetService>(assetService, args);
            
            if (m_InventoryService == null)
                throw new Exception("Failed to load IInventoryService \"" + inventoryService + "\"");

            IConfig cablebeachConfig = config.Configs["CableBeachService"];
            if (cablebeachConfig == null)
                throw new Exception("No CableBeachService section in config file");

            m_ServiceUrl = cablebeachConfig.GetString("ServiceUrl", String.Empty);
            m_OpenIDProvider = cablebeachConfig.GetString("OpenIDProvider", String.Empty);
            m_UserService = cablebeachConfig.GetString("UserService", String.Empty);
            m_ServiceTimeout = cablebeachConfig.GetInt("ServiceTimeOut", 10000);
            m_UseLocks = cablebeachConfig.GetBoolean("UseLocks", true);
            m_UseLockTimeouts = cablebeachConfig.GetBoolean("UseLockTimeouts", true);

            // Get property provider in ini file: choices currently NHibernatePropertyStorage and DummyPropertyProvider
            string propertyProvider = cablebeachConfig.GetString("PropertyProvider", "NHibernatePropertyStorage");

            // Get all property providers in this assembly
            System.Reflection.Assembly a = System.Reflection.Assembly.GetExecutingAssembly();
            Type[] constructorParams = new Type[] { };
            object[] parameters = new object[] { };
            bool found = false;
            foreach (Type t in a.GetTypes())
            {
                try
                {
                    Type[] interfaces = t.GetInterfaces();
                    foreach (Type i in interfaces)
                    {
                        if (i.Name.Equals("IPropertyProvider"))
                            if (t.Name == propertyProvider)
                            {
                                System.Reflection.ConstructorInfo info = t.GetConstructor(constructorParams);
                                m_PropertyProvider = (IPropertyProvider)info.Invoke(parameters);
                                found = true;
                                break;
                            }
                    }
                    if (found)
                        break;
                }
                catch (Exception e) { throw e; }
            }
            m_PropertyProvider.Initialize(config);
            //m_PropertyProvider = new DummyPropertyProvider();

            m_InventoryLockHandler = new WebDAVLockHandler(this, m_InventoryPropertyHandler, "inventory", "/");
            m_AvatarPropertyHandler = new WebDAVPropertyHandler(this, m_InventoryService, m_AssetService, m_PropertyProvider, m_InventoryLockHandler, "avatar", AVATAR_FOLDER_PATH);
            m_InventoryPropertyHandler = new WebDAVPropertyHandler(this, m_InventoryService, m_AssetService, m_PropertyProvider, m_InventoryLockHandler, "inventory", "/");
            

            // Avatar WebDAV service endpoint
            WebDAVListener avatarWebdav = new WebDAVListener(server, "/avatar");
            avatarWebdav.Authentication = AuthenticationType.None;
            avatarWebdav.OnPropFind += m_AvatarPropertyHandler.PropFindHandler;
            avatarWebdav.OnGet += AvatarGetHandler;
            //avatarWebdav.OnLock += AvatarLockHandler;
            //avatarWebdav.OnUnlock += AvatarUnlockHandler;

            // Inventory WebDAV service endpoint
            WebDAVListener inventoryWebdav = new WebDAVListener(server, "/inventory");
            //inventoryWebdav.Authentication = AuthenticationType.None;
            //inventoryWebdav.Authentication = AuthenticationType.Digest;
            inventoryWebdav.Authentication = AuthenticationType.Basic;

            inventoryWebdav.OnGet += InventoryGetHandler;
            inventoryWebdav.OnCopy += InventoryCopyHandler;
            inventoryWebdav.OnPropFind += m_InventoryPropertyHandler.PropFindHandler;
            //inventoryWebdav.OnDigestAuthenticate += InventoryOnDigestAuthenticateHandler;
            //inventoryWebdav.OnDigestAuthenticate += new DigestAuthenticationCallback(inventoryWebdav_OnDigestAuthenticate);
            inventoryWebdav.OnBasicAuthenticate += InventoryBasicAuthenticationHandler;

            // if we use locks and timeouts we proxy these calls, through timeout handler and then through lock handler
            // if we use locks but not timeouts we only through lock handler
            if (m_UseLocks && m_UseLockTimeouts) 
            {
                WebDAVTransparentLockTimeOutHandler handler = WebDAVTransparentLockTimeOutHandler.CreateProxyHandler<ILockProxy>(m_InventoryLockHandler, m_InventoryLockHandler);
                ILockProxy proxy = (ILockProxy)handler.GetTransparentProxy();
                
                m_InventoryLockHandler.m_useTimeOuts = true;
                m_InventoryLockHandler.m_WebDAVTimeOutHandler = handler;
                WebDAVTransparentLockTimeOutHandler.MAX_TIME_OUT = cablebeachConfig.GetLong("MaxTimeOut", 60 * 60 * 24 * 7); // default set to one week
                                                                                                                             // -1 = infinite
                WebDAVTransparentLockTimeOutHandler.FORCE_SERVER_TIMEOUT = cablebeachConfig.GetBoolean("ForseServerTimeOut", false);
                inventoryWebdav.OnNewCol += proxy.MkcolCallback;
                inventoryWebdav.OnMove += proxy.MoveCallback;
                inventoryWebdav.OnPut += proxy.PutCallback;
                inventoryWebdav.OnDelete += proxy.DeleteCallback;
                inventoryWebdav.OnPropPatch += proxy.PropPatchCallback;
                inventoryWebdav.OnLock += proxy.LockHandler;
                inventoryWebdav.OnUnlock += m_InventoryLockHandler.UnlockHandler; // dont need to be proxied
            }
            else if (m_UseLocks && !m_UseLockTimeouts)
            {
                inventoryWebdav.OnNewCol += m_InventoryLockHandler.MkcolCallback;
                inventoryWebdav.OnMove += m_InventoryLockHandler.MoveCallback;
                inventoryWebdav.OnPut += m_InventoryLockHandler.PutCallback;
                inventoryWebdav.OnDelete += m_InventoryLockHandler.DeleteCallback;
                inventoryWebdav.OnPropPatch += m_InventoryLockHandler.PropPatchCallback;
                inventoryWebdav.OnLock += m_InventoryLockHandler.LockHandler;
                inventoryWebdav.OnUnlock += m_InventoryLockHandler.UnlockHandler;
            }
            else
            {
                inventoryWebdav.OnNewCol += InventoryNewColHandler;
                inventoryWebdav.OnMove += InventoryMoveHandler;
                inventoryWebdav.OnPut += InventoryPutHandler;
                inventoryWebdav.OnDelete += InventoryDeleteHandler;
                inventoryWebdav.OnPropPatch += m_InventoryPropertyHandler.PropPatchHandler;
            }


            // Register this server connector as a Cable Beach service
            CableBeachServerState.RegisterService(new Uri(CableBeachServices.FILESYSTEM), CreateCapabilitiesHandler);

            // Inventory and avatar url handler
            server.AddStreamHandler(new TrustedStreamHandler("GET", "/get_inventory_webdav_url", new WebDavInventoryUrlHandler(m_ServiceUrl))); 
            server.AddStreamHandler(new TrustedStreamHandler("GET", "/get_avatar_webdav_url", new WebDavAvatarUrlHandler(m_ServiceUrl)));

            CableBeachServerState.Log.Info("[CABLE BEACH WEBDAV]: WebDAVServerConnector is running");
        }

        public HttpStatusCode InventoryMoveHandler(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<string, HttpStatusCode> multiStatusValues)
        {
            return MoveAndCopyWorker(username, uri, destination, depth, overwrite, ifHeaders, out multiStatusValues, false);
        }

        HttpStatusCode InventoryCopyHandler(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<string, HttpStatusCode> multiStatusValues)
        {
            return MoveAndCopyWorker(username, uri, destination, depth, overwrite, ifHeaders, out multiStatusValues, true);
        }

        public HttpStatusCode InventoryDeleteHandler(Uri uri, string username, out Dictionary<string, HttpStatusCode> multi_status, string[] if_headers)
        {
            multi_status = null;
            string localPath = "";
            string path = uri.LocalPath;

            UUID agentID = AgentIDFromRequestPath("inventory", "/", path, ref localPath);

            string parentFolderPath = GetParentPathFromLocalPath(localPath);
            //check parent folder exists
            InventoryNodeBase inb = PathToInventory(agentID, parentFolderPath);
            if (inb != null)
            {
                InventoryCollection ic = m_InventoryService.GetFolderContent(agentID, inb.ID);
                if (localPath.EndsWith("/")) { localPath = localPath.Substring(0, localPath.Length - 1); }
                InventoryNodeBase target = PathToInventory(agentID, localPath);

                // check if there is folder or item in the path
                //InventoryNodeBase nodeBase = CheckIDInInventoryCollection(ic, inb.ID);
                InventoryNodeBase nodeBase = CheckIDInInventoryCollection(ic, target.ID);
                if (nodeBase != null)
                {
                    multi_status = new Dictionary<string, HttpStatusCode>();
                    if (nodeBase is InventoryItemBase)
                    {
                        // delete item
                        List<UUID> uuids = new List<UUID>();
                        uuids.Add(nodeBase.ID);
                        if (m_InventoryService.DeleteItems(agentID, uuids))
                        {       
                            // sending multiple status even if there's only one status, because DeleteCommand otherwise sends internal server error
                            m_InventoryPropertyHandler.DeleteProperties(path);
                            multi_status.Add(uri.ToString(), HttpStatusCode.OK);
                            return (HttpStatusCode)207;
                        }
                        else
                            return HttpStatusCode.InternalServerError;
                    }
                    else if (nodeBase is InventoryFolderBase) 
                    {
                        InventoryFolderBase ifb = (InventoryFolderBase)nodeBase;
                        if(RecursiveDelete(ifb, localPath, multi_status, agentID)){
                            return (HttpStatusCode)207;
                        } else {
                            return HttpStatusCode.InternalServerError;
                        }
                    }
                }
            }
            return HttpStatusCode.NotFound;
        }

        public HttpStatusCode InventoryNewColHandler(string path, string username, string[] if_headers)
        {
            string localPath = "";

            UUID agentID = AgentIDFromRequestPath("inventory", "/", path, ref localPath);

            InventoryCollection ic = m_InventoryService.GetUserInventory(agentID);
            if (localPath == "/") // Trying to create root
            {
                return HttpStatusCode.MethodNotAllowed;
            }
            else 
            {
                // remove some "/" from path
                localPath = localPath.Substring(1, localPath.Length - 2);

                InventoryFolderBase userRootFolder = this.GetRootFolder(ic);
                string[] parts = localPath.Split('/');
                string pathToCreate = GetParentPathFromLocalPath(localPath);
                //for (int i = 0; i < parts.Length - 1; i++) 
                //{
                //    pathToCreate += parts[i] + "/";
                //}
                //if (pathToCreate == "")
                //    pathToCreate = "/";

                string folderName = parts[parts.Length-1];

                InventoryNodeBase parentFolder = this.PathToInventory(agentID, pathToCreate);
                if (parentFolder != null) // if parent folder exists, try create folder
                {
                    // check if exists (no folder or item with same name allowed)
                    InventoryCollection collection = m_InventoryService.GetFolderContent(agentID, parentFolder.ID);
                    foreach (InventoryFolderBase iFolder in collection.Folders)
                    {
                        if (iFolder.Name == folderName) {
                            return HttpStatusCode.Conflict;
                        }
                    }
                    foreach (InventoryItemBase iItem in collection.Items)
                    {
                        if (iItem.Name == folderName)
                        {
                            return HttpStatusCode.Conflict;
                        }
                    }

                    InventoryFolderBase newFolder = new InventoryFolderBase(UUID.Random(), folderName, agentID, parentFolder.ID);
                    m_InventoryService.AddFolder(newFolder);
                    // test if exists?
                    // m_InventoryService.GetFolder needs InventoryFolderBase to return InventoryFolderBase?

                    return HttpStatusCode.Created;
                }
                else 
                {
                    return HttpStatusCode.MethodNotAllowed;
                }
            }
        }

        private void CreateCapabilitiesHandler(UUID sessionID, Uri identity, ref Dictionary<Uri, Uri> capabilities)
        {
        }

        public HttpStatusCode InventoryPutHandler(OSHttpRequest request, string path, string username, string[] if_headers)
        {
            // TODO: Figure out ETAG checks
            byte[] assetData = request.GetBody();
            if (assetData.Length == 0)
                return HttpStatusCode.BadRequest;
            string localPath = "";
            UUID agentID = AgentIDFromRequestPath("inventory", "/", path, ref localPath);

            string[] pathParts = localPath.Split('/');
            string assetName;
            if(localPath.EndsWith("/"))
            {
                assetName = pathParts[pathParts.Length - 2];
                localPath = localPath.Substring(0, localPath.Length - (assetName.Length + 1));
            }
            else
            {
                assetName = pathParts[pathParts.Length - 1];
                localPath = localPath.Substring(0, localPath.Length - assetName.Length);
            }
            InventoryNodeBase invObject = PathToInventory(agentID, localPath);

            if (invObject != null)
            {
                if (invObject is InventoryFolderBase)
                {
                    InventoryFolderBase parentFolder = (InventoryFolderBase)invObject;
                    OpenSim.Framework.AssetMetadata assetMetadata = new OpenSim.Framework.AssetMetadata();
                    assetMetadata.ContentType = request.Headers["Content-type"];
                    assetMetadata.CreationDate = DateTime.Now;
                    assetMetadata.Description = assetName;
                    assetMetadata.ID = UUID.Random().ToString();
                    assetMetadata.Name = assetName;
                    assetMetadata.Temporary = false;
                    //assetMetadata.SHA256 = OpenMetaverse.Utils.SHA256(assetData);
                    assetMetadata.SHA1 = OpenMetaverse.Utils.SHA1(assetData);
                    
                    sbyte type = CableBeachUtils.ContentTypeToSLAssetType(request.Headers["Content-type"]);

                    AssetBase asset = null;

                    bool existingAssetUpdate = false;
                    if (request.Headers["Overwrite"].Equals("T"))
                    {
                        InventoryItemBase invAssetItem = null;
                        InventoryNodeBase invAssetNode = PathToInventory(agentID, localPath + assetName);
                        if (invAssetNode is InventoryItemBase)
                            invAssetItem = (InventoryItemBase)invAssetNode;
                        if (invAssetItem != null)
                        {
                            asset = m_AssetService.Get(invAssetItem.AssetID.ToString());
                            if (asset != null)
                            {
                                assetMetadata = m_AssetService.GetMetadata(asset.ID);
                                existingAssetUpdate = true;
                            }
                        }
                    }

                    if (!existingAssetUpdate)
                        asset = new AssetBase(UUID.Random(), assetName, type);

                    asset.Data = assetData;
                    asset.Metadata = assetMetadata;
                    string ret = m_AssetService.Store(asset);

                    // Check if asset was created
                    if (m_AssetService.GetMetadata(assetMetadata.ID) != null)
                    {
                        InventoryItemBase inventoryItem = new InventoryItemBase();
                        inventoryItem.AssetID = new UUID(assetMetadata.ID);
                        inventoryItem.AssetType = parentFolder.Type;
                        // TODO: conversion from long to int migth not be sufficient here
                        inventoryItem.CreationDate = (int) DateTime.Now.Ticks; 
                        inventoryItem.CreatorId = agentID.ToString();
                        inventoryItem.Owner = agentID;
                        inventoryItem.CurrentPermissions = 2147483647;
                        inventoryItem.NextPermissions = 2147483647;
                        inventoryItem.BasePermissions = 2147483647;
                        inventoryItem.EveryOnePermissions = 2147483647;
                        inventoryItem.GroupPermissions = 2147483647;
                        inventoryItem.InvType = (int)CableBeachMessages.InventoryType.Object;
                        inventoryItem.GroupOwned = false;
                        inventoryItem.Description = assetMetadata.Description;
                        inventoryItem.ID = UUID.Random();
                        inventoryItem.Name = assetMetadata.Name;
                        inventoryItem.Folder = parentFolder.ID;
                        inventoryItem.SalePrice = 0;
                        if (!existingAssetUpdate)
                        {
                            if (m_InventoryService.AddItem(inventoryItem))
                                return HttpStatusCode.Created;
                        }
                        else
                        {
                            if (m_InventoryService.UpdateItem(inventoryItem))
                                return HttpStatusCode.Created;
                        }
                    }
                    else 
                    { 
                        // failed asset creation, dont create inventory item either sender, send error back
                        //return HttpStatusCode.PreconditionFailed & HttpStatusCode.InternalServerError;
                        return HttpStatusCode.InternalServerError;
                    }
                }
            }
            return HttpStatusCode.InternalServerError;
        }

        bool inventoryWebdav_OnDigestAuthenticate(string username, out PasswordFormat format, out string password)
        {
            /*/
            password = "test";
            format = PasswordFormat.MD5;
            return true;
            /*/
            Uri owner;

            format = PasswordFormat.Plain;
            password = String.Empty; //TODO: this should eventually be some session generated passwd

            if (Uri.TryCreate(username, UriKind.Absolute, out owner))
            {
                UUID agentID = CableBeachUtils.IdentityToUUID(owner);
                UUID ownerID = CableBeachUtils.MessageToUUID(owner, agentID);


            }
            return false;
            //*/
        }

        HttpStatusCode AvatarGetHandler(OSHttpResponse response, string path, string username)
        {
            string localPath = "";
            UUID agentID = AgentIDFromRequestPath("avatar", AVATAR_FOLDER_PATH, path, ref localPath);
            if (agentID != UUID.Zero)
            {
                return GetWorker(response, path, username, localPath, agentID);
            }
            return HttpStatusCode.BadRequest;
        }

        HttpStatusCode InventoryGetHandler(OSHttpResponse response, string path, string username)
        {
            string localPath = "";
            UUID agentID = AgentIDFromRequestPath("inventory", "/", path, ref localPath);
            if (agentID != UUID.Zero)
            {
                return GetWorker(response, path, username, localPath, agentID);
            }
            return HttpStatusCode.BadRequest;
        }

        private HttpStatusCode GetWorker(OSHttpResponse response, string path, string username, string local_path, UUID agentID)
        {
            HttpStatusCode status = HttpStatusCode.NotFound;
            InventoryNodeBase invObject = PathToInventory(agentID, local_path);

            if (invObject is InventoryItemBase)
            {
                InventoryItemBase item = (InventoryItemBase)invObject;
                byte[] assetData = m_AssetService.GetData(item.AssetID.ToString());

                if (assetData != null && assetData.Length != 0)
                {
                    status = HttpStatusCode.OK;
                    //response.ContentType = "application/octet-stream";
                    response.ContentType = CableBeachUtils.SLAssetTypeToContentType(((InventoryItemBase)invObject).AssetType);
                    //response.AddHeader("Content-Disposition", "attachment; filename=" + item.Name);
                    response.ContentLength = assetData.Length;
                    response.Body.Write(assetData, 0, assetData.Length);
                }
                else
                {
                    status = HttpStatusCode.NotFound;
                    //status = HttpStatusCode.InternalServerError;
                }
            }
            else if (invObject is InventoryFolderBase)
            {
                InventoryFolderBase folder = (InventoryFolderBase)invObject;
                string body = String.Empty;
                string myPath = path;

                if (myPath[myPath.Length - 1] != '/')
                    myPath += "/";
                if (myPath[0] == '/')
                    myPath = myPath.Substring(1, myPath.Length - 1);

                // HTML
                body += "<html>" +
                        "<head>" +
                        "<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" />" +
                        "<title>" + folder.Name.ToString() + "</title>" +
                        "</head>" +
                        "<body>" +
                        "<p>";
                if (local_path != "/") {
                    body += "<a href=\"../\">../</a><br>";
                }

                InventoryCollection ic = m_InventoryService.GetFolderContent(agentID, folder.ID);

                local_path = local_path.Substring(1);
                foreach (InventoryFolderBase ifb in ic.Folders)
                {
                    body += "<a href=\"" + ifb.Name + "/\">" + ifb.Name + "/";
                    body += "</a><br>";
                }

                foreach (InventoryItemBase iib in ic.Items)
                {
                    body += "<a href=\"" + iib.Name + "\">" + iib.Name;
                    body += "</a><br>";
                }


                body += "</p></body></html>";

                UTF8Encoding ecoding = new UTF8Encoding();
                response.ContentType = "text";
                response.ContentLength = Encoding.UTF8.GetBytes(body).Length;
                response.Body.Write(Encoding.UTF8.GetBytes(body), 0, Encoding.UTF8.GetBytes(body).Length);
                status = HttpStatusCode.OK;
            }
            return status;
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

        public InventoryNodeBase PathToInventory(UUID userID, string path)
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
            if (invObject is InventoryFolderBase)
            {
                InventoryFolderBase folder = (InventoryFolderBase)invObject;
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

        public static UUID AgentIDFromRequestPath(string path_parent_folder,
                                                  string initial_local_path,
                                                  string path,
                                                  ref string local_path)
        {
            if (path.EndsWith("/")) 
            {
                path = path.Substring(0, path.Length - 1);
            }
            string[] parts = path.Split('/');
            local_path = "";
            if (parts.Length >= 3 && parts[1] == path_parent_folder)
            {
                string localPath = initial_local_path;
                for (int i = 3; i < parts.Length; i++)
                {
                    string part = HttpUtility.UrlDecode(parts[i]);
                    localPath += part + "/";
                }
                string agentStr = parts[2];
                UUID agentID = UUID.Parse(agentStr);
                local_path = localPath;
                return agentID;
            }
            return UUID.Zero;
        }

        public bool InventoryBasicAuthenticationHandler(string username, string password)
        {
            string uuid = GetUUID(username);
            return CheckPassword(uuid, password);
        }

        public string GetUUID(string username)
        {
            string userServiceUrl = m_UserService;
            int timeOutMs = m_ServiceTimeout;

            string method = "get_user_by_name";

            Hashtable param = new Hashtable();
            param["avatar_name"] = username; 
            IList parameters = new ArrayList();
            parameters.Add(param);

            XmlRpcRequest request = new XmlRpcRequest(method, parameters);
            XmlRpcResponse response = request.Send(userServiceUrl, timeOutMs);
            Hashtable respData = (Hashtable)response.Value;

            if (response.IsFault)
            {
                return "";
            }
            string uuid = (string)respData["uuid"];
            return uuid;
        }

        public bool CheckPassword(string uuid, string password)
        {
            string userServiceUrl = m_UserService;
            int timeOutMs = m_ServiceTimeout;
            string method = "authenticate_user_by_password";
            Hashtable param = new Hashtable();
            param["user_uuid"] = uuid; 
            param["password"] = password;
            IList parameters = new ArrayList();
            parameters.Add(param);

            XmlRpcRequest request = new XmlRpcRequest(method, parameters);
            XmlRpcResponse response = request.Send(userServiceUrl, timeOutMs);

            Hashtable respData = (Hashtable)response.Value;
            if (response.IsFault)
            {
                return false;
            }

            if ((string)respData["auth_user"] == "TRUE")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="localPath"></param>
        /// <returns></returns>
        private static string GetParentPathFromLocalPath(string localPath)
        {
            if (localPath.StartsWith("/")) { localPath = localPath.Substring(1);}
            if (localPath.EndsWith("/")) { localPath = localPath.Substring(0, localPath.Length-1); }
            string[] parts = localPath.Split('/');
            string parentPath = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                parentPath += parts[i] + "/";
            }
            if (parentPath == "")
                parentPath = "/";

            return parentPath;
        }

        private static InventoryNodeBase CheckIDInInventoryCollection(InventoryCollection ic, UUID uuid)
        {
            foreach (InventoryFolderBase ifb in ic.Folders) 
            {
                if (ifb.ID == uuid)
                    return ifb;
            }
            foreach (InventoryItemBase iib in ic.Items)
            {
                if (iib.ID == uuid)
                    return iib;
            }
            return null;
        }

        /// <summary>
        /// Deleting recursevily folders
        /// </summary>
        /// <param name="ic"></param>
        /// <param name="delete_status"></param>
        /// <returns></returns>
        private bool RecursiveDelete(InventoryFolderBase folder,
                                     string uri,
                                     Dictionary<string, HttpStatusCode> delete_status,
                                     UUID agent_id)
        {
            InventoryCollection ic = m_InventoryService.GetFolderContent(agent_id, folder.ID);
            // need to delete all items one by one so we now which delete succeeded
            foreach (InventoryItemBase iib in ic.Items) 
            { 
                List<UUID> iibList = new List<UUID>();
                iibList.Add(iib.ID);
                if (m_InventoryService.DeleteItems(agent_id, iibList))
                {
                    // TODO: according to webdav we would not need to send successful deletes only failures
                    m_InventoryPropertyHandler.DeleteProperties(
                        "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, iib));
                    delete_status.Add(uri + "/" + iib.Name, HttpStatusCode.OK);
                }
                else 
                {
                    // TODO: rigth status codes
                    delete_status.Add(uri + "/" + iib.Name, HttpStatusCode.Unauthorized);
                }
            }
            foreach (InventoryFolderBase ifb in ic.Folders) {
                // Recursive call
                if (RecursiveDelete(ifb, uri + "/" + ifb.Name, delete_status, agent_id)) {
                    m_InventoryPropertyHandler.DeleteProperties(
                        "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, ifb));
                    delete_status.Add(uri + "/" + ifb.Name, HttpStatusCode.OK);
                } else {
                    // TODO: rigth status codes
                    delete_status.Add(uri + "/" + ifb.Name, HttpStatusCode.PreconditionFailed);
                }
            }
            // delete this folder
            List<UUID> folderList = new List<UUID>();
            folderList.Add(folder.ID);
            if (m_InventoryService.DeleteFolders(agent_id, folderList))
            {
                m_InventoryPropertyHandler.DeleteProperties(
                    "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, folder));
                delete_status.Add(uri + "/" + folder.Name + "/", HttpStatusCode.OK);
                return true;
            }
            else 
            {
                // TODO: rigth status codes
                delete_status.Add(uri + "/" + folder.Name + "/", HttpStatusCode.Unauthorized);
                return false;
            }

        }

        private HttpStatusCode CopyItem(UUID agent_id, InventoryItemBase source_item, InventoryFolderBase destination, string destName)
        {
            // make sourceItem new item
            // make copy
            InventoryItemBase copy = (InventoryItemBase)source_item.Clone();
            copy.ID = UUID.Random();
            copy.Owner = agent_id;
            copy.Name = destName;
            copy.Folder = destination.ID;

            if (m_InventoryService.AddItem(copy))
            {
                return HttpStatusCode.Created;
            }

            else 
            {
                return HttpStatusCode.InternalServerError;
            }
        }

        private HttpStatusCode RecursiveCopyFolder(UUID agent_id, 
                                                   InventoryFolderBase source, 
                                                   InventoryFolderBase destinationParent, 
                                                   DepthHeader depth,
                                                   Dictionary<string, HttpStatusCode> status_values) 
        {
            InventoryFolderBase copyFolderBase = new InventoryFolderBase(UUID.Random(), source.Name,
                                                                         agent_id, 
                                                                         destinationParent.ID);

            if (m_InventoryService.AddFolder(copyFolderBase)) 
            {
                m_InventoryPropertyHandler.CopyProperties(agent_id, source,
                    "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, copyFolderBase));

                InventoryCollection ic = m_InventoryService.GetFolderContent(agent_id, source.ID);
                if (depth == DepthHeader.Infinity) // copy folders
                {
                    foreach (InventoryFolderBase ifb in ic.Folders) { 
                        // create destination folder
                        HttpStatusCode status = RecursiveCopyFolder(agent_id, ifb, copyFolderBase, depth, status_values);
                        if (status == HttpStatusCode.OK) 
                        {
                            m_InventoryPropertyHandler.CopyProperties(agent_id, ifb,
                                "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, copyFolderBase) + ifb.Name + "/");
                        }
                        status_values.Add(NodeToPath(agent_id, ifb), status);
                    }
                }
                // copy items
                foreach (InventoryItemBase iib in ic.Items) 
                {
                    HttpStatusCode status = CopyItem(agent_id, iib, copyFolderBase, iib.Name);
                    if (status == HttpStatusCode.Created) 
                    {
                        m_InventoryPropertyHandler.CopyProperties(agent_id, iib,
                            "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, copyFolderBase) + iib.Name);
                    }
                    status_values.Add(NodeToPath(agent_id, iib), status);
                }
            }
            else 
            {   // Failed folder creation, so wont create subfolders or items for the folder either
                status_values.Add(NodeToPath(agent_id, copyFolderBase), HttpStatusCode.InternalServerError);
            }

            return HttpStatusCode.OK;
        }

        public string NodeToPath(UUID agent_id, InventoryNodeBase node)
        {
            string path = "";
            UUID parentID = UUID.Zero;
            InventoryFolderBase currentFolder = null;
            if (node is InventoryFolderBase) { parentID = ((InventoryFolderBase)node).ParentID; path = node.Name + "/"; }
            if (node is InventoryItemBase) { parentID = ((InventoryItemBase)node).Folder; path = node.Name; }
            List<InventoryFolderBase> folders = m_InventoryService.GetInventorySkeleton(agent_id);
            while (parentID != UUID.Zero)
            {
                InventoryFolderBase fetchFolder = new InventoryFolderBase(parentID, agent_id);
                currentFolder = m_InventoryService.GetFolder(fetchFolder);
                if (currentFolder.ParentID != UUID.Zero)
                {
                    path = currentFolder.Name + "/" + path;
                }
                parentID = currentFolder.ParentID;
            }
            return path;
        }


        /// <summary>
        /// Create folder path and return folder in the path, if exist return the existing folder
        /// if create_parent is true create parent of the folder or return existing parent
        /// </summary>
        /// <param name="agent_id"></param>
        /// <param name="destination_path"></param>
        /// <param name="create_parent"></param>
        /// <returns></returns>
        private InventoryFolderBase CreateFolderPath(UUID agent_id, string destination_path, bool create_parent) 
        {
            InventoryFolderBase root = m_InventoryService.GetRootFolder(agent_id);
            //InventoryCollection rootCollection = m_InventoryService.GetFolderContent(agent_id, root.ID);
            List<InventoryFolderBase> allFolders = m_InventoryService.GetInventorySkeleton(agent_id);
            // Go thru path from beginning to point of first non existing folder
            
            if (destination_path.EndsWith("/")) // cut trailing "/" off
            {
                destination_path = destination_path.Substring(0, destination_path.Length - 1);
            }

            string[] split = destination_path.Split('/');
            List<string> foundPath = new List<string>();
            int count;
            if (create_parent)
                count = split.Length - 1;
            else
                count = split.Length;

            int existingFoldersCount = 0;
            InventoryFolderBase currentFolder = root;
            foreach (string folderStr in split) 
            {
                bool found = false;
                if (create_parent) 
                {
                    if (existingFoldersCount == count)
                        break; // we have found enough folders
                }

                foreach (InventoryFolderBase ifb in allFolders)
                {
                    if (ifb.ParentID == currentFolder.ID)
                    {
                        if (folderStr == ifb.Name) 
                        {
                            currentFolder = ifb;
                            foundPath.Add(folderStr);
                            found = true;
                            existingFoldersCount++;
                            break;
                        }
                    }
                }
                if (!found)
                    break;
            }

            // Create rest of the folders
            for (int i = existingFoldersCount; i < count; i++) 
            {
                InventoryFolderBase ifb = new InventoryFolderBase(UUID.Random(), split[i], agent_id, currentFolder.ID);
                m_InventoryService.AddFolder(ifb);
                currentFolder = ifb;
            }
            return currentFolder;
        }

        private HttpStatusCode CopyItemToNonExistentDestination(UUID agent_id,
                                                      string local_destination,
                                                      InventoryNodeBase source_node,
                                                      Dictionary<string, HttpStatusCode> status_values,
                                                      string destName) 
        {
            InventoryFolderBase destinationParentFolder = CreateFolderPath(agent_id, local_destination, true);
            //fetch item and copy it
            InventoryItemBase sourceItemBase = m_InventoryService.GetItem(new InventoryItemBase(source_node.ID, agent_id));
            HttpStatusCode created = CopyItem(agent_id, sourceItemBase, destinationParentFolder, destName);
            if (created == HttpStatusCode.Created) // copy item properties
            {
                //m_InventoryPropertyHandler.CopyProperties(path, "/inventory/" + agent_id.ToString() + "/" + local_destination);
                m_InventoryPropertyHandler.CopyProperties(agent_id, source_node, "/inventory/" + agent_id.ToString() + "/" + local_destination);
            }
            status_values.Add(NodeToPath(agent_id, source_node), created);
            return (HttpStatusCode)207;
        }

        private HttpStatusCode CopyFolderToNonExistentDestination(UUID agent_id,
                                                      string local_destionation,
                                                      InventoryNodeBase source_node,
                                                      DepthHeader depth, 
                                                      Dictionary<string, HttpStatusCode> status_values,
                                                      string destName)
        {
            InventoryFolderBase destinationParentFolder = CreateFolderPath(agent_id, local_destionation, true);
            //fetch folder and copy it
            InventoryFolderBase sourceFolderToCopy = m_InventoryService.GetFolder(new InventoryFolderBase(source_node.ID, agent_id));
            sourceFolderToCopy.Name = destName;
            HttpStatusCode ret = RecursiveCopyFolder(agent_id, sourceFolderToCopy, destinationParentFolder,
                                                     depth, status_values);
            status_values.Add(NodeToPath(agent_id, source_node), ret);
            return (HttpStatusCode)207;
        }

        private HttpStatusCode MoveItemToNonExistentDestination(UUID agent_id,
                                                      string local_destination,
                                                      InventoryNodeBase source_node,
                                                      Dictionary<string, HttpStatusCode> status_values,
                                                      string destName) 
        {
            InventoryFolderBase destinationParentFolder = CreateFolderPath(agent_id, local_destination, true);
            InventoryItemBase sourceItemBase = m_InventoryService.GetItem(new InventoryItemBase(source_node.ID, agent_id));
            string sourcePath = "/inventory/" + agent_id.ToString() + "/" + NodeToPath(agent_id, sourceItemBase);
            sourceItemBase.Folder = destinationParentFolder.ID;
            sourceItemBase.Name = destName;
            if (m_InventoryService.UpdateItem(sourceItemBase))
            {
                m_InventoryPropertyHandler.MoveProperties(sourcePath, "/inventory/" + agent_id.ToString() + "/" + local_destination);
                status_values.Add(NodeToPath(agent_id, source_node), HttpStatusCode.OK);
            }
            else 
            {
                status_values.Add(NodeToPath(agent_id, source_node), HttpStatusCode.InternalServerError);
            }
            return HttpStatusCode.OK;
        }

        private HttpStatusCode MoveFolderToNonExistentDestination(UUID agent_id,
                                                      string local_destination,
                                                      InventoryNodeBase source_node,
                                                      DepthHeader depth, 
                                                      Dictionary<string, HttpStatusCode> status_values,
                                                      string destName)
        {
            InventoryFolderBase destinationParentFolder = CreateFolderPath(agent_id, local_destination, true);
            InventoryFolderBase sourceFolderBase = m_InventoryService.GetFolder(new InventoryFolderBase(source_node.ID, agent_id));
            List<string> customProperties = m_InventoryPropertyHandler.PreparePropertiesMove(agent_id, source_node);
            sourceFolderBase.ParentID = destinationParentFolder.ID;
            sourceFolderBase.Name = destName;
            if (m_InventoryService.UpdateFolder(sourceFolderBase))
            {
                m_InventoryPropertyHandler.MoveProperties(agent_id, sourceFolderBase, customProperties);
                status_values.Add(NodeToPath(agent_id, source_node), HttpStatusCode.OK);
            }
            else
            {
                status_values.Add(NodeToPath(agent_id, source_node), HttpStatusCode.InternalServerError);
            }
            return HttpStatusCode.OK;
        }


        private HttpStatusCode MoveAndCopyWorker(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<string, HttpStatusCode> multiStatusValues, bool copy)
        {
            destination = HttpUtility.UrlDecode(destination);

            CopyMoveItemHandler copyMoveItemHandler = null;
            CopyMoveFolderHandler copyMoveFolderHandler = null;
            if (copy)
            {
                copyMoveItemHandler = new CopyMoveItemHandler(CopyItemToNonExistentDestination);
                copyMoveFolderHandler = new CopyMoveFolderHandler(CopyFolderToNonExistentDestination);
            }
            else 
            {
                copyMoveItemHandler = new CopyMoveItemHandler(MoveItemToNonExistentDestination);
                copyMoveFolderHandler = new CopyMoveFolderHandler(MoveFolderToNonExistentDestination);            
            }

            multiStatusValues = null;
            string source = uri.AbsoluteUri;
            source = HttpUtility.UrlDecode(source);
            string[] srcParts = source.ToString().Split('/');
            string[] dstParts = destination.Split('/');
            for (int i = 0; i < 3; i++)
            {
                if (srcParts[i] != dstParts[i])
                {
                    //error, some of the following has happened
                    //a) source or destination did not contain the whole absolute uri
                    //b) source and destination use different protocol http vs. https
                    //c) source and destination are in different domain
                    return HttpStatusCode.BadGateway;
                }
            }

            string localPath = "";
            string path = uri.LocalPath;
            UUID agentID = AgentIDFromRequestPath("inventory", "/", path, ref localPath);
            string localDestionation = destination;
            // get local destination cut out the http://127.0.0.1:8003/inventory/00000000-0000-0000-0000-000000000000/ part
            string[] parts = localDestionation.Split(new char[] { '/' }, 6);
            localDestionation = parts[5];

            if (destination.EndsWith("/"))
                destination = destination.Substring(0, destination.Length - 1);

            string destinationName = destination.Substring(destination.LastIndexOf('/') + 1); // destination object or collection name

            // Check if source & destination paths exist
            // check if we moving an item or collection
            // get source and change parentID

            // Check if source & destination paths exist
            bool destinationNodeExists = true;
            // get source node
            InventoryNodeBase sourceNode = PathToInventory(agentID, localPath);
            if (sourceNode == null)
                return HttpStatusCode.NotFound;
            // destination node
            InventoryNodeBase destinationNode = PathToInventory(agentID, localDestionation);
            if (destinationNode == null) { destinationNodeExists = false; }

            // Check if source is folder or item
            bool sourceIsItem = false;
            InventoryFolderBase fetchFolder = new InventoryFolderBase(sourceNode.ID, agentID);
            InventoryFolderBase sourceFolderBase = m_InventoryService.GetFolder(fetchFolder);
            if (sourceFolderBase == null) { sourceIsItem = true; }

            // if destination exists check if its an item
            bool destinationIsItem = false;
            InventoryFolderBase DestinationFolder = null;
            if (destinationNodeExists)
            {
                InventoryFolderBase fetchDestinationFolder = new InventoryFolderBase(destinationNode.ID, agentID);
                DestinationFolder = m_InventoryService.GetFolder(fetchDestinationFolder);
                if (DestinationFolder == null) { destinationIsItem = true; }
            }

            // Check if destination node does not exist check if its parent exists
            bool destinationParentExists = false;
            bool destinationParentIsFolder = false;
            InventoryNodeBase destinationParentNode = null;
            if (!destinationNodeExists)
            {
                if (localDestionation.EndsWith("/")) localDestionation = localDestionation.Substring(0, localDestionation.Length - 1);
                string destinationParentPath = localDestionation.Substring(0, localDestionation.LastIndexOf('/'));
                destinationParentNode = PathToInventory(agentID, destinationParentPath);
                if (destinationParentNode != null) 
                {
                    destinationParentExists = true;
                    // if parent exists make sure its actually a folder
                    if (destinationParentNode is InventoryFolderBase) {
                        destinationParentIsFolder = true;
                    }
                }
            }

            multiStatusValues = new Dictionary<string, HttpStatusCode>();

            // Go thru these scenarios
            // 1. source is an item and destination does not exist
            //      1.1. destination parent folder exists
            //      1.2. destination parent exist but is an item
            //      1.3. destination parent folder does not exist
            // 2. source is folder and destination does not exist
            //      2.1. destination parent folder exists
            //      2.2. destination parent exist but is an item
            //      2.3. destination parent folder does not exist
            // 3. source and destination are both items
            // 4. source and destination are both folders
            // 5. source is item and destination is folder
            // 6. source is folder and destination is item

            // 1. :
            if (sourceIsItem && !destinationNodeExists)
            {
                // 1.1.
                if (destinationParentExists && destinationParentIsFolder)
                {
                    HttpStatusCode result = copyMoveItemHandler(agentID, localDestionation, sourceNode, multiStatusValues, destinationName);
                    //m_InventoryPropertyHandler.CopyProperties(path, "/inventory/" + agentID.ToString() + "/" + localDestionation);
                    return (HttpStatusCode) 207;
                }
                // 1.2.
                if (destinationParentExists && !destinationParentIsFolder) 
                {
                    if (!overwrite)
                    { 
                        return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues, 
                            new HttpStatusCode[] {HttpStatusCode.NotAcceptable}); 
                    }
                    // TODO: figure out right politics here
                    // could delete the item and write folder in its place like this:
                    //List<UUID> uuids = new List<UUID>();
                    //uuids.Add(destinationParentNode.ID);
                    //if (m_InventoryService.DeleteItems(agentID, uuids)) {
                    //    return copyMoveItemHandler(agentID, localDestionation, sourceNode, multiStatusValues, destinationName);
                    //}
                    // easy way out
                    return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                        new HttpStatusCode[] { HttpStatusCode.Conflict });
                }
                // 1.3.
                if (!destinationParentExists && !destinationParentIsFolder) 
                {
                    if (!overwrite)
                    {
                        return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                            new HttpStatusCode[] { HttpStatusCode.NotAcceptable });
                    }
                    // we could do path creation if overwrite is true, we need to overwrite if there's item in the path that has same
                    // name as folder in the path
                    // taking easy way out return conflict:
                    return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                        new HttpStatusCode[] { HttpStatusCode.Conflict });
                }                
            }

            // 2. :
            if (!sourceIsItem && !destinationNodeExists)
            {
                // 1.1.
                if (destinationParentExists && destinationParentIsFolder)
                    return copyMoveFolderHandler(agentID, localDestionation, sourceNode, depth, multiStatusValues, destinationName);
                // 1.2.
                if (destinationParentExists && !destinationParentIsFolder)
                {
                    if (!overwrite)
                    {
                        return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                            new HttpStatusCode[] { HttpStatusCode.NotAcceptable });
                    }
                    // TODO: figure out right politics here
                    // could delete the item and write folder in its place like this:
                    //List<UUID> uuids = new List<UUID>();
                    //uuids.Add(destinationParentNode.ID);
                    //if (m_InventoryService.DeleteItems(agentID, uuids))
                    //{
                    //    return copyMoveItemHandler(agentID, localDestionation, sourceNode, multiStatusValues, destinationName);
                    //}
                    // easy way out
                    return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                        new HttpStatusCode[] { HttpStatusCode.Conflict });
                }
                // 1.3.
                if (!destinationParentExists && !destinationParentIsFolder) // we could do path creation if overwrite is true
                {
                    if (!overwrite)
                    {
                        return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                            new HttpStatusCode[] { HttpStatusCode.NotAcceptable });
                    }
                    // we could do path creation if overwrite is true, we need to overwrite if there's item in the path that has same
                    // name as folder in the path
                    // taking easy way out return conflict:
                    // easy way out
                    return SetErrorsAndReturnMultiStatus(agentID, sourceNode, ref multiStatusValues,
                        new HttpStatusCode[] { HttpStatusCode.Conflict });
                }                
            }

            // 3. :
            if (sourceIsItem && destinationIsItem)
            {
                if (!overwrite)
                {
                    multiStatusValues.Add(NodeToPath(agentID, sourceNode), HttpStatusCode.Conflict);
                    return (HttpStatusCode)207; ;
                }
                // delete destination item
                List<UUID> items = new List<UUID>(); items.Add(destinationNode.ID);
                if (!m_InventoryService.DeleteItems(agentID, items))
                {
                    return HttpStatusCode.InternalServerError;
                }
                // reduces to 1:
                return copyMoveItemHandler(agentID, localDestionation, sourceNode, multiStatusValues, destinationName);
            }

            // 4. :
            if (!sourceIsItem && !destinationIsItem)
            {
                if (!overwrite)
                {
                    multiStatusValues.Add(NodeToPath(agentID, sourceNode), HttpStatusCode.Conflict);
                    return (HttpStatusCode)207; ;
                }
                // delete destination folder
                RecursiveDelete(DestinationFolder, NodeToPath(agentID, destinationNode), multiStatusValues, agentID);
                // reduces to 2:
                return copyMoveFolderHandler(agentID, localDestionation, sourceNode, depth, multiStatusValues, destinationName);
            }

            // 5. :
            if (sourceIsItem && !destinationIsItem)
            {
                if (!overwrite)
                {
                    multiStatusValues.Add(NodeToPath(agentID, sourceNode), HttpStatusCode.Conflict);
                    return (HttpStatusCode)207; ;
                }
                // delete destination folder
                RecursiveDelete(DestinationFolder, NodeToPath(agentID, destinationNode), multiStatusValues, agentID);
                // reduces to 1:
                return copyMoveItemHandler(agentID, localDestionation, sourceNode, multiStatusValues, destinationName);
            }

            // 6. :
            if (!sourceIsItem && destinationIsItem)
            {
                if (!overwrite)
                {
                    multiStatusValues.Add(NodeToPath(agentID, sourceNode), HttpStatusCode.Conflict);
                    return (HttpStatusCode)207; ;
                }
                // delete destination item
                List<UUID> items = new List<UUID>(); items.Add(destinationNode.ID);
                if (!m_InventoryService.DeleteItems(agentID, items))
                {
                    return HttpStatusCode.InternalServerError;
                }
                // reduces to 2:
                return copyMoveFolderHandler(agentID, localDestionation, sourceNode, depth, multiStatusValues, destinationName);
            }

            return HttpStatusCode.InternalServerError;
        }

        private HttpStatusCode SetErrorsAndReturnMultiStatus(UUID agent_id, InventoryNodeBase source_node, ref Dictionary<string, HttpStatusCode> multiStatus, HttpStatusCode[] errors) 
        {
            foreach (HttpStatusCode hsc in errors) {
                multiStatus.Add(NodeToPath(agent_id, source_node), hsc);
            }
            return (HttpStatusCode)207;
        }

    }

    public class WebDavInventoryUrlHandler : BaseStreamHandler
    {
        private string m_ServiceUrl;

        public WebDavInventoryUrlHandler(string serviceUrl) :
            base("GET", "/get_inventory_webdav_url")
        {
            m_ServiceUrl = serviceUrl;
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            byte[] result = OpenMetaverse.Utils.EmptyBytes;
            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;

            UUID avatarUuid;
            string avatar_address;
            string avatarUuidString = httpRequest.Headers.Get("Avatar-UUID");

            if (UUID.TryParse(avatarUuidString, out avatarUuid) && m_ServiceUrl != string.Empty)
            {
                string pathString;
                if (m_ServiceUrl.EndsWith("/"))
                    pathString = "inventory/" + avatarUuid.ToString();
                else
                    pathString = "/inventory/" + avatarUuid.ToString();
                avatar_address = m_ServiceUrl + pathString;
                httpResponse.AddHeader("Inventory-Webdav-Url", avatar_address);
                httpResponse.StatusCode = (int)HttpStatusCode.OK;
            }

            return result;
        }
    }

    public class WebDavAvatarUrlHandler : BaseStreamHandler
    {
        private string m_ServiceUrl;

        public WebDavAvatarUrlHandler(string serviceUrl) :
            base("GET", "/get_avatar_webdav_url")
        {
            m_ServiceUrl = serviceUrl;
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            byte[] result = OpenMetaverse.Utils.EmptyBytes;
            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;

            UUID avatarUuid;
            string avatar_address;
            string avatarUuidString = httpRequest.Headers.Get("Avatar-UUID");

            if (UUID.TryParse(avatarUuidString, out avatarUuid) && m_ServiceUrl != string.Empty)
            {
                string pathString;
                if (m_ServiceUrl.EndsWith("/"))
                    pathString = "avatar/" + avatarUuid.ToString() + "/Avatar.xml";
                else
                    pathString = "/avatar/" + avatarUuid.ToString() + "/Avatar.xml";
                avatar_address = m_ServiceUrl + pathString;
                httpResponse.AddHeader("Avatar-Webdav-Url", avatar_address);
                httpResponse.StatusCode = (int)HttpStatusCode.OK;
            }

            return result;
        }
    }
}
