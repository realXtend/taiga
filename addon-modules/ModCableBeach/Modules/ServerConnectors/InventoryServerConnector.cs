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

namespace ModCableBeach
{
    public class InventoryServerConnector : ServiceConnector
    {
        private static readonly ILog m_log = LogManager.GetLogger("CableBeachInventoryServer");

        private IInventoryService m_InventoryService;

        public InventoryServerConnector(IConfigSource config, IHttpServer server) :
            base(config, server, "InventoryService")
        {
            IConfig serverConfig = config.Configs["InventoryService"];
            if (serverConfig == null)
                throw new Exception("No InventoryService section in config file");

            string inventoryService = serverConfig.GetString("LocalServiceModule", String.Empty);

            if (String.IsNullOrEmpty(inventoryService))
                throw new Exception("No LocalServiceModule in InventoryService section in config file");

            Object[] args = new Object[] { config };
            m_InventoryService = ServerUtils.LoadPlugin<IInventoryService>(inventoryService, args);

            if (m_InventoryService == null)
                throw new Exception("Failed to load IInventoryService \"" + inventoryService + "\"");

            // Inventory service endpoints
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/create_filesystem", new CBCreateInventoryHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/create_object", new CBCreateObjectHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/get_object", new CBGetObjectHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/get_filesystem_skeleton", new CBGetInventorySkeletonHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/get_root_folder", new CBGetRootFolderHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/purge_folder", new CBPurgeFolderHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/delete_object", new CBDeleteObjectHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/get_folder_contents", new CBGetFolderContentsHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/get_folder_for_type", new CBGetFolderForTypeHandler(m_InventoryService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/filesystem/get_active_gestures", new CBGetActiveGesturesHandler(m_InventoryService)));

            // TODO: Remove these legacy handlers once the UserServer->InventoryServer mess in OpenSim is sorted out
            CheckTrustedSourceMethod nullCheck = delegate(IPEndPoint peer) { return true; };
            server.AddStreamHandler(new RestDeserialiseTrustedHandler<Guid, bool>("POST", "/CreateInventory/", LegacyCreateUsersInventoryHandler, nullCheck));
            server.AddStreamHandler(new RestDeserialiseTrustedHandler<Guid, List<InventoryFolderBase>>("POST", "/RootFolders/", LegacyRootFoldersHandler, nullCheck));
            server.AddStreamHandler(new RestDeserialiseTrustedHandler<Guid, List<InventoryItemBase>>("POST", "/ActiveGestures/", LegacyActiveGesturesHandler, nullCheck));

            // Register this server connector as a Cable Beach service
            CableBeachServerState.RegisterService(new Uri(CableBeachServices.FILESYSTEM), CreateCapabilitiesHandler);

