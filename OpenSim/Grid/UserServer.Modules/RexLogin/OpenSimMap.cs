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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Nwc.XmlRpc;
using OpenMetaverse;
using CableBeachMessages;
using OpenSim.Services.Interfaces;

/// <summary>
/// Copy paste from WorldServer.Extensions for the most part
/// </summary>
namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    /// <summary>
    /// Response from a call to a backend provider
    /// </summary>
    public enum BackendResponse
    {
        /// <summary>The call succeeded</summary>
        Success,
        /// <summary>The resource requested was not found</summary>
        NotFound,
        /// <summary>A server failure prevented the call from
        /// completing</summary>
        Failure
    }

    public class OpenSimMap //: IExtension<WorldServer>, IMapProvider
    {
        const int REQUEST_TIMEOUT = 1000 * 30;

        //WorldServer server;
        Uri m_GridServerUrl;
        protected IGridService m_GridService;

        public OpenSimMap(Uri gridServerUrl, IGridService gridService)
        {
            m_GridService = gridService;
            m_GridServerUrl = gridServerUrl;
        }        

        public BackendResponse TryFetchRegion(UUID id, out RegionInfo region)
        {
            List<RegionInfo> regions;
            BackendResponse response = TryFetchRegionsFromGridService(out regions);

            if (response == BackendResponse.Success)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    if (regions[i].ID == id)
                    {
                        region = regions[i];
                        return BackendResponse.Success;
                    }
                }
            }
            else
            {
                region = default(RegionInfo);
                return response;
            }

            region = default(RegionInfo);
            return response;
        }

        public BackendResponse TryFetchRegion(uint x, uint y, out RegionInfo region)
        {
            List<RegionInfo> regions;
            BackendResponse response = TryFetchRegionsFromGridService(out regions);

            if (x < 25600)
            {
                ;//Logger.Debug("[OpenSimMap] Multiplying coordinates " + x + "," + y + " by 256");
                x *= 256u;
                y *= 256u;
            }

            ulong handle = Utils.UIntsToLong(x, y);

            if (response == BackendResponse.Success)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    if (regions[i].Handle == handle)
                    {
                        region = regions[i];
                        return BackendResponse.Success;
                    }
                }
            }
            else
            {
                region = default(RegionInfo);
                return response;
            }

            region = default(RegionInfo);
            return BackendResponse.NotFound;
        }

        public BackendResponse TryFetchRegion(string name, out RegionInfo region)
        {
            List<RegionInfo> regions;
            BackendResponse response = TryFetchRegionsFromGridService(out regions);

            if (response == BackendResponse.Success)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    // TODO: Implement a better matching algorithm
                    if (regions[i].Name.ToLowerInvariant().Contains(name.ToLowerInvariant()))
                    {
                        region = regions[i];
                        return BackendResponse.Success;
                    }
                }
            }
            else
            {
                region = default(RegionInfo);
                return response;
            }

            region = default(RegionInfo);
            return BackendResponse.NotFound;
        }

        public BackendResponse TryFetchRegionNearest(uint x, uint y, out RegionInfo region)
        {
            List<RegionInfo> regions;
            BackendResponse response = TryFetchRegionsFromGridService(out regions);

            if (x < 25600)
            {
                ;//Logger.Debug("[OpenSimMap] Multiplying coordinates " + x + "," + y + " by 256");
                x *= 256u;
                y *= 256u;
            }

            Vector2 targetPosition = new Vector2(x, y);
            float bestDistance = Single.MaxValue;
            region = default(RegionInfo);

            if (response == BackendResponse.Success)
            {
                for (int i = 0; i < regions.Count; i++)
                {
                    uint regionX, regionY;
                    Utils.LongToUInts(regions[i].Handle, out regionX, out regionY);
                    Vector2 position = new Vector2(regionX, regionY);

                    float dist = Vector2.Distance(position, targetPosition);

                    if (dist < bestDistance)
                    {
                        bestDistance = dist;
                        region = regions[i];
                    }
                }
            }
            else
            {
                region = default(RegionInfo);
                return response;
            }

            if (bestDistance == Single.MaxValue)
                return BackendResponse.Failure;
            else
                return BackendResponse.Success;
        }

        public BackendResponse TryFetchDefaultRegion(out RegionInfo region)
        {
            return TryFetchRegionNearest(1000, 1000, out region);
        }

        public BackendResponse RegionSearch(string query, out List<RegionInfo> results)
        {
            results = null;
            return BackendResponse.Failure;
        }

        public int RegionCount()
        {
            return 0;
        }

        public int OnlineRegionCount()
        {
            return 0;
        }

        public int ForEach(Action<RegionInfo> action, int start, int count)
        {
            return 0;
        }

        // Fetching regions with "map_block"
        BackendResponse TryFetchRegions(out List<RegionInfo> regions)
        {
            Hashtable requestData = new Hashtable();
            requestData["xmin"] = 0;
            requestData["ymin"] = 0;
            requestData["xmax"] = 100000;
            requestData["ymax"] = 100000;

            ArrayList requestParams = new ArrayList();
            requestParams.Add(requestData);
            XmlRpcRequest request = new XmlRpcRequest("map_block", requestParams);

            try
            {
                XmlRpcResponse response = request.Send(m_GridServerUrl.ToString(), REQUEST_TIMEOUT);
                Hashtable responseData = (Hashtable)response.Value;

                if (!responseData.ContainsKey("error"))
                {
                    regions = XmlBlocksToRegionInfos(responseData);
                    ;//Logger.Info("[OpenSimMap] GridServer returned " + regions.Count + " regions");
                    return BackendResponse.Success;
                }
                else
                {
                    ;//Logger.Error("[OpenSimMap] GridServer returned an error for map_block: " + (string)responseData["error"]);
                }
            }
            catch (Exception e)
            {
                ;//Logger.Error("[OpenSimMap] Failed to retrieve a GridServer response for method map_block: " + e.Message);
            }

            regions = null;
            return BackendResponse.Failure;
        }


        // Fetching regions from gridservice
        BackendResponse TryFetchRegionsFromGridService(out List<RegionInfo> regions) 
        {
            try
            {
                //TODO: figure out maxX and maxY vals (100 000 not enough)
                List<OpenSim.Services.Interfaces.GridRegion> regs = m_GridService.GetRegionRange(UUID.Zero, 0, 1000000, 0, 1000000);
                regions = InterfaceRegionsToCableBeachRegions(regs);
                if (regions.Count > 0)
                    return BackendResponse.Success;
                else
                    return BackendResponse.NotFound;
            }
            catch (Exception)
            {
                regions = new List<RegionInfo>();
                return BackendResponse.Failure;
            }
        }

        private List<RegionInfo> InterfaceRegionsToCableBeachRegions(List<OpenSim.Services.Interfaces.GridRegion> gridRegions)
        {
            List<RegionInfo> regions = new List<RegionInfo>();
            foreach (OpenSim.Services.Interfaces.GridRegion gridReg in gridRegions) {
                string hostname = gridReg.ServerURI;
                int httpPort = (int)gridReg.HttpPort;
                RegionInfo regionInfo = new RegionInfo();
                //regionInfo.AgentCount = (int)simData["agents"]; //TODO
                regionInfo.DefaultLookAt = Vector3.UnitX;
                regionInfo.DefaultPosition = new Vector3(128f, 128f, 100f);
                //regionInfo.Flags = (int)simData["region-flags"]; //TODO
                regionInfo.Handle = gridReg.RegionHandle;
                regionInfo.ID = gridReg.RegionID;
                string ip = hostname.Split('/')[2].Split(':')[0];
                regionInfo.IP = Utils.HostnameToIPv4(ip);                
                regionInfo.MapTextureID = gridReg.TerrainImage;
                regionInfo.Name = gridReg.RegionName;
                regionInfo.Online = true;
                regionInfo.Owner = null;
                //regionInfo.Owner = ConvertUUIDToUri(gridReg.EstateOwner); //TODO
                regionInfo.Port = gridReg.ExternalEndPoint.Port;
                //regionInfo.WaterHeight = (float)(int)simData["water-height"]; //TODO
                
                // HACK: Hardcoded in the assumption that the simulator has an enable_client service endpoint at a fixed location
                regionInfo.Capabilities = new Dictionary<Uri, Uri>();
                regionInfo.Capabilities[new Uri(CableBeachServices.SIMULATOR_ENABLE_CLIENT)] = new Uri("http://" + ip + ":" + httpPort + "/enable_client");
                regions.Add(regionInfo);                
            }
            return regions;
        }

        /*BackendResponse TryFetchRegion(Hashtable requestData, out RegionInfo region)
        {
            ArrayList requestParams = new ArrayList();
            requestParams.Add(requestData);
            XmlRpcRequest request = new XmlRpcRequest("simulator_data_request", requestParams);

            try
            {
                XmlRpcResponse response = request.Send(gridServerUrl.ToString(), REQUEST_TIMEOUT);
                Hashtable responseData = (Hashtable)response.Value;

                if (!responseData.ContainsKey("error"))
                {
                    region = XmlToRegionInfo(responseData, String.Empty);
                    return BackendResponse.Success;
                }
                else
                {
                    Logger.Error("[OpenSimMap] GridServer returned an error for simulator_data_request: " + (string)responseData["error"]);
                }
            }
            catch (Exception e)
            {
                Logger.Error("[OpenSimMap] Failed to retrieve a GridServer response for method simulator_data_request: " + e.Message);
            }

            region = default(RegionInfo);
            return BackendResponse.Failure;
        }*/

        List<RegionInfo> XmlBlocksToRegionInfos(Hashtable responseData)
        {
            List<RegionInfo> regions = new List<RegionInfo>();

            if (responseData.Contains("sim-profiles"))
            {
                IList regionList = (IList)responseData["sim-profiles"];

                for (int i = 0; i < regionList.Count; i++)
                {
                    Hashtable simData = (Hashtable)regionList[i];
                    string hostname = (string)simData["sim_ip"];
                    int httpPort = Convert.ToInt32((string)simData["http_port"]);

                    RegionInfo regionInfo = new RegionInfo();
                    regionInfo.AgentCount = (int)simData["agents"];
                    regionInfo.DefaultLookAt = Vector3.UnitX;
                    regionInfo.DefaultPosition = new Vector3(128f, 128f, 100f);
                    regionInfo.Flags = (int)simData["region-flags"];
                    regionInfo.Handle = Convert.ToUInt64((string)simData["regionhandle"]);
                    regionInfo.ID = UUID.Parse((string)simData["uuid"]);
                    regionInfo.IP = Utils.HostnameToIPv4(hostname);
                    regionInfo.MapTextureID = UUID.Parse((string)simData["map-image-id"]);
                    regionInfo.Name = (string)simData["name"];
                    regionInfo.Online = true;
                    regionInfo.Owner = null;
                    regionInfo.Port = Convert.ToInt32((string)simData["sim_port"]);
                    regionInfo.WaterHeight = (float)(int)simData["water-height"];

                    // HACK: Hardcoded in the assumption that the simulator has an enable_client service endpoint at a fixed location
                    regionInfo.Capabilities = new Dictionary<Uri, Uri>();
                    regionInfo.Capabilities[new Uri(CableBeachServices.SIMULATOR_ENABLE_CLIENT)] = new Uri("http://" + hostname + ":" + httpPort + "/enable_client");

                    regions.Add(regionInfo);
                }
            }

            return regions;
        }

        /*RegionInfo XmlToRegionInfo(Hashtable responseData, string prefix)
        {
            uint regX = Convert.ToUInt32((string)responseData[prefix + "region_locx"]);
            uint regY = Convert.ToUInt32((string)responseData[prefix + "region_locy"]);
            string hostname = (string)responseData[prefix + "sim_ip"];
            uint httpPort = Convert.ToUInt32((string)responseData[prefix + "http_port"]);

            RegionInfo regionInfo = new RegionInfo();
            regionInfo.AgentCount = 0;
            regionInfo.DefaultLookAt = Vector3.UnitX;
            regionInfo.DefaultPosition = new Vector3(128f, 128f, 100f);
            regionInfo.Flags = 0;
            regionInfo.Handle = Utils.UIntsToLong(regX, regY);
            regionInfo.ID = new UUID((string)responseData[prefix + "region_UUID"]);
            regionInfo.IP = Utils.HostnameToIPv4(hostname);
            regionInfo.MapTextureID = new UUID((string)responseData[prefix + "map_UUID"]);
            regionInfo.Name = (string)responseData[prefix + "region_name"];
            regionInfo.Online = true;
            regionInfo.Owner = null;
            regionInfo.Port = (int)Convert.ToUInt32(responseData[prefix + "sim_port"]);
            regionInfo.WaterHeight = 0f;
            //Convert.ToUInt32((string)responseData[prefix + "remoting_port"])

            // HACK: Hardcoded in the assumption that the simulator has an enable_client service endpoint at a fixed location
            regionInfo.Capabilities = new Dictionary<Uri, Uri>();
            regionInfo.Capabilities[new Uri(CableBeachServices.SIMULATOR_ENABLE_CLIENT)] = new Uri("http://" + hostname + ":" + httpPort + "/enable_client");

            return regionInfo;
        }*/
    }
}
