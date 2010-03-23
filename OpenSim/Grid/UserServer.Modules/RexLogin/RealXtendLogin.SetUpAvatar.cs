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
using ModularRex.RexDBObjects;
using OpenSim.Framework.Servers.HttpServer;
using System.Net;
using ModularRex.RexNetwork.RexLogin;
using ModularRex.RexNetwork;
using OpenSim.Services.Interfaces;
using CableBeachMessages;
using OpenMetaverse.Http;
using System.Text.RegularExpressions;
using OpenSim.Data;

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    partial class RealXtendLogin : RexLoginModule
    {

        /// <summary>
        /// Set up avatar basic data
        /// </summary>
        /// <param name="account"></param>
        /// <param name="authUrl"></param>
        /// <param name="sessionID"></param>
        /// <param name="theUser"></param>
        /// <param name="clientVersion"></param>
        /// <param name="client"></param>
        /// <returns></returns>
        protected OpenSim.Grid.UserServer.Modules.RexLogin.Avatar SetUpAvatar(
            string account, string authUrl, UUID sessionID,
            RexUserProfileData theUser) 
        {
            OpenSim.Grid.UserServer.Modules.RexLogin.ServiceCollection services =
                new OpenSim.Grid.UserServer.Modules.RexLogin.ServiceCollection();

            OpenSim.Grid.UserServer.Modules.RexLogin.Avatar avatar =
                OpenSim.Grid.UserServer.Modules.RexLogin.RexLoginHelper.GetUserByAccount(account, authUrl);

            avatar.Services = services;
            avatar.Attributes[AvatarAttributes.FIRST_NAME] = OSD.FromString(theUser.FirstName);
            avatar.Attributes[AvatarAttributes.LAST_NAME] = OSD.FromString(theUser.SurName);
                
            
            if(theUser.CanDoMask!=null)
                avatar.Attributes[AvatarAttributes.CAN_DO] = OSD.FromUInteger(theUser.CanDoMask);
            if(theUser.Created!=null)
                avatar.Attributes[AvatarAttributes.BIRTH_DATE] = OSD.FromInteger(theUser.Created);
            if(theUser.CustomType!=null)
                avatar.Attributes[AvatarAttributes.CUSTOM_TYPE] = OSD.FromString(theUser.CustomType);
            if(theUser.Email!=null)
                avatar.Attributes[AvatarAttributes.EMAIL] = OSD.FromString(theUser.Email);
            if(theUser.FirstLifeAboutText!=null)
                avatar.Attributes[AvatarAttributes.FIRST_LIFE_BIOGRAPHY] = OSD.FromString(theUser.FirstLifeAboutText);
            if(theUser.FirstLifeImage!=null)
                avatar.Attributes[AvatarAttributes.FIRST_LIFE_IMAGE_ID] = OSD.FromUUID(theUser.FirstLifeImage);
            if(theUser.GodLevel!=null)
                avatar.Attributes[AvatarAttributes.GOD_LEVEL] = OSD.FromInteger(theUser.GodLevel);
            if(theUser.HomeLocation!=null)
                avatar.Attributes[AvatarAttributes.HOME_POSITION] = OSD.FromVector3(theUser.HomeLocation);
            if(theUser.HomeLookAt!=null)
                avatar.Attributes[AvatarAttributes.HOME_LOOKAT] = OSD.FromVector3(theUser.HomeLookAt);
            if(theUser.HomeRegionID!=null)
                avatar.Attributes[AvatarAttributes.HOME_REGION_ID] = OSD.FromUUID(theUser.HomeRegionID);
            if (theUser.HomeRegionX != null)
            {
                avatar.Attributes[AvatarAttributes.HOME_REGION_X] = OSD.FromUInteger(theUser.HomeRegionX);
            }
            if (theUser.HomeRegionY != null)
            {
                avatar.Attributes[AvatarAttributes.HOME_REGION_Y] = OSD.FromUInteger(theUser.HomeRegionY);
            }
            if(theUser.ID!=null)
                avatar.Attributes[AvatarAttributes.AVATAR_ID] = OSD.FromUUID(theUser.ID);
            if(theUser.Image!=null)
                avatar.Attributes[AvatarAttributes.IMAGE_ID] = OSD.FromUUID(theUser.Image);
            if(theUser.LastLogin!=null)
                avatar.Attributes[AvatarAttributes.LAST_LOGIN_DATE] = OSD.FromInteger(theUser.LastLogin);
            if(theUser.Partner!=null)
                avatar.Attributes[AvatarAttributes.PARTNER_ID] = OSD.FromUUID(theUser.Partner);

            RegionProfileData rpd = CableBeachState.LoginService.RegionProfileService.RequestSimProfileData(theUser.CurrentAgent.Handle, m_UserConfig.GridServerURL,
                m_UserConfig.GridSendKey, m_UserConfig.GridRecvKey);
            if (rpd.UUID != UUID.Zero)
            {
                avatar.Attributes[AvatarAttributes.LAST_REGION_X] = OSD.FromUInteger(rpd.RegionLocX);
                avatar.Attributes[AvatarAttributes.LAST_REGION_Y] = OSD.FromUInteger(rpd.RegionLocY);
            }
            else 
            {
                avatar.Attributes[AvatarAttributes.LAST_REGION_X] = OSD.FromUInteger(theUser.HomeRegionX);
                avatar.Attributes[AvatarAttributes.LAST_REGION_Y] = OSD.FromUInteger(theUser.HomeRegionY);
            }

            //*/
            //OSD.FromString(theUser.PasswordHash);
            //OSD.FromString(theUser.PasswordSalt);
            //OSD.FromString(theUser.ProfileUrl);
            //OSD.FromString(theUser.UserAssetURI);
            if(theUser.UserFlags!=null)
                avatar.Attributes[AvatarAttributes.USER_FLAGS] = OSD.FromInteger(theUser.UserFlags);
            //OSD.FromString(theUser.UserInventoryURI);
            if(theUser.WantDoMask!=null)
                avatar.Attributes[AvatarAttributes.WANT_DO] = OSD.FromInteger(theUser.WantDoMask);
            //OSD.FromString(theUser.WebLoginKey);
            
            return avatar;
        }

    }
}