            CableBeachServerState.Log.Info("[CABLE BEACH INVENTORY]: InventoryServerConnector is running");
        }

        void CreateCapabilitiesHandler(UUID sessionID, Uri identity, ref Dictionary<Uri, Uri> capabilities)
        {
            Uri[] caps = new Uri[capabilities.Count];
            capabilities.Keys.CopyTo(caps, 0);

            for (int i = 0; i < caps.Length; i++)
            {
                Uri cap = caps[i];
                string capName = cap.ToString();

                switch (capName)
                {
                    case CableBeachServices.FILESYSTEM_CREATE_FILESYSTEM:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBCreateInventoryHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_CREATE_OBJECT:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBCreateObjectHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_GET_OBJECT:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBGetObjectHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_GET_FILESYSTEM_SKELETON:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBGetInventorySkeletonHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_GET_FILESYSTEM:
                        m_log.Error("[CABLE BEACH INVENTORY]: Got a request for deprecated get_filesystem capability");
                        break;
                    case CableBeachServices.FILESYSTEM_GET_ROOT_FOLDER:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBGetRootFolderHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_PURGE_FOLDER:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBPurgeFolderHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_DELETE_OBJECT:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBDeleteObjectHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_GET_FOLDER_CONTENTS:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBGetFolderContentsHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_GET_FOLDER_FOR_TYPE:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBGetFolderForTypeHandler(m_InventoryService), false, identity);
                        break;
                    case CableBeachServices.FILESYSTEM_GET_ACTIVE_GESTURES:
                        capabilities[cap] = CableBeachServerState.CreateCapability(sessionID, new CBGetActiveGesturesHandler(m_InventoryService), false, identity);
                        break;
                }
            }
        }

        #region Inventory Endpoint Handlers

        public class CBCreateInventoryHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBCreateInventoryHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    CreateInventoryMessage message = new CreateInventoryMessage();
                    message.Deserialize(map);

                    CreateInventoryReplyMessage reply = new CreateInventoryReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    if (!m_InventoryService.HasInventoryForUser(ownerID))
                    {
                        if (m_InventoryService.CreateUserInventory(ownerID))
                            m_log.Info("[CABLE BEACH INVENTORY]: create_inventory succeeded for user " + ownerID);
                        else
                            m_log.Error("[CABLE BEACH INVENTORY]: create_inventory failed for user " + ownerID);
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH INVENTORY]: create_inventory called for user " + ownerID + " who already has an inventory");
                    }

                    InventoryFolderBase rootFolder = m_InventoryService.GetRootFolder(ownerID);
                    if (rootFolder != null)
                    {
                        reply.Success = true;
                    }
                    else
                    {
                        reply.Success = false;
                        reply.Message = "failed to create inventory for " + ownerID;
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: create_inventory called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBCreateObjectHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBCreateObjectHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    CreateObjectMessage message = new CreateObjectMessage();
                    message.Deserialize(map);

                    CreateObjectReplyMessage reply = new CreateObjectReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    if (message.Object is InventoryBlockItem)
                    {
                        #region InventoryBlockItem Handling

                        InventoryBlockItem incomingObj = (InventoryBlockItem)message.Object;
                        InventoryItemBase item = new InventoryItemBase(incomingObj.ID, ownerID);
                        bool create = false;

                        // Try to fetch the existing item first
                        InventoryItemBase fetchedItem;
                        if (item.ID != UUID.Zero && (fetchedItem = m_InventoryService.GetItem(item)) != null)
                        {
                            // Item already existed in the database, this is an update
                            item = fetchedItem;

                            // Set the assetID
                            if (incomingObj.AssetID != UUID.Zero)
                                item.AssetID = incomingObj.AssetID;

                            // Set the parentID
                            if (incomingObj.ParentID != UUID.Zero)
                                item.Folder = incomingObj.ParentID;
                        }
                        else
                        {
                            // No item previously existed, this is a create
                            create = true;

                            // Set the itemID
                            item.ID = (incomingObj.ID != UUID.Zero) ?
                                incomingObj.ID :
                                UUID.Random();

                            // Set the assetID
                            item.AssetID = (incomingObj.AssetID != UUID.Zero) ?
                                incomingObj.AssetID :
                                UUID.Zero;

                            // Set the parentID
                            item.Folder = (incomingObj.ParentID != UUID.Zero) ?
                                incomingObj.ParentID :
                                ServiceHelper.GetFolderForType(m_InventoryService, ownerID, incomingObj.ContentType);

                            item.AssetType = (int)CableBeachUtils.ContentTypeToSLAssetType(incomingObj.ContentType);
                            item.InvType = (int)CableBeachUtils.ContentTypeToSLInvType(incomingObj.ContentType);
                            item.CreationDate = (int)OpenMetaverse.Utils.DateTimeToUnixTime(DateTime.Now);
                            item.CreatorIdAsUuid = incomingObj.CreatorID;
                            item.CreatorId = incomingObj.CreatorID.ToString();
                        }

                        // Set the name
                        if (!String.IsNullOrEmpty(incomingObj.Name))
                            item.Name = incomingObj.Name;
                        if (item.Name == null)
                            item.Name = "(no name)";

                        // Set the description
                        if (!String.IsNullOrEmpty(incomingObj.Description))
                            item.Description = incomingObj.Description;
                        if (item.Description == null)
                            item.Description = String.Empty;

                        // Set everything else
                        item.BasePermissions = incomingObj.PermsBase;
                        item.CurrentPermissions = incomingObj.PermsOwner;
                        item.EveryOnePermissions = incomingObj.PermsEveryone;
                        item.Flags = incomingObj.Flags;
                        item.GroupID = incomingObj.GroupID;
                        item.GroupOwned = incomingObj.GroupOwned;
                        item.GroupPermissions = incomingObj.PermsGroup;
                        item.NextPermissions = incomingObj.PermsNext;
                        item.SalePrice = incomingObj.SalePrice;
                        item.SaleType = (byte)incomingObj.SaleType;

                        if (create)
                        {
                            reply.Success = m_InventoryService.AddItem(item);

                            if (!reply.Success)
                            {
                                m_log.Error("[CABLE BEACH INVENTORY]: AddItem failed for item " + item.ID);
                                reply.Message = "item creation failed";
                            }
                        }
                        else
                        {
                            reply.Success = m_InventoryService.UpdateItem(item);

                            if (!reply.Success)
                            {
                                m_log.Error("[CABLE BEACH INVENTORY]: UpdateItem failed for item " + item.ID);
                                reply.Message = "item update failed";
                            }
                        }

                        #endregion InventoryBlockItem Handling
                    }
                    else if (message.Object is InventoryBlockFolder)
                    {
                        #region InventoryBlockFolder Handling

                        InventoryBlockFolder incomingObj = (InventoryBlockFolder)message.Object;
                        InventoryFolderBase folder = new InventoryFolderBase(incomingObj.ID, ownerID);
                        bool create = false;

                        // Try to fetch the existing folder first
                        InventoryFolderBase fetchedFolder;
                        if (incomingObj.ID != UUID.Zero && (fetchedFolder = m_InventoryService.GetFolder(folder)) != null)
                        {
                            // Folder already existed in the filesystem, this is an update
                            folder = fetchedFolder;

                            // Set the parentID
                            if (incomingObj.ParentID != UUID.Zero)
                                folder.ParentID = incomingObj.ParentID;

                            // Update the version
                            ++folder.Version;
                        }
                        else
                        {
                            // No folder previously existed, this is a create
                            create = true;

                            // Set the folderID
                            folder.ID = (incomingObj.ID != UUID.Zero) ?
                                incomingObj.ID :
                                UUID.Random();

                            // Set the parentID
                            folder.ParentID = (incomingObj.ParentID != UUID.Zero) ?
                                incomingObj.ParentID :
                                ServiceHelper.GetFolderForType(m_InventoryService, ownerID, "application/vnd.ll.folder");

                            // Set the preferred content type
                            folder.Type = (short)CableBeachUtils.ContentTypeToSLAssetType(incomingObj.PreferredContentType);

                            // Set the version
                            folder.Version = 1;
                        }

                        // Set the name
                        if (!String.IsNullOrEmpty(incomingObj.Name))
                            folder.Name = incomingObj.Name;

                        if (create)
                        {
                            reply.Success = m_InventoryService.AddFolder(folder);

                            if (!reply.Success)
                            {
                                m_log.Error("[CABLE BEACH INVENTORY]: AddFolder failed for folder " + folder.ID);
                                reply.Message = "folder creation failed";
                            }
                        }
                        else
                        {
                            reply.Success = m_InventoryService.UpdateFolder(folder);

                            if (!reply.Success)
                            {
                                m_log.Error("[CABLE BEACH INVENTORY]: UpdateFolder failed for folder " + folder.ID);
                                reply.Message = "folder update failed";
                            }
                        }

                        #endregion InventoryBlockFolder Handling
                    }
                    else
                    {
                        m_log.Error("[CABLE BEACH INVENTORY]: create_object called with unrecognized object");
                        httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                        return Utils.EmptyBytes;
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: create_object called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBGetObjectHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBGetObjectHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    GetObjectMessage message = new GetObjectMessage();
                    message.Deserialize(map);

                    GetObjectReplyMessage reply = new GetObjectReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    if (message.IsFolder)
                    {
                        // Folder request
                        InventoryFolderBase folder = new InventoryFolderBase(message.ObjectID, ownerID);
                        folder = m_InventoryService.GetFolder(folder);
                        if (folder != null)
                        {
                            reply.Success = true;
                            reply.Object = ServiceHelper.InventoryToMessage(folder);
                        }
                        else
                        {
                            m_log.Error("[CABLE BEACH INVENTORY]: get_object could not find folder " + message.ObjectID);
                            reply.Message = "folder not found";
                        }
                    }
                    else
                    {
                        // Item request
                        InventoryItemBase item = new InventoryItemBase(message.ObjectID, ownerID);
                        item = m_InventoryService.GetItem(item);
                        if (item != null)
                        {
                            reply.Success = true;
                            reply.Object = ServiceHelper.InventoryToMessage(item);
                        }
                        else
                        {
                            m_log.Error("[CABLE BEACH INVENTORY]: get_object could not find item " + message.ObjectID);
                            reply.Message = "item not found";
                        }
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: get_object called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBGetInventorySkeletonHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBGetInventorySkeletonHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    GetInventorySkeletonMessage message = new GetInventorySkeletonMessage();
                    message.Deserialize(map);

                    GetInventorySkeletonReplyMessage reply = new GetInventorySkeletonReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    List<InventoryFolderBase> skeleton = m_InventoryService.GetInventorySkeleton(ownerID);

                    if (skeleton != null)
                    {
                        reply.Folders = new GetInventorySkeletonReplyMessage.Folder[skeleton.Count];
                        for (int i = 0; i < skeleton.Count; i++)
                        {
                            InventoryFolderBase invFolder = skeleton[i];

                            GetInventorySkeletonReplyMessage.Folder folder = new GetInventorySkeletonReplyMessage.Folder();
                            folder.FolderID = invFolder.ID;
                            folder.Name = invFolder.Name;
                            folder.ParentID = invFolder.ParentID;
                            folder.PreferredContentType = CableBeachUtils.SLAssetTypeToContentType(invFolder.Type);

                            reply.Folders[i] = folder;
                        }
                    }
                    else
                    {
                        reply.Folders = new GetInventorySkeletonReplyMessage.Folder[0];
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: get_inventory_skeleton called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBGetRootFolderHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBGetRootFolderHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    GetRootFolderMessage message = new GetRootFolderMessage();
                    message.Deserialize(map);

                    GetRootFolderReplyMessage reply = new GetRootFolderReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    InventoryFolderBase rootFolder = m_InventoryService.GetRootFolder(ownerID);

                    if (rootFolder != null && rootFolder.ID != UUID.Zero)
                    {
                        reply.Success = true;
                        reply.RootFolder = (InventoryBlockFolder)ServiceHelper.InventoryToMessage(rootFolder);
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH INVENTORY]: No root folder found for user " + ownerID);
                        reply.Message = "failed to find root folder for user " + ownerID;
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: get_root_folder called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBPurgeFolderHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBPurgeFolderHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    PurgeFolderMessage message = new PurgeFolderMessage();
                    message.Deserialize(map);

                    PurgeFolderReplyMessage reply = new PurgeFolderReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    InventoryFolderBase folder = new InventoryFolderBase(message.FolderID, ownerID);
                    reply.Success = m_InventoryService.PurgeFolder(folder);

                    if (!reply.Success)
                    {
                        m_log.Error("[CABLE BEACH INVENTORY]: Failed to purge folder " + folder.ID + " for user " + folder.Owner);
                        reply.Message = "failed to purge folder " + folder.ID + " for user " + folder.Owner;
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: purge_folder called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBDeleteObjectHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBDeleteObjectHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    DeleteObjectMessage message = new DeleteObjectMessage();
                    message.Deserialize(map);

                    DeleteObjectReplyMessage reply = new DeleteObjectReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    InventoryFolderBase folder = new InventoryFolderBase(message.ObjectID, ownerID);
                    if (m_InventoryService.GetFolder(folder) != null)
                    {
                        m_InventoryService.PurgeFolder(folder);
                        reply.Success = m_InventoryService.DeleteFolders(ownerID, new List<UUID> { message.ObjectID });

                        if (!reply.Success)
                        {
                            m_log.Error("[CABLE BEACH INVENTORY]: failed to delete folder " + message.ObjectID);
                            reply.Message = "failed to delete folder";
                        }
                    }
                    else
                    {
                        reply.Success = m_InventoryService.DeleteItems(ownerID, new List<UUID> { message.ObjectID });

                        if (!reply.Success)
                        {
                            m_log.Error("[CABLE BEACH INVENTORY]: failed to delete item " + message.ObjectID);
                            reply.Message = "failed to delete item";
                        }
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: delete_object called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBGetFolderContentsHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBGetFolderContentsHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    GetFolderContentsMessage message = new GetFolderContentsMessage();
                    message.Deserialize(map);

                    GetFolderContentsReplyMessage reply = new GetFolderContentsReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    InventoryCollection contents = m_InventoryService.GetFolderContent(ownerID, message.FolderID);

                    if (contents != null)
                    {
                        reply.Objects = new InventoryBlock[contents.Folders.Count + contents.Items.Count];
                        int i = 0;

                        foreach (InventoryFolderBase invFolder in contents.Folders)
                            reply.Objects[i++] = ServiceHelper.InventoryToMessage(invFolder);

                        foreach (InventoryItemBase invItem in contents.Items)
                            reply.Objects[i++] = ServiceHelper.InventoryToMessage(invItem);
                    }
                    else
                    {
                        reply.Objects = new InventoryBlock[0];
                    }

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: get_folder_contents called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBGetFolderForTypeHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBGetFolderForTypeHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    GetFolderForTypeMessage message = new GetFolderForTypeMessage();
                    message.Deserialize(map);

                    GetFolderForTypeReplyMessage reply = new GetFolderForTypeReplyMessage();
                    reply.FolderForType = new GetFolderForTypeReplyMessage.Folder();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    InventoryFolderBase folder = m_InventoryService.GetFolderForType(ownerID,
                        (OpenMetaverse.AssetType)CableBeachUtils.ContentTypeToSLAssetType(message.ContentType));

                    if (folder != null && folder.ID != UUID.Zero)
                    {
                        reply.FolderForType.FolderID = folder.ID;
                        reply.FolderForType.Name = folder.Name;
                        reply.FolderForType.ParentID = folder.ParentID;
                        reply.FolderForType.PreferredContentType = CableBeachUtils.SLAssetTypeToContentType(folder.Type);
                        reply.FolderForType.Version = folder.Version;
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH INVENTORY]: get_folder_for_type did not return a folder for user " +
                            ownerID + ", content type " + message.ContentType + ". Trying to retrieve root folder");

                        folder = m_InventoryService.GetRootFolder(ownerID);

                        if (folder != null && folder.ID != UUID.Zero)
                        {
                            reply.FolderForType.FolderID = folder.ID;
                            reply.FolderForType.Name = folder.Name;
                            reply.FolderForType.ParentID = folder.ParentID;
                            reply.FolderForType.PreferredContentType = CableBeachUtils.SLAssetTypeToContentType(folder.Type);
                            reply.FolderForType.Version = folder.Version;
                        }
                        else
                        {
                            m_log.Warn("[CABLE BEACH INVENTORY]: get_folder_for_type failed to fetch root folder for user " +
                                ownerID + ", returning an empty response");
                        }
                    }

                    m_log.Debug("[CABLE BEACH INVENTORY]: Returning folder " + reply.FolderForType.FolderID + " for type " +
                        message.ContentType);

                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: get_folder_for_type called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        public class CBGetActiveGesturesHandler : BaseStreamHandler
        {
            private IInventoryService m_InventoryService;

            public CBGetActiveGesturesHandler(IInventoryService service) :
                base(String.Empty, String.Empty)
            {
                m_InventoryService = service;
            }

            public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
            {
                OSDMap map;
                if (ServiceHelper.TryGetOSD(httpRequest, out map))
                {
                    GetActiveGesturesMessage message = new GetActiveGesturesMessage();
                    message.Deserialize(map);

                    GetActiveGesturesReplyMessage reply = new GetActiveGesturesReplyMessage();

                    UUID ownerID = CableBeachUtils.MessageToUUID(message.Identity, message.AgentID);

                    List<InventoryItemBase> gestures = m_InventoryService.GetActiveGestures(ownerID);

                    if (gestures != null)
                    {
                        reply.Gestures = new GetActiveGesturesReplyMessage.Gesture[gestures.Count];

                        for (int i = 0; i < gestures.Count; i++)
                        {
                            InventoryItemBase gestureItem = gestures[i];

                            GetActiveGesturesReplyMessage.Gesture gesture = new GetActiveGesturesReplyMessage.Gesture();
                            gesture.AssetID = gestureItem.AssetID;
                            gesture.ItemID = gestureItem.ID;
                            reply.Gestures[i] = gesture;
                        }
                    }
                    else
                    {
                        reply.Gestures = new GetActiveGesturesReplyMessage.Gesture[0];
                    }

                    m_log.Debug("[CABLE BEACH INVENTORY]: get_active_gestures responding with " +
                        reply.Gestures.Length + " active gestures for " + ownerID);
                    return ServiceHelper.MakeResponse(httpResponse, reply.Serialize());
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: get_active_gestures called with invalid data");
                    httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Utils.EmptyBytes;
                }
            }
        }

        #endregion Inventory Endpoint Handlers

        #region Legacy Handlers

        bool LegacyCreateUsersInventoryHandler(Guid rawUserID)
        {
            UUID ownerID = new UUID(rawUserID);
            m_log.Info("[CABLE BEACH INVENTORY]: (Legacy) Creating new set of inventory folders for user " + ownerID);

            if (!m_InventoryService.HasInventoryForUser(ownerID))
            {
                if (m_InventoryService.CreateUserInventory(ownerID))
                {
                    m_log.Info("[CABLE BEACH INVENTORY]: create_inventory succeeded for user " + ownerID);
                    return true;
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: create_inventory failed for user " + ownerID);
                    return false;
                }
            }
            else
            {
                m_log.Warn("[CABLE BEACH INVENTORY]: create_inventory called for user " + ownerID + " who already has an inventory");
                return false;
            }
        }

        List<InventoryFolderBase> LegacyRootFoldersHandler(Guid rawUserID)
        {
            UUID ownerID = new UUID(rawUserID);
            m_log.Info("[CABLE BEACH INVENTORY]: (Legacy) Fetching inventory skeleton for user " + ownerID);

            return m_InventoryService.GetInventorySkeleton(ownerID);
        }

        List<InventoryItemBase> LegacyActiveGesturesHandler(Guid rawUserID)
        {
            UUID ownerID = new UUID(rawUserID);
            m_log.Info("[CABLE BEACH INVENTORY]: (Legacy) Fetching active gestures for user " + ownerID);

            return m_InventoryService.GetActiveGestures(ownerID);
        }

        #endregion Legacy Handlers
    }
}
