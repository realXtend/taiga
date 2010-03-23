using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using log4net;
using Nini.Config;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Communications;
using OpenSim.Framework.Communications.Services;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Framework.Servers;
using ModularRex.RexDBObjects;
using OpenSim.Framework.Servers.HttpServer;
using System.Net;
using ModularRex.RexNetwork.RexLogin;
using ModularRex.RexNetwork;
using OpenSim.Services.Interfaces;
using CableBeachMessages;
using OpenMetaverse.Http;
using System.Text.RegularExpressions;

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    partial class RealXtendLogin : RexLoginModule
    {
        public static bool TryGetStartingRegion(Avatar avatar, string startLocation, ref OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginData response,
            out CableBeachMessages.RegionInfo startRegion, out Vector3 startPosition)
        {
            startPosition = Vector3.Zero;
            startRegion = new CableBeachMessages.RegionInfo();

            uint regionX, regionY;

            switch (startLocation)
            {
                case "home":
                case "safe":
                    // Try and get the home location for this avatar
                    regionX = avatar.GetAttribute(AvatarAttributes.HOME_REGION_X).AsUInteger();
                    regionY = avatar.GetAttribute(AvatarAttributes.HOME_REGION_Y).AsUInteger();
                    m_log.Debug("Trying to fetch home region at " + regionX + "," + regionY);
                    startRegion = GetNearestRegion(avatar, regionX, regionY);
                    if (startRegion.ID != UUID.Zero)
                    {
                        startPosition = startRegion.DefaultPosition;
                        response.StartLocation = "home";
                    }

                    break;
                case "last":
                    // Try and get the last location for this avatar
                    regionX = avatar.GetAttribute(AvatarAttributes.LAST_REGION_X).AsUInteger();
                    regionX = avatar.Attributes[AvatarAttributes.LAST_REGION_X].AsUInteger();
                    regionY = avatar.GetAttribute(AvatarAttributes.LAST_REGION_Y).AsUInteger();
                    m_log.Debug("Trying to fetch last region at " + regionX + "," + regionY);
                    startRegion = GetNearestRegion(avatar, regionX, regionY);
                    if (startRegion.ID != UUID.Zero)
                    {
                        startPosition = startRegion.DefaultPosition;
                        response.StartLocation = "last";
                    }

                    break;
                default:
                    Regex reURI = new Regex(@"^uri:(?<region>[^&]+)&(?<x>\d+)&(?<y>\d+)&(?<z>\d+)$");
                    Match uriMatch = reURI.Match(startLocation);
                    if (uriMatch != null && m_OpenSimMap.TryFetchRegion(uriMatch.Groups["region"].Value, out startRegion) == BackendResponse.Success)
                    {
                        Single.TryParse(uriMatch.Groups["x"].Value, out startPosition.X);
                        Single.TryParse(uriMatch.Groups["y"].Value, out startPosition.Y);
                        Single.TryParse(uriMatch.Groups["z"].Value, out startPosition.Z);
                    }
                    else
                    {
                        m_log.Warn("[LindenLoginHelper] Can't locate a simulator from custom login URI: " + startLocation);
                    }

                    break;
            }

            if (startRegion.ID != UUID.Zero)
            {
                response.LookAt = startRegion.DefaultLookAt;
                response.RegionX = startRegion.X;
                response.RegionY = startRegion.Y;
                response.SimAddress = startRegion.IP.ToString();
                response.SimPort = (uint)startRegion.Port;

                return true;
            }
            else
            {
                m_log.Error("[LindenLoginHelper] Could not find an available region for login");
                return false;
            }
        }

        static CableBeachMessages.RegionInfo GetNearestRegion(Avatar avatar, uint regionX, uint regionY)
        {
            CableBeachMessages.RegionInfo region = new CableBeachMessages.RegionInfo();
            m_OpenSimMap.TryFetchRegionNearest(regionX, regionY, out region);
            //server.MapProvider.TryFetchRegionNearest(regionX, regionY, out region);
            return region;
        }
    }
}