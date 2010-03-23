using System;
using System.Collections.Generic;
using System.Text;
using Nwc.XmlRpc;
using System.Collections;
using OpenMetaverse;
using CableBeachMessages;
using OpenMetaverse.StructuredData;
using log4net;
using System.Reflection;

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    public static class RexLoginHelper
    {
        public static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static Avatar GetUserByAccount(string account, string authUrl)
        {
            try
            {

                if (account == null || account.Length == 0 ||
                    authUrl == null || authUrl.Length == 0)
                {
                    return null;
                }

                if (account.Contains("@"))
                {
                    account = account.Split('@')[0];
                }

                Hashtable requestData = new Hashtable();
                requestData.Add("avatar_account", account);

                XmlRpcResponse response = DoRequest("get_user_by_account", requestData, authUrl);

                if (response == null)
                {
                    return null;
                }

                Avatar userProfile = ConvertXMLRPCDataToUserProfile((Hashtable)response.Value);
                userProfile.Attributes.Add(RexAvatarAttributes.AUTH_URI, OSD.FromString(authUrl));

                if (userProfile == null)
                {
                    return null;
                }

                return userProfile;

            }
            catch (Exception e)
            {
                ;
            }

            return null;

        }

        public static Avatar ConvertXMLRPCDataToUserProfile(Hashtable data)
        {

            try
            {

                if (data.Contains("error_type"))
                {
                    m_log.Warn("[AUTHENTICATIONSERVICE]: " +
                               "Error sent by authentication server server when trying to get user profile: (" +
                               data["error_type"] +
                               "): " + data["error_desc"]);
                    return null;
                }

                Avatar userData = new Avatar();
                userData.Attributes.Add(AvatarAttributes.FIRST_NAME, OSD.FromString((string)data["firstname"]));
                userData.Attributes.Add(AvatarAttributes.LAST_NAME, OSD.FromString((string)data["lastname"]));
                userData.ID = new UUID((string)data["uuid"]);
                userData.Attributes.Add(AvatarAttributes.AVATAR_ID, OSD.FromString((string)data["uuid"]));

                //userData.UserInventoryURI = (string)data["server_inventory"];
                //userData.UserAssetURI = (string)data["server_asset"];
                userData.Attributes.Add(AvatarAttributes.FIRST_LIFE_BIOGRAPHY, OSD.FromString((string)data["profile_firstlife_about"]));
                userData.Attributes.Add(AvatarAttributes.FIRST_LIFE_IMAGE_ID, OSD.FromString((string)data["profile_firstlife_image"]));
                userData.Attributes.Add(AvatarAttributes.CAN_DO, OSD.FromString((string)data["profile_can_do"]));
                userData.Attributes.Add(AvatarAttributes.WANT_DO, OSD.FromObject(data["profile_want_do"]));
                userData.Attributes.Add(AvatarAttributes.BIOGRAPHY, OSD.FromString((string)data["profile_about"]));
                userData.Attributes.Add(AvatarAttributes.IMAGE_ID, OSD.FromString((string)data["profile_image"]));
                userData.Attributes.Add(AvatarAttributes.LAST_REGION_ID, OSD.FromString((string)data["profile_lastlogin"]));
                //TODO: insert home region
                //userData.HomeRegion = Convert.ToUInt64((string)data["home_region"]);
                if (data.Contains("home_region_id"))
                    userData.Attributes.Add(AvatarAttributes.HOME_REGION_ID, OSD.FromString((string)data["home_region_id"]));
                else
                    userData.Attributes.Add(AvatarAttributes.HOME_REGION_ID, OSD.FromUUID(UUID.Zero));

                userData.Attributes.Add(AvatarAttributes.HOME_POSITION, OSD.FromVector3(
                    new Vector3((float)Convert.ToDecimal((string)data["home_coordinates_x"]),
                                  (float)Convert.ToDecimal((string)data["home_coordinates_y"]),
                                  (float)Convert.ToDecimal((string)data["home_coordinates_z"]))));
                userData.Attributes.Add(AvatarAttributes.HOME_LOOKAT, OSD.FromVector3(
                    new Vector3((float)Convert.ToDecimal((string)data["home_look_x"]),
                                  (float)Convert.ToDecimal((string)data["home_look_y"]),
                                  (float)Convert.ToDecimal((string)data["home_look_z"]))));

                if (data.Contains("user_flags"))
                    userData.Attributes.Add(AvatarAttributes.USER_FLAGS, OSD.FromString((string)data["user_flags"]));
                if (data.Contains("god_level"))
                    userData.Attributes.Add(AvatarAttributes.GOD_LEVEL, OSD.FromString((string)data["god_level"]));

                if (data.Contains("custom_type"))
                    userData.Attributes.Add(AvatarAttributes.CUSTOM_TYPE, OSD.FromString((string)data["custom_type"]));
                else
                    userData.Attributes.Add(AvatarAttributes.CUSTOM_TYPE, OSD.FromString(String.Empty));

                if (data.Contains("partner"))
                    userData.Attributes.Add(AvatarAttributes.PARTNER_ID, OSD.FromString((string)data["partner"]));
                else
                    userData.Attributes.Add(AvatarAttributes.PARTNER_ID, OSD.FromUUID(UUID.Zero));

                if (data.Contains("account"))
                    userData.Attributes.Add(RexAvatarAttributes.ACCOUNT, OSD.FromString((string)data["account"]));
                else
                    userData.Attributes.Add(RexAvatarAttributes.ACCOUNT, OSD.FromString(String.Empty));

                if (data.Contains("realname"))
                    userData.Attributes.Add(RexAvatarAttributes.REALNAME, OSD.FromString((string)data["realname"]));
                else
                    userData.Attributes.Add(RexAvatarAttributes.REALNAME, OSD.FromString(String.Empty));

                if (data.Contains("sessionHash"))
                    userData.Attributes.Add(RexAvatarAttributes.SESSIONHASH, OSD.FromString((string)data["sessionHash"]));
                else
                    userData.Attributes.Add(RexAvatarAttributes.SESSIONHASH, OSD.FromString(String.Empty));

                if (data.Contains("as_address"))
                    userData.Attributes.Add(RexAvatarAttributes.AVATAR_STORAGE_URL, OSD.FromString((string)data["as_address"]));
                else
                    userData.Attributes.Add(RexAvatarAttributes.AVATAR_STORAGE_URL, OSD.FromString(String.Empty));

                if (data.Contains("skypeUrl"))
                    userData.Attributes.Add(RexAvatarAttributes.SKYPE_URL, OSD.FromString((string)data["skypeUrl"]));
                else
                    userData.Attributes.Add(RexAvatarAttributes.SKYPE_URL, OSD.FromString(String.Empty));

                if (data.Contains("gridUrl"))
                    userData.Attributes.Add(RexAvatarAttributes.GRID_URL, OSD.FromString((string)data["gridUrl"]));
                else
                    userData.Attributes.Add(RexAvatarAttributes.GRID_URL, OSD.FromString(String.Empty));

                
                //Avatar agent = new Avatar();
                //agent.ID
                //agent.Identity.UserInfo.
                //agent.Services.Keys
                //agent.Attributes.Add
                //if (data.Contains("currentAgent"))
                //{
                //    Hashtable agentData = (Hashtable)data["currentAgent"];
                //    agent.AgentIP = (string)agentData["agentIP"];
                //    agent.AgentOnline = Convert.ToBoolean((string)data["agentOnline"]);
                //    agent.AgentPort = Convert.ToUInt32((string)data["agentPort"]);
                //    agent.Handle = Convert.ToUInt64((string)data["handle"]);
                //    agent.InitialRegion = new UUID((string)data["initialRegion"]);
                //    agent.LoginTime = Convert.ToInt32((string)data["loginTime"]);
                //    agent.LogoutTime = Convert.ToInt32((string)data["logoutTime"]);
                //    agent.LookAt = new Vector3((float)Convert.ToDecimal((string)data["home_look_x"]),
                //                  (float)Convert.ToDecimal((string)data["home_look_y"]),
                //                  (float)Convert.ToDecimal((string)data["home_look_z"]));
                //    agent.Position = new Vector3((float)Convert.ToDecimal((string)data["currentPos_x"]),
                //                  (float)Convert.ToDecimal((string)data["currentPos_y"]),
                //                  (float)Convert.ToDecimal((string)data["currentPos_z"]));
                //    agent.ProfileID = new UUID((string)data["UUID"]); ;
                //    agent.Region = new UUID((string)data["regionID"]);
                //    agent.SecureSessionID = new UUID((string)data["secureSessionID"]);
                //    agent.SessionID = new UUID((string)data["sessionID"]);
                //    agent.currentRegion = new UUID((string)data["currentRegion"]);
                //    userData.CurrentAgent = agent;
                //}

                return userData;

            }
            catch (Exception except)
            {
                ;
            }

            return null;

        }

        public static XmlRpcResponse DoRequest(string method, Hashtable requestParams, string sendAddr)
        {
            try
            {
                ArrayList SendParams = new ArrayList();
                SendParams.Add(requestParams);
                XmlRpcRequest req = new XmlRpcRequest(method, SendParams);
                if (!sendAddr.StartsWith("http://"))
                    sendAddr = "http://" + sendAddr;
                return req.Send(sendAddr, 3000);
            }
            catch (Exception except)
            {
                return null;
            }
        }

    }
}
