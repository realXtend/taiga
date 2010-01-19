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
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using log4net;
using Mono.Addins;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Server.Base;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenMetaverse.Http;
using CableBeachMessages;

namespace ModCableBeach
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class InventoryServiceConnector : INonSharedRegionModule, IInventoryService
    {
        const int REQUEST_TIMEOUT = 1000 * 30;

        private static readonly Uri FILESYSTEM_SERVICE_URI = new Uri(CableBeachServices.FILESYSTEM);
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;
        private bool m_Initialized = false;
        private WorldServiceConnector m_WorldServiceConnector;
        private Uri m_DefaultFilesystemService;

        public Type ReplaceableInterface { get { return null; } }

        public string Name
        {
            get { return "CableBeachInventoryServiceConnector"; }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig assetConfig = source.Configs["Network"];
            if (assetConfig == null)
            {
                m_log.Error("[CABLE BEACH INVENTORY]: [Network] section missing from configuration");
                return;
            }

            if (!Uri.TryCreate(assetConfig.GetString("inventory_server_url"), UriKind.Absolute, out m_DefaultFilesystemService))
            {
                m_log.Error("[CABLE BEACH INVENTORY]: inventory_server_url missing from [Network] configuration section");
                return;
            }

            WorldServiceConnector.OnWorldServiceConnectorLoaded += WorldServiceConnectorLoadedHandler;

            m_log.Info("[CABLE BEACH INVENTORY]: Cable Beach inventory connector initializing...");
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            // TODO: This can be removed when the old services cruft is cleaned out of OpenSim
            if (!m_Initialized)
            {
                m_Initialized = true;
                scene.CommsManager.UserProfileCacheService.SetInventoryService(this);
                scene.CommsManager.UserService.SetInventoryService(this);
            }

            scene.RegisterModuleInterface<IInventoryService>(this);
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;
        }

        /// <summary>
        /// Event handler that is fired when the world service connector is loaded
        /// </summary>
        /// <param name="instance">Reference to the world service connector</param>
        private void WorldServiceConnectorLoadedHandler(WorldServiceConnector instance)
        {
            m_WorldServiceConnector = instance;
            m_Enabled = true;
            m_log.Info("[CABLE BEACH INVENTORY]: Cable Beach inventory connector initialized");
        }

        #region IInventoryService Implementation

        public bool CreateUserInventory(UUID user)
        {
            m_log.Info("[CABLE BEACH INVENTORY]: Ignoring CreateUserInventory() call for " + user);
            return false;
        }

        public List<InventoryFolderBase> GetInventorySkeleton(UUID userID)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getSkeletonUri = GetServiceCapability(userID,CableBeachServices.FILESYSTEM_GET_FILESYSTEM_SKELETON);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetInventorySkeleton(), Capability=" + getSkeletonUri);

            if (getSkeletonUri != null)
            {
                GetInventorySkeletonMessage get = new GetInventorySkeletonMessage();
                get.Identity = identity;
                get.AgentID = userID;

                CapsClient request = new CapsClient(getSkeletonUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetInventorySkeletonReplyMessage reply = new GetInventorySkeletonReplyMessage();
                    reply.Deserialize(response);

                    List<InventoryFolderBase> folders = new List<InventoryFolderBase>(reply.Folders.Length);

                    for (int i = 0; i < reply.Folders.Length; i++)
                    {
                        GetInventorySkeletonReplyMessage.Folder folderObj = reply.Folders[i];
                        InventoryFolderBase folder = new InventoryFolderBase();

                        folder.ID = folderObj.FolderID;
                        folder.Name = folderObj.Name;
                        folder.Owner = userID;
                        folder.ParentID = folderObj.ParentID;
                        folder.Type = (short)CableBeachUtils.ContentTypeToSLAssetType(folderObj.PreferredContentType);
                        folder.Version = (ushort)folderObj.Version;

                        folders.Add(folder);
                    }

                    return folders;
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve skeleton for " + userID + " from " + getSkeletonUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_filesystem_skeleton capability for " + userID);
            }

            return null;
        }

        public InventoryCollection GetUserInventory(UUID userID)
        {
            m_log.Error("[CABLE BEACH INVENTORY]: Legacy function GetUserInventory(UUID userID) called");

            InventoryCollection collection = new InventoryCollection();
            collection.UserID = userID;
            collection.Items = new List<InventoryItemBase>();
            collection.Folders = new List<InventoryFolderBase>();
            return collection;
        }

        public void GetUserInventory(UUID userID, InventoryReceiptCallback callback)
        {
            m_log.Error("[CABLE BEACH INVENTORY]: Legacy function GetUserInventory(UUID userID, InventoryReceiptCallback callback) called");

            callback(new List<InventoryFolderImpl>(), new List<InventoryItemBase>());
        }

        public InventoryFolderBase GetRootFolder(UUID userID)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getRootUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_GET_ROOT_FOLDER);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetRootFolder(), Capability=" + getRootUri);

            if (getRootUri != null)
            {
                GetRootFolderMessage get = new GetRootFolderMessage();
                get.Identity = identity;
                get.AgentID = userID;

                CapsClient request = new CapsClient(getRootUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetRootFolderReplyMessage reply = new GetRootFolderReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Success)
                    {
                        InventoryFolderBase root = new InventoryFolderBase(reply.RootFolder.ID, reply.RootFolder.Name, reply.RootFolder.OwnerID,
                            CableBeachUtils.ContentTypeToSLAssetType(reply.RootFolder.PreferredContentType), reply.RootFolder.ParentID,
                            (ushort)reply.RootFolder.Version);
                        return root;
                    }
                    else
                    {
                        m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve root folder for " + userID + " from " + getRootUri + ": " + reply.Message);
                    }
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve root folder for " + userID + " from " + getRootUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_root_folder capability for " + userID);
            }

            return null;
        }

        public InventoryFolderBase GetFolderForType(UUID userID, OpenMetaverse.AssetType type)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getFolderForTypeUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_GET_FOLDER_FOR_TYPE);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetFolderForType(type=" + type + "), Capability=" + getFolderForTypeUri);

            if (getFolderForTypeUri != null)
            {
                GetFolderForTypeMessage get = new GetFolderForTypeMessage();
                get.Identity = identity;
                get.AgentID = userID;
                get.ContentType = CableBeachUtils.SLAssetTypeToContentType((int)type);

                CapsClient request = new CapsClient(getFolderForTypeUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetFolderForTypeReplyMessage reply = new GetFolderForTypeReplyMessage();
                    reply.Deserialize(response);

                    if (reply.FolderForType.FolderID != UUID.Zero)
                    {
                        InventoryFolderBase folder = new InventoryFolderBase(
                            reply.FolderForType.FolderID,
                            reply.FolderForType.Name,
                            userID,
                            CableBeachUtils.ContentTypeToSLAssetType(reply.FolderForType.PreferredContentType),
                            reply.FolderForType.ParentID,
                            (ushort)reply.FolderForType.Version);
                        return folder;
                    }
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve folder type (" + type + ") for " + userID +
                        " from " + getFolderForTypeUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_folder_for_type capability for " + userID);
            }

            return null;
        }

        public InventoryCollection GetFolderContent(UUID userID, UUID folderID)
        {
            InventoryCollection inventory = new InventoryCollection();
            inventory.UserID = userID;
            inventory.Folders = new List<InventoryFolderBase>();
            inventory.Items = new List<InventoryItemBase>();

            if (m_WorldServiceConnector == null)
                return inventory;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getFolderContentsUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_GET_FOLDER_CONTENTS);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetFolderContent(folderID=" + folderID + "), Capability=" + getFolderContentsUri);

            if (getFolderContentsUri != null)
            {
                GetFolderContentsMessage get = new GetFolderContentsMessage();
                get.Identity = identity;
                get.AgentID = userID;
                get.FolderID = folderID;

                CapsClient request = new CapsClient(getFolderContentsUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetFolderContentsReplyMessage reply = new GetFolderContentsReplyMessage();
                    reply.Deserialize(response);

                    List<InventoryFolderBase> folders = new List<InventoryFolderBase>();
                    List<InventoryItemBase> items = new List<InventoryItemBase>();

                    // Convert the incoming array of inventory objects to separate collections of folders and items
                    for (int i = 0; i < reply.Objects.Length; i++)
                    {
                        InventoryBlock obj = reply.Objects[i];

                        if (obj is InventoryBlockFolder)
                        {
                            InventoryBlockFolder folderObj = (InventoryBlockFolder)obj;
                            InventoryFolderImpl folder = BlockToFolder(folderObj);
                            folders.Add(folder);
                        }
                        else
                        {
                            InventoryBlockItem itemObj = (InventoryBlockItem)obj;
                            InventoryItemBase item = BlockToItem(itemObj);
                            items.Add(item);
                        }
                    }

                    m_log.Debug("[CABLE BEACH INVENTORY]: Fetched folder contents: " + folders.Count + " folders and " +
                        items.Count + " items for " + userID);

                    inventory.Folders = folders;
                    inventory.Items = items;
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve folder contents for " + userID + " from " + getFolderContentsUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_folder_contents capability for " + userID);
            }

            return inventory;
        }

        public List<InventoryItemBase> GetFolderItems(UUID userID, UUID folderID)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getObjectUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_GET_OBJECT);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetFolderItems(folderID=" + folderID + "), Capability=" + getObjectUri);

            if (getObjectUri != null)
            {
                GetObjectMessage get = new GetObjectMessage();
                get.Identity = identity;
                get.AgentID = userID;
                get.ObjectID = folderID;

                CapsClient request = new CapsClient(getObjectUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetObjectReplyMessage reply = new GetObjectReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Object is InventoryBlockFolder)
                    {
                        InventoryBlockFolder folderObj = (InventoryBlockFolder)reply.Object;
                        InventoryFolderImpl folder = BlockToFolder(folderObj);

                        return new List<InventoryItemBase>(folder.Items.Values);
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH INVENTORY]: Folder " + folderID + " for " + userID + " was not found at " + getObjectUri);
                    }
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve folder for " + userID + " from " + getObjectUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_object capability for " + userID);
            }

            return null;
        }

        public bool AddFolder(InventoryFolderBase folder)
        {
            return AddObject(folder.Owner, FolderToBlock(folder));
        }

        public bool UpdateFolder(InventoryFolderBase folder)
        {
            return AddObject(folder.Owner, FolderToBlock(folder));
        }

        public bool MoveFolder(InventoryFolderBase folder)
        {
            return AddObject(folder.Owner, FolderToBlock(folder));
        }

        public bool AddItem(InventoryItemBase item)
        {
            return AddObject(item.Owner, ItemToBlock(item));
        }

        public bool UpdateItem(InventoryItemBase item)
        {
            return AddObject(item.Owner, ItemToBlock(item));
        }

        public bool PurgeFolder(InventoryFolderBase folder)
        {
            if (m_WorldServiceConnector == null)
                return false;

            Uri identity = m_WorldServiceConnector.GetIdentity(folder.Owner);
            Uri purgeFolderUri = GetServiceCapability(folder.Owner, CableBeachServices.FILESYSTEM_PURGE_FOLDER);

            m_log.Debug("[CABLE BEACH INVENTORY]: PurgeFolder(folder.ID=" + folder.ID + "), Capability=" + purgeFolderUri);

            if (purgeFolderUri != null)
            {
                PurgeFolderMessage purge = new PurgeFolderMessage();
                purge.Identity = identity;
                purge.AgentID = folder.Owner;
                purge.FolderID = folder.ID;

                CapsClient request = new CapsClient(purgeFolderUri);
                OSDMap response = request.GetResponse(purge.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    PurgeFolderReplyMessage reply = new PurgeFolderReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Success)
                        return true;
                    else
                        m_log.Error("[CABLE BEACH INVENTORY]: Received a failure response when purging folder " +
                            folder.ID + " for " + folder.Owner + " from " + purgeFolderUri + " (" + reply.Message + ")");
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to purge folder for " + folder.Owner + " from " + purgeFolderUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a purge_folder capability for " + folder.Owner);
            }

            return false;
        }

        public bool DeleteItem(InventoryItemBase item)
        {
            if (m_WorldServiceConnector == null)
                return false;

            Uri identity = m_WorldServiceConnector.GetIdentity(item.Owner);
            Uri deleteObjectUri = GetServiceCapability(item.Owner, CableBeachServices.FILESYSTEM_DELETE_OBJECT);

            m_log.Debug("[CABLE BEACH INVENTORY]: DeleteItem(item.ID=" + item.ID + "), Capability=" + deleteObjectUri);

            if (deleteObjectUri != null)
            {
                DeleteObjectMessage delete = new DeleteObjectMessage();
                delete.Identity = identity;
                delete.AgentID = item.Owner;
                delete.ObjectID = item.ID;

                CapsClient request = new CapsClient(deleteObjectUri);
                OSDMap response = request.GetResponse(delete.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    DeleteObjectReplyMessage reply = new DeleteObjectReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Success)
                        return true;
                    else
                        m_log.Error("[CABLE BEACH INVENTORY]: Received a failure response when deleting item " +
                            item.ID + " for " + item.Owner + " from " + deleteObjectUri + " (" + reply.Message + ")");
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to delete item for " + item.Owner + " from " + deleteObjectUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a delete_object capability for " + item.Owner);
            }

            return false;
        }

        public InventoryItemBase GetItem(InventoryItemBase item)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(item.Owner);
            Uri getObjectUri = GetServiceCapability(item.Owner, CableBeachServices.FILESYSTEM_GET_OBJECT);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetItem(item.ID=" + item.ID + "), Capability=" + getObjectUri);

            if (getObjectUri != null)
            {
                GetObjectMessage get = new GetObjectMessage();
                get.Identity = identity;
                get.AgentID = item.Owner;
                get.ObjectID = item.ID;

                CapsClient request = new CapsClient(getObjectUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetObjectReplyMessage reply = new GetObjectReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Object is InventoryBlockItem)
                    {
                        InventoryBlockItem itemObj = (InventoryBlockItem)reply.Object;
                        return BlockToItem(itemObj);
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH INVENTORY]: Item " + item.ID + " for " + item.Owner +
                            " was not found at " + getObjectUri);
                    }
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve item for " + item.Owner + " from " + getObjectUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_object capability for " + item.Owner);
            }

            return null;
        }

        public InventoryFolderBase GetFolder(InventoryFolderBase folder)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(folder.Owner);
            Uri getObjectUri = GetServiceCapability(folder.Owner, CableBeachServices.FILESYSTEM_GET_OBJECT);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetFolder(folder.ID=" + folder.ID + "), Capability=" + getObjectUri);

            if (getObjectUri != null)
            {
                GetObjectMessage get = new GetObjectMessage();
                get.Identity = identity;
                get.AgentID = folder.Owner;
                get.ObjectID = folder.ID;

                CapsClient request = new CapsClient(getObjectUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetObjectReplyMessage reply = new GetObjectReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Object is InventoryBlockFolder)
                    {
                        InventoryBlockFolder folderObj = (InventoryBlockFolder)reply.Object;
                        return BlockToFolder(folderObj);
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH INVENTORY]: Folder " + folder.ID + " for " + folder.Owner +
                            " was not found at " + getObjectUri);
                    }
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve folder for " + folder.Owner + " from " + getObjectUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_object capability for " + folder.Owner);
            }

            return null;
        }

        public bool HasInventoryForUser(UUID userID)
        {
            InventoryFolderBase rootFolder = RequestRootFolder(userID);
            return rootFolder != null && rootFolder.ID != UUID.Zero;
        }

        public InventoryFolderBase RequestRootFolder(UUID userID)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getRootFolderUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_GET_ROOT_FOLDER);

            m_log.Debug("[CABLE BEACH INVENTORY]: RequestRootFolder(), Capability=" + getRootFolderUri);

            if (getRootFolderUri != null)
            {
                GetRootFolderMessage get = new GetRootFolderMessage();
                get.Identity = identity;
                get.AgentID = userID;

                CapsClient request = new CapsClient(getRootFolderUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetRootFolderReplyMessage reply = new GetRootFolderReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Success)
                        return BlockToFolder(reply.RootFolder);
                    else
                        m_log.Warn("[CABLE BEACH INVENTORY]: No root folder for " + userID + " was found at " +
                            getRootFolderUri + " (" + reply.Message + ")");
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve item for " + userID + " from " + getRootFolderUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_root_folder capability for " + userID);
            }

            return null;
        }

        public List<InventoryItemBase> GetActiveGestures(UUID userID)
        {
            if (m_WorldServiceConnector == null)
                return null;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri getActiveGesturesUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_GET_ACTIVE_GESTURES);

            m_log.Debug("[CABLE BEACH INVENTORY]: GetActiveGestures(), Capability=" + getActiveGesturesUri);

            if (getActiveGesturesUri != null)
            {
                GetActiveGesturesMessage get = new GetActiveGesturesMessage();
                get.Identity = identity;
                get.AgentID = userID;

                CapsClient request = new CapsClient(getActiveGesturesUri);
                OSDMap response = request.GetResponse(get.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    GetActiveGesturesReplyMessage reply = new GetActiveGesturesReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Gestures != null)
                    {
                        List<InventoryItemBase> gestures = new List<InventoryItemBase>(reply.Gestures.Length);

                        // TODO: Holy inefficiency Batman!
                        for (int i = 0; i < reply.Gestures.Length; i++)
                        {
                            InventoryItemBase gesture = new InventoryItemBase();
                            gesture.ID = reply.Gestures[i].ItemID;
                            gesture.Owner = userID;
                            gesture = GetItem(gesture);

                            if (gesture != null)
                                gestures.Add(gesture);
                        }

                        m_log.Debug("[CABLE BEACH INVENTORY]: Fetched " + gestures.Count + " active gestures for " +
                            userID + " from " + getActiveGesturesUri);
                        return gestures;
                    }
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to retrieve item for " + userID + " from " + getActiveGesturesUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a get_active_gestures capability for " + userID);
            }

            return new List<InventoryItemBase>(0);
        }

        public int GetAssetPermissions(UUID userID, UUID assetID)
        {
            return 2147483647;
        }

        public bool MoveItems(UUID ownerID, List<InventoryItemBase> items)
        {
            // TODO: This needs to be optimized into a single call
            bool success = false;

            for (int i = 0; i < items.Count; i++)
                success = UpdateItem(items[i]);

            return success;
        }

        public bool DeleteItems(UUID userID, List<UUID> itemIDs)
        {
            // TODO: This needs to be optimized into a single call
            bool success = false;

            for (int i = 0; i < itemIDs.Count; i++)
                success = DeleteItem(new InventoryItemBase(itemIDs[i], userID));

            return success;
        }

        public bool DeleteFolders(UUID userID, List<UUID> folderIDs)
        {
            return DeleteItems(userID, folderIDs);
        }

        #endregion IInventoryService Implementation

        bool AddObject(UUID userID, InventoryBlock obj)
        {
            if (m_WorldServiceConnector == null)
                return false;

            Uri identity = m_WorldServiceConnector.GetIdentity(userID);
            Uri createObjectUri = GetServiceCapability(userID, CableBeachServices.FILESYSTEM_CREATE_OBJECT);

            m_log.Debug("[CABLE BEACH INVENTORY]: AddObject(obj.ID=" + obj.ID + ",obj.Name=" + obj.Name +
                ",obj.ParentID=" + obj.ParentID + "), Capability=" + createObjectUri);

            if (createObjectUri != null)
            {
                CreateObjectMessage create = new CreateObjectMessage();
                create.Identity = identity;
                create.AgentID = userID;
                create.Object = obj;

                CapsClient request = new CapsClient(createObjectUri);
                OSDMap response = request.GetResponse(create.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (response != null)
                {
                    CreateObjectReplyMessage reply = new CreateObjectReplyMessage();
                    reply.Deserialize(response);

                    if (reply.Success)
                        return true;
                    else
                        m_log.Error("[CABLE BEACH INVENTORY]: create_object at " + createObjectUri + " returned a failure for " +
                            obj.ID + " (" + reply.Message + ")");
                }
                else
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Failed to create object for " + userID + " at " + createObjectUri);
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH INVENTORY]: Could not find a create_object capability for " + userID);
            }

            return false;
        }

        Uri GetServiceCapability(UUID userID, string capIdentifier)
        {
            Uri capability = m_WorldServiceConnector.GetServiceCapability(userID, FILESYSTEM_SERVICE_URI, new Uri(capIdentifier));

            if (capability == null)
            {
                if (!ModCableBeach.TryGetDefaultCap(m_DefaultFilesystemService, capIdentifier, out capability))
                {
                    m_log.Error("[CABLE BEACH INVENTORY]: Could not get a default capability for " + capIdentifier +
                        ", using inventory server at " + m_DefaultFilesystemService);
                }
            }

            return capability;
        }

        #region Conversion Functions

        InventoryFolderImpl BlockToFolder(InventoryBlockFolder folderObj)
        {
            InventoryFolderImpl folder = new InventoryFolderImpl();
            folder.ID = folderObj.ID;
            folder.Name = folderObj.Name;
            folder.Owner = folderObj.OwnerID;
            folder.ParentID = folderObj.ParentID;
            folder.Type = (short)CableBeachUtils.ContentTypeToSLAssetType(folderObj.PreferredContentType);
            folder.Version = (ushort)folderObj.Version;

            // Parse the children items and folders
            for (int i = 0; i < folderObj.Children.Length; i++)
            {
                InventoryBlock block = folderObj.Children[i];

                if (block is InventoryBlockFolder)
                {
                    InventoryBlockFolder childFolderObj = (InventoryBlockFolder)block;
                    InventoryFolderImpl childFolder = BlockToFolder(childFolderObj);
                    folder.AddChildFolder(childFolder);
                }
                else
                {
                    InventoryBlockItem childItemObj = (InventoryBlockItem)block;
                    InventoryItemBase childItem = BlockToItem(childItemObj);
                    folder.Items[childItem.ID] = childItem;
                }
            }

            return folder;
        }

        InventoryItemBase BlockToItem(InventoryBlockItem itemObj)
        {
            InventoryItemBase item = new InventoryItemBase();
            item.AssetID = itemObj.AssetID;
            item.AssetType = (int)CableBeachUtils.ContentTypeToSLAssetType(itemObj.ContentType);
            item.BasePermissions = itemObj.PermsBase;
            item.CreationDate = (int)Utils.DateTimeToUnixTime(itemObj.CreationDate);
            item.CreatorId = itemObj.CreatorID.ToString();
            item.CreatorIdAsUuid = itemObj.CreatorID;
            item.CurrentPermissions = itemObj.PermsOwner;
            item.Description = itemObj.Description;
            item.EveryOnePermissions = itemObj.PermsEveryone;
            item.Flags = itemObj.Flags;
            item.Folder = itemObj.ParentID;
            item.GroupID = itemObj.GroupID;
            item.GroupOwned = itemObj.GroupOwned;
            item.GroupPermissions = itemObj.PermsGroup;
            item.ID = itemObj.ID;
            item.InvType = (int)CableBeachUtils.ContentTypeToSLInvType(itemObj.ContentType);
            item.Name = itemObj.Name;
            item.NextPermissions = itemObj.PermsNext;
            item.Owner = itemObj.OwnerID;
            item.SalePrice = itemObj.SalePrice;
            item.SaleType = (byte)itemObj.SaleType;

            return item;
        }

        InventoryBlockFolder FolderToBlock(InventoryFolderBase folder)
        {
            InventoryBlockFolder folderObj = new InventoryBlockFolder();
            folderObj.ID = folder.ID;
            folderObj.Name = folder.Name;
            folderObj.OwnerID = folder.Owner;
            folderObj.ParentID = folder.ParentID;
            folderObj.PreferredContentType = CableBeachUtils.SLAssetTypeToContentType(folder.Type);
            folderObj.Version = folder.Version;
            folderObj.Children = new InventoryBlock[0];

            return folderObj;
        }

        InventoryBlockFolder FolderToBlock(InventoryFolderImpl folder)
        {
            InventoryBlockFolder folderObj = new InventoryBlockFolder();
            folderObj.ID = folder.ID;
            folderObj.Name = folder.Name;
            folderObj.OwnerID = folder.Owner;
            folderObj.ParentID = folder.ParentID;
            folderObj.PreferredContentType = CableBeachUtils.SLAssetTypeToContentType(folder.Type);
            folderObj.Version = folder.Version;

            List<InventoryFolderBase> childFolders = folder.RequestListOfFolders();
            List<InventoryItemBase> childItems = folder.RequestListOfItems();

            folderObj.Children = new InventoryBlock[childFolders.Count + childItems.Count];
            int i = 0;

            foreach (InventoryFolderBase childFolder in childFolders)
                folderObj.Children[i++] = FolderToBlock(childFolder);

            foreach (InventoryItemBase childItem in childItems)
                folderObj.Children[i++] = ItemToBlock(childItem);

            return folderObj;
        }

        InventoryBlockItem ItemToBlock(InventoryItemBase item)
        {
            InventoryBlockItem itemObj = new InventoryBlockItem();
            itemObj.AssetID = item.AssetID;
            itemObj.ContentType = CableBeachUtils.SLAssetTypeToContentType(item.AssetType);
            itemObj.CreationDate = Utils.UnixTimeToDateTime(item.CreationDate);
            itemObj.CreatorID = item.CreatorIdAsUuid;
            itemObj.Description = item.Description;
            itemObj.Flags = item.Flags;
            itemObj.GroupID = item.GroupID;
            itemObj.GroupOwned = item.GroupOwned;
            itemObj.ID = item.ID;
            itemObj.Name = item.Name;
            itemObj.OwnerID = item.Owner;
            itemObj.ParentID = item.Folder;
            itemObj.PermsBase = item.BasePermissions;
            itemObj.PermsEveryone = item.EveryOnePermissions;
            itemObj.PermsGroup = item.GroupPermissions;
            itemObj.PermsNext = item.NextPermissions;
            itemObj.PermsOwner = item.CurrentPermissions;
            itemObj.SalePrice = item.SalePrice;
            itemObj.SaleType = item.SaleType;

            return itemObj;
        }

        #endregion Conversion Functions
    }
}
