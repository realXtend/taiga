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
using System.Text;
using System.Text.RegularExpressions;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.Http;
using OpenMetaverse.StructuredData;
using CableBeachMessages;
using OpenSim.Framework.Servers.HttpServer;
using log4net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{


    public static class LindenLoginHelper
    {
        public const int REQUEST_TIMEOUT = 1000 * 100;
        public static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        static Random rng = new Random();

        public static void SetClassifiedCategories(ref LindenLoginData response)
        {
            response.AddClassifiedCategory(1, "Shopping");
            response.AddClassifiedCategory(2, "Land Rental");
            response.AddClassifiedCategory(3, "Property Rental");
            response.AddClassifiedCategory(4, "Special Attraction");
            response.AddClassifiedCategory(5, "New Products");
            response.AddClassifiedCategory(6, "Employment");
            response.AddClassifiedCategory(7, "Wanted");
            response.AddClassifiedCategory(8, "Service");
            response.AddClassifiedCategory(9, "Personal");
        }

        public static Hashtable GetBuddyList(UUID avatarID)
        {
            // TODO: Buddy list support
            return new Hashtable(0);
        }

        public static bool TryPrepareLogin(OpenSim.Grid.UserServer.Modules.RexLogin.Avatar avatar,
            CableBeachMessages.RegionInfo startRegion,
            Vector3 startPosition,
            string clientVersion,
            System.Net.IPAddress clientIP,
            ref OpenSim.Grid.UserServer.Modules.RexLogin.LindenLoginData response,
            X509Certificate2 httpCertificate )
        {
            EnableClientMessage message = new EnableClientMessage();
            message.Identity = avatar.Identity;
            message.AgentID = avatar.ID;
            message.Attributes = avatar.Attributes;
            message.CallbackUri = null;
            message.ChildAgent = false;
            message.CircuitCode = CreateCircuitCode();
            message.ClientVersion = clientVersion;
            message.IP = clientIP;
            message.RegionHandle = startRegion.Handle;
            message.SecureSessionID = response.SecureSessionID;
            message.Services = avatar.Services.ToMessageDictionary();
            Dictionary<Uri, Uri> avStrgDict = new Dictionary<Uri, Uri>();
            avStrgDict.Add(
                OpenSim.Grid.UserServer.Modules.RexLogin.RexAvatarAttributes.AVATAR_STORAGE_URL,
                avatar.Attributes[OpenSim.Grid.UserServer.Modules.RexLogin.RexAvatarAttributes.AVATAR_STORAGE_URL].AsUri());
            message.Services.Add(OpenSim.Grid.UserServer.Modules.RexLogin.RexAvatarAttributes.AVATAR_STORAGE_URL, avStrgDict);
            message.SessionID = response.SessionID;

            Uri enableClientCap;
            if (startRegion.Capabilities.TryGetValue(new Uri(CableBeachServices.SIMULATOR_ENABLE_CLIENT), out enableClientCap))
            {
                CapsClient request = (httpCertificate != null) ?
                new CapsClient(enableClientCap, httpCertificate) :
                new CapsClient(enableClientCap);

                OSDMap responseMap = request.GetResponse(message.Serialize(), OSDFormat.Json, REQUEST_TIMEOUT) as OSDMap;

                if (responseMap != null)
                {
                    EnableClientReplyMessage reply = new EnableClientReplyMessage();
                    reply.Deserialize(responseMap);

                    if (reply.SeedCapability != null)
                    {
                        m_log.Info("enable_client succeeded, sent circuit code " + message.CircuitCode + " and received seed capability " +
                            reply.SeedCapability + " from " + enableClientCap);

                        response.CircuitCode = message.CircuitCode;
                        response.SeedCapability = reply.SeedCapability.ToString();
                        return true;
                    }
                    else
                    {
                        m_log.Error("[LindenLoginHelper] enable_client call to region " + startRegion.Name + " for login from " + avatar.Identity +
                            " failed, did not return a seed capability");
                    }
                }
                else
                {
                    m_log.Error("[LindenLoginHelper] enable_client call to region " + startRegion.Name + " for login from " + avatar.Identity +
                        " failed, could not contact or invalid response");
                }
            }
            else
            {
                m_log.Error("[LindenLoginHelper] enable_client call failed, region " + startRegion.Name +
                    " does not have an enable_client capability");
            }

            return false;
        }

        public static int CreateCircuitCode()
        {
            // TODO: Track these so we don't generate duplicate circuit codes
            return rng.Next(0, Int32.MaxValue);
        }

        #region Login XML responses

        public static XmlRpcResponse CreateFailureResponse(string reason, string message, bool loginSuccess)
        {
            Hashtable responseData = new Hashtable(3);
            responseData["reason"] = reason;
            responseData["message"] = message;
            responseData["login"] = loginSuccess.ToString().ToLower();

            XmlRpcResponse response = new XmlRpcResponse();
            response.Value = responseData;
            return response;
        }

        public static XmlRpcResponse CreateLoginFailedResponse()
        {
            return CreateFailureResponse(
                "key",
                "Could not authenticate your avatar. Please check your username and password, and check the grid if problems persist.",
                false);
        }

        public static XmlRpcResponse CreateLoginGridErrorResponse()
        {
            return CreateFailureResponse(
                "key",
                "Error connecting to grid. Could not perceive credentials from login XML.",
                false);
        }

        public static XmlRpcResponse CreateLoginBlockedResponse()
        {
            return CreateFailureResponse(
                "presence",
                "Logins are currently restricted. Please try again later",
                false);
        }

        public static XmlRpcResponse CreateLoginInternalErrorResponse()
        {
            return CreateFailureResponse(
                "key",
                "The login server failed to complete the login process. Please try again later",
                false);
        }

        public static XmlRpcResponse CreateLoginServicesErrorResponse()
        {
            return CreateFailureResponse(
                "key",
                "The login server failed to locate a required grid service. Please try again later",
                false);
        }

        public static XmlRpcResponse CreateLoginNoRegionResponse()
        {
            return CreateFailureResponse(
                "key",
                "The login server could not find an available region to login to. Please try again later",
                false);
        }

        #endregion Login XML responses
    }
}
