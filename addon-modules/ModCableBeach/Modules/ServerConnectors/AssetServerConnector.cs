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
    public class AssetServerConnector : ServiceConnector
    {
        private IAssetService m_AssetService;

        public AssetServerConnector(IConfigSource config, IHttpServer server) :
            base(config, server, "AssetService")
        {
            IConfig serverConfig = config.Configs["AssetService"];
            if (serverConfig == null)
                throw new Exception("No AssetService section in config file");

            string assetService = serverConfig.GetString("LocalServiceModule", String.Empty);

            if (String.IsNullOrEmpty(assetService))
                throw new Exception("No LocalServiceModule in AssetService section in config file");

            Object[] args = new Object[] { config };
            m_AssetService = ServerUtils.LoadPlugin<IAssetService>(assetService, args);

            if (m_AssetService == null)
                throw new Exception("Failed to load IAssetService \"" + assetService + "\"");

            // Asset service endpoints
            server.AddStreamHandler(new TrustedStreamHandler("GET", "/assets", new CBAssetServerGetHandler(m_AssetService)));
            server.AddStreamHandler(new TrustedStreamHandler("POST", "/assets", new CBAssetServerPostHandler(m_AssetService)));
            server.AddStreamHandler(new TrustedStreamHandler("DELETE", "/assets", new CBAssetServerDeleteHandler(m_AssetService)));

            CableBeachServerState.Log.Info("[CABLE BEACH ASSETS]: AssetServerConnector is running");
        }
    }

    public class CBAssetServerGetHandler : BaseStreamHandler
    {
        private IAssetService m_AssetService;

        public override string ContentType { get { return null; } }

        public CBAssetServerGetHandler(IAssetService service) :
            base("GET", "/assets")
        {
            m_AssetService = service;
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            byte[] result = Utils.EmptyBytes;
            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;
            httpResponse.ContentType = "application/octet-stream";

            string[] p = SplitParams(path);

            if (p.Length > 1)
            {
                if (p[p.Length - 1] == "data")
                {
                    // Fetch the asset data from the database
                    byte[] assetData = m_AssetService.GetData(p[p.Length - 2]);

                    if (result != null)
                    {
                        httpResponse.StatusCode = (int)HttpStatusCode.OK;
                        result = assetData;
                    }
                }
                else if (p[p.Length - 1] == "metadata")
                {
                    AssetMetadata metadata = m_AssetService.GetMetadata(p[p.Length - 2]);

                    if (metadata != null)
                    {
                        GetAssetMetadataMessage message = new GetAssetMetadataMessage();
                        message.Metadata = new MetadataDefault();
                        message.Metadata.ContentType = CableBeachUtils.SLAssetTypeToContentType(metadata.Type);
                        message.Metadata.CreationDate = metadata.CreationDate;
                        message.Metadata.Description = metadata.Description;
                        UUID.TryParse(metadata.ID, out message.Metadata.ID);
                        message.Metadata.Methods = new System.Collections.Generic.Dictionary<string, Uri>(0);
                        message.Metadata.Name = metadata.Name;
                        message.Metadata.SHA256 = Utils.EmptyBytes; // TODO: metadata.SHA1;
                        message.Metadata.Temporary = metadata.Temporary;

                        httpResponse.StatusCode = (int)HttpStatusCode.OK;
                        httpResponse.ContentType = "application/json";
                        httpResponse.ContentEncoding = Encoding.UTF8;
                        result = Encoding.UTF8.GetBytes(OSDParser.SerializeJsonString(message.Serialize()));
                    }
                }
                else
                {
                    // TODO: Send the metadata and data back together
                }
            }

            return result;
        }
    }

    public class CBAssetServerPostHandler : BaseStreamHandler
    {
        private IAssetService m_AssetService;

        public CBAssetServerPostHandler(IAssetService service) :
            base("POST", "/assets")
        {
            m_AssetService = service;
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            CreateAssetReplyMessage reply = new CreateAssetReplyMessage();
            reply.AssetID = UUID.Zero;
            reply.AssetUri = null;

            try
            {
                OSD osdata = OSDParser.DeserializeJson(httpRequest.InputStream);

                if (osdata.Type == OSDType.Map)
                {
                    CreateAssetMessage message = new CreateAssetMessage();
                    message.Deserialize((OSDMap)osdata);

                    byte[] assetData = null;
                    try { assetData = Convert.FromBase64String(message.Base64Data); }
                    catch (Exception) { }

                    if (assetData != null && assetData.Length > 0)
                    {
                        AssetBase asset = new AssetBase();
                        asset.Data = assetData;
                        asset.Metadata.ContentType = message.Metadata.ContentType;
                        asset.Metadata.CreationDate = DateTime.Now;
                        asset.Metadata.Description = message.Metadata.Description;
                        asset.Metadata.FullID = (message.Metadata.ID != UUID.Zero) ? message.Metadata.ID : UUID.Random();
                        asset.Metadata.ID = asset.Metadata.FullID.ToString();
                        asset.Metadata.Local = false;
                        asset.Metadata.Name = message.Metadata.Name;
                        asset.Metadata.SHA1 = Utils.EmptyBytes; // TODO: Calculate the SHA-1 hash of the asset here?
                        asset.Metadata.Temporary = message.Metadata.Temporary;
                        asset.Metadata.Type = CableBeachUtils.ContentTypeToSLAssetType(message.Metadata.ContentType);

                        string assetID = m_AssetService.Store(asset);

                        if (!String.IsNullOrEmpty(assetID))
                        {
                            reply.AssetID = message.Metadata.ID;
                            httpResponse.StatusCode = (int)HttpStatusCode.Created;
                        }
                        else
                        {
                            httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
                        }
                    }
                    else
                    {
                        httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    }
                }
            }
            catch (Exception)
            {
                httpResponse.StatusCode = (int)HttpStatusCode.InternalServerError;
            }

            httpResponse.ContentType = "application/json";
            httpResponse.ContentEncoding = Encoding.UTF8;
            return Encoding.UTF8.GetBytes(OSDParser.SerializeJsonString(reply.Serialize()));
        }
    }

    public class CBAssetServerDeleteHandler : BaseStreamHandler
    {
        private IAssetService m_AssetService;

        public CBAssetServerDeleteHandler(IAssetService service) :
            base("DELETE", "/assets")
        {
            m_AssetService = service;
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            httpResponse.StatusCode = (int)HttpStatusCode.NotFound;

            string[] p = SplitParams(path);

            if (p.Length > 0)
            {
                if (m_AssetService.Delete(p[p.Length - 1]))
                    httpResponse.StatusCode = (int)HttpStatusCode.OK;
            }

            return Utils.EmptyBytes;
        }
    }
}
