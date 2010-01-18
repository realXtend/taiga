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
using System.Reflection;
using System.Text;
using log4net;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using CableBeachMessages;

namespace ModCableBeach
{
    public static class ServiceHelper
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        public static bool TryGetOSD(OSHttpRequest httpRequest, out OSDMap map)
        {
            try
            {
                map = OSDParser.DeserializeJson(httpRequest.InputStream) as OSDMap;
                if (map != null)
                    return true;
                else
                    m_log.Warn("[CABLE BEACH]: No valid JSON/OSD data found in stream");
            }
            catch (Exception ex)
            {
                m_log.Warn("[CABLE BEACH]: Failed parsing JSON/OSD data from stream: " + ex.Message);
            }

            map = null;
            return false;
        }

        public static byte[] MakeResponse(OSHttpResponse httpResponse, OSDMap map)
        {
            byte[] responseData = Encoding.UTF8.GetBytes(OSDParser.SerializeJsonString(map));

            httpResponse.ContentType = "application/json";
            httpResponse.ContentLength = responseData.Length;
            httpResponse.ContentEncoding = Encoding.UTF8;

            return responseData;
        }

        public static UUID GetFolderForType(IInventoryService inventoryService, UUID ownerID, string contentType)
        {
            InventoryFolderBase folder = inventoryService.GetFolderForType(ownerID, (OpenMetaverse.AssetType)CableBeachUtils.ContentTypeToSLAssetType(contentType));

            if (folder != null)
            {
                if (folder.ID == UUID.Zero)
                    m_log.Error("[CABLE BEACH]: GetFolderForType( " + contentType + ") returned a folder with UUID.Zero");

                return folder.ID;
            }
            else
            {
                m_log.Error("[CABLE BEACH]: GetFolderForType( " + contentType + ") did not return any folder");
                return UUID.Zero;
            }
        }

        public static InventoryBlock InventoryToMessage(InventoryNodeBase obj)
        {
            if (obj is InventoryItemBase)
            {
                // Inventory item
                InventoryItemBase item = (InventoryItemBase)obj;
                InventoryBlockItem itemObj = new InventoryBlockItem();

                itemObj.AssetID = item.AssetID;
                itemObj.ContentType = CableBeachUtils.SLAssetTypeToContentType(item.AssetType);
                itemObj.CreationDate = OpenMetaverse.Utils.UnixTimeToDateTime(item.CreationDate);
                UUID.TryParse(item.CreatorId, out itemObj.CreatorID);
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
            else if (obj is InventoryFolderImpl)
            {
                // Inventory folder that potentially contains child objects
                InventoryFolderImpl folder = (InventoryFolderImpl)obj;
                InventoryBlockFolder folderObj = new InventoryBlockFolder();

                folderObj.ID = folder.ID;
                folderObj.Name = folder.Name;
                folderObj.OwnerID = folder.Owner;
                folderObj.ParentID = folder.ParentID;
                folderObj.PreferredContentType = CableBeachUtils.SLAssetTypeToContentType(folder.Type);
                folderObj.Version = folder.Version;

                if (folder.Items != null)
                {
                    folderObj.Children = new InventoryBlock[folder.Items.Count];
                    int i = 0;
                    foreach (InventoryNodeBase child in folder.Items.Values)
                        folderObj.Children[i++] = InventoryToMessage(child);
                }
                else
                {
                    folderObj.Children = new InventoryBlock[0];
                }

                return folderObj;
            }
            else if (obj is InventoryFolderBase)
            {
                // Basic inventory folder object, no children
                InventoryFolderBase folder = (InventoryFolderBase)obj;
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
            else
            {
                throw new ArgumentException("Unrecognized inventory node type " + obj.GetType().FullName);
            }
        }
    }
}
