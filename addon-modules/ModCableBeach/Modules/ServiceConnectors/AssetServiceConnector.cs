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
    public class AssetServiceConnector : INonSharedRegionModule, IAssetService
    {
        const int REQUEST_TIMEOUT = 1000 * 30;

        private static readonly Uri ASSET_SERVICE_URI = new Uri(CableBeachServices.ASSETS);
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private bool m_Enabled = false;
        private WorldServiceConnector m_WorldServiceConnector;
        private IImprovedAssetCache m_Cache;
        private Uri m_DefaultAssetService;

        public Type ReplaceableInterface { get { return null; } }

        public string Name
        {
            get { return "CableBeachAssetServiceConnector"; }
        }

        public void Initialise(IConfigSource source)
        {
            IConfig assetConfig = source.Configs["Network"];
            if (assetConfig == null)
            {
                m_log.Error("[CABLE BEACH ASSETS]: [Network] section missing from configuration");
                return;
            }

            if (!Uri.TryCreate(assetConfig.GetString("asset_server_url"), UriKind.Absolute, out m_DefaultAssetService))
            {
                m_log.Error("[CABLE BEACH ASSETS]: asset_server_url missing from [Network] configuration section");
                return;
            }

            WorldServiceConnector.OnWorldServiceConnectorLoaded += WorldServiceConnectorLoadedHandler;

            m_log.Info("[CABLE BEACH ASSETS]: Cable Beach asset connector initializing...");
        }

        public void Close()
        {
        }

        public void AddRegion(Scene scene)
        {
            scene.RegisterModuleInterface<IAssetService>(this);
        }

        public void RemoveRegion(Scene scene)
        {
        }

        public void RegionLoaded(Scene scene)
        {
            if (!m_Enabled)
                return;

            m_Cache = scene.RequestModuleInterface<IImprovedAssetCache>();

            if (m_Cache == null)
            {
                m_log.Error("[CABLE BEACH ASSETS]: Failed to locate an IImprovedAssetCache. " +
                    "Storage for local and temporary assets will be disabled");
            }
        }

        /// <summary>
        /// Event handler that is fired when the world service connector is loaded
        /// </summary>
        /// <param name="instance">Reference to the world service connector</param>
        private void WorldServiceConnectorLoadedHandler(WorldServiceConnector instance)
        {
            m_WorldServiceConnector = instance;
            m_Enabled = true;
            m_log.Info("[CABLE BEACH ASSETS]: Cable Beach asset connector initialized");
        }

        public AssetBase Get(string id)
        {
            AssetBase asset = null;

            // Cache check
            if (m_Cache != null)
            {
                asset = m_Cache.Get(id);
                if (asset != null)
                    return asset;
            }

            // Remote metadata fetch
            AssetMetadata metadata = GetMetadata(id);

            if (metadata != null)
            {
                // Remote data fetch
                byte[] assetData = GetData(id);

                if (assetData != null)
                {
                    asset = new AssetBase(metadata.FullID, metadata.Name, metadata.Type);
                    asset.Metadata = metadata;
                    asset.Data = assetData;

                    // Store this asset in the local cache
                    if (m_Cache != null)
                        m_Cache.Cache(asset);
                }
            }

            return asset;
        }

        public AssetBase GetCached(string id)
        {
	  if (m_Cache != null)
	    return m_Cache.Get(id);

	  return null;
        }

        public AssetMetadata GetMetadata(string id)
        {
            // Cache check
            if (m_Cache != null)
            {
                AssetBase asset = m_Cache.Get(id);
                if (asset != null)
                    return asset.Metadata;
            }

            AssetMetadata metadata = null;
            Uri assetMetadataUri = GetAssetUri(id, "metadata");

            // Remote metadata request
            CapsClient request = new CapsClient(assetMetadataUri);
            OSDMap responseMap = request.GetResponse(REQUEST_TIMEOUT) as OSDMap;

            if (responseMap != null)
            {
                // Parse the response
                GetAssetMetadataMessage message = new GetAssetMetadataMessage();
                message.Deserialize(responseMap);

                metadata = new AssetMetadata();
                metadata.ContentType = message.Metadata.ContentType;
                metadata.CreationDate = message.Metadata.CreationDate;
                metadata.Description = message.Metadata.Description;
                metadata.FullID = message.Metadata.ID;
                metadata.ID = id;
                metadata.Local = false;
                metadata.Name = message.Metadata.Name;
                metadata.SHA1 = Utils.EmptyBytes; //message.Metadata.SHA256; // FIXME: :-(
                metadata.Temporary = message.Metadata.Temporary;
                metadata.Type = (sbyte)CableBeachUtils.ContentTypeToSLAssetType(message.Metadata.ContentType);
            }
            else
            {
                m_log.Debug("[CABLE BEACH ASSETS]: Failed to fetch asset metadata from " + assetMetadataUri);
            }

            return metadata;
        }

        public byte[] GetData(string id)
        {
            // Cache check
            if (m_Cache != null)
            {
                AssetBase asset = m_Cache.Get(id);
                if (asset != null)
                    return asset.Data;
            }

            byte[] assetData = null;
            Uri assetDataUri = GetAssetUri(id, "data");

            // Remote data request
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(assetDataUri);

            try
            {
                // Parse the response
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    using (Stream responseStream = response.GetResponseStream())
                    {
                        #region Read the response

                        // If Content-Length is set we create a buffer of the exact size, otherwise
                        // a MemoryStream is used to receive the response
                        bool nolength = (response.ContentLength <= 0);
                        int size = (nolength) ? 8192 : (int)response.ContentLength;
                        MemoryStream ms = (nolength) ? new MemoryStream() : null;
                        byte[] buffer = new byte[size];

                        int bytesRead = 0;
                        int offset = 0;

                        while ((bytesRead = responseStream.Read(buffer, offset, size)) != 0)
                        {
                            if (nolength)
                            {
                                ms.Write(buffer, 0, bytesRead);
                            }
                            else
                            {
                                offset += bytesRead;
                                size -= bytesRead;
                            }
                        }

                        if (nolength)
                        {
                            assetData = ms.ToArray();
                            ms.Close();
                        }
                        else
                        {
                            assetData = buffer;
                        }

                        #endregion Read the response
                    }
                }
            }
            catch (WebException ex)
            {
                m_log.Debug("[CABLE BEACH ASSETS]: Failed to fetch asset data from " + assetDataUri + ": " + ex.Message);
            }

            return assetData;
        }

        public bool Get(string id, Object sender, AssetRetrieved handler)
        {
            AssetBase asset = null;

            // Cache check
            if (m_Cache != null)
            {
                asset = m_Cache.Get(id);
                if (asset != null)
                {
                    handler(id, sender, asset);
                    return true;
                }
            }

            // Remote asset request
            asset = Get(id);
            Util.FireAndForget(delegate(object o) { handler(id, sender, asset); });

            return true;
        }

        public string Store(AssetBase asset)
        {
            if (asset.Temporary || asset.Local)
            {
                if (m_Cache != null)
                    m_Cache.Cache(asset);
                else
                    m_log.Warn("[CABLE BEACH ASSETS]: Cache is missing. A valid cache must be configured for proper operation");

                return asset.ID;
            }

            if (m_WorldServiceConnector != null)
            {
                // Cache the asset first
                if (m_Cache != null)
                    m_Cache.Cache(asset);

                // FIXME: OpenSim API needs to be fixed to pass this in wherever possible
                UUID userID = UUID.Zero;

                Uri createAssetUri = GetServiceCapability(userID, CableBeachServices.ASSET_CREATE_ASSET);
                if (createAssetUri != null)
                {
                    UUID assetID;
                    UUID.TryParse(asset.Metadata.ID, out assetID);

                    CreateAssetMessage create = new CreateAssetMessage();
                    create.Metadata = new MetadataDefault();

                    // Convert the OpenSim Type enum to a content-type if it is set, or try to use the OpenSim ContentType field
                    sbyte assetType = asset.Metadata.Type;
                    if (assetType != -1)
                    {
                        create.Metadata.ContentType = CableBeachUtils.SLAssetTypeToContentType(assetType);
                    }
                    else if (!String.IsNullOrEmpty(asset.Metadata.ContentType))
                    {
                        create.Metadata.ContentType = asset.Metadata.ContentType;
                    }
                    else
                    {
                        m_log.Warn("[CABLE BEACH ASSETS]: Storing asset " + assetID + " with a content-type of application/octet-stream");
                        create.Metadata.ContentType = "application/octet-stream";
                    }

                    create.Metadata.CreationDate = DateTime.Now;
                    create.Metadata.Description = asset.Metadata.Description;
                    create.Metadata.ID = assetID;
                    create.Metadata.Methods = new Dictionary<string, Uri>();
                    create.Metadata.Name = asset.Metadata.Name;
                    create.Metadata.SHA256 = OpenMetaverse.Utils.EmptyBytes;
                    create.Metadata.Temporary = asset.Metadata.Temporary;
                    create.Base64Data = Convert.ToBase64String(asset.Data);

                    CapsClient request = new CapsClient(createAssetUri);
                    OSDMap response = request.GetResponse(create.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                    if (response != null)
                    {
                        // Parse the response
                        CreateAssetReplyMessage reply = new CreateAssetReplyMessage();
                        reply.Deserialize(response);

                        if (reply.AssetID != UUID.Zero)
                        {
                            asset.FullID = reply.AssetID;
                            asset.ID = reply.AssetID.ToString();
                            return asset.ID;
                        }
                    }
                    else
                    {
                        m_log.Error("[CABLE BEACH ASSETS]: Failed to store asset at " + createAssetUri);
                    }
                }
                else
                {
                    m_log.Warn("[CABLE BEACH ASSETS]: Failed to store remote asset " + asset.ID + ", could not find a create_asset capability");
                }
            }
            else
            {
                m_log.Error("[CABLE BEACH ASSETS]: Cannot upload asset, no reference to a world server connector");
            }

            return String.Empty;
        }

        Uri GetServiceCapability(UUID userID, string capIdentifier)
        {
            Uri capability = m_WorldServiceConnector.GetServiceCapability(userID, ASSET_SERVICE_URI, new Uri(capIdentifier));

            if (capability == null)
                ModCableBeach.TryGetDefaultCap(m_DefaultAssetService, capIdentifier, out capability);

            return capability;
        }

        public bool UpdateContent(string id, byte[] data)
        {
            AssetBase asset = null;

            // Updating uses the same call as asset creation. Get the existing asset and store it
            // again with new data

            // NOTE: This function assumes that there won't be any race conditions between
            // simulators trying to update the same asset

            // Cache check
            if (m_Cache != null)
                asset = m_Cache.Get(id);

            // Remote asset fetch
            if (asset == null)
                asset = Get(id);

            if (asset != null)
            {
                // Update the asset and create it again, overwriting the old asset
                asset.Data = data;
                return Store(asset) == asset.ID;
            }

            return false;
        }

        public bool Delete(string id)
        {
            // Purge the asset from the cache
            if (m_Cache != null)
                m_Cache.Expire(id);

            // TODO: Check if we have a delete capability and try to delete from the remote asset server

            return false;
        }

        /// <summary>
        /// This method attempts to parse a UUID of of an asset ID, which can be either a UUID or a
        /// Cable Beach asset URL
        /// </summary>
        /// <param name="id">String to parse the asset UUID out of</param>
        /// <returns>The asset UUID that has been parsed out of the asset ID</returns>
        /*private UUID GetAssetID(string id)
        {
            UUID assetID;
            Uri assetUri;

            if (UUID.TryParse(id, out assetID))
            {
                // Request by UUID
                return assetID;
            }
            else if (Uri.TryCreate(id, UriKind.Absolute, out assetUri))
            {
                // Request by URI, try to parse a UUID out of the URI
                for (int i = assetUri.Segments.Length - 1; i >= 0; i--)
                {
                    if (UUID.TryParse(assetUri.Segments[i], out assetID))
                        return assetID;
                }
            }

            return UUID.Zero;
        }*/

        /// <summary>
        /// Converts local UUIDs to URLs pointing to the default asset service, and converts full URLs to
        /// the correct URL for the given request type
        /// </summary>
        /// <param name="id">Asset UUID or URL</param>
        /// <param name="requestType">REST method to append to the asset URL</param>
        /// <returns>Asset URL for the given request method</returns>
        private Uri GetAssetUri(string id, string requestType)
        {
            UUID assetID;
            Uri assetUri;

            if (UUID.TryParse(id, out assetID))
            {
                // Request by UUID, use the default asset service for this region
                return new Uri(m_DefaultAssetService, "/assets/" + assetID.ToString() + "/" + requestType);
            }
            else if (Uri.TryCreate(id, UriKind.Absolute, out assetUri))
            {
                // Request by URI, fetch from the URI
                // Example: http://assets.com/content/1e048410-8c23-11de-8a39-0800200c9a66?param1=true
                //           ->
                //          http://assets.com/content/1e048410-8c23-11de-8a39-0800200c9a66/metadata?param1=true)
                return new Uri(assetUri.Scheme + "://" + assetUri.Authority + assetUri.AbsolutePath.TrimEnd('/') + "/" + requestType + "?" + assetUri.Query);
            }
            else
            {
                m_log.Warn("[CABLE BEACH ASSETS]: Unrecognized asset id: " + id);
                return new Uri(m_DefaultAssetService, id + "/" + requestType);
            }
        }
    }
}
