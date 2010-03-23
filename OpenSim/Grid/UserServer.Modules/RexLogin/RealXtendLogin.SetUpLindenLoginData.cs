using System;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using CableBeachMessages;
using OpenSim.Grid.UserServer.Modules.RexLogin;
using ModularRex.RexNetwork.RexLogin;

namespace OpenSim.Grid.UserServer.Modules.RexLogin
{
    partial class RealXtendLogin : RexLoginModule
    {
        LindenLoginData SetUpLindenLoginData(OpenSim.Grid.UserServer.Modules.RexLogin.Avatar avatar, string account,
            string welcomeMessage, UUID sessionID) 
        {
            LindenLoginData response = new LindenLoginData();

            response.AgentID = avatar.ID;
            response.BuddyList = LindenLoginHelper.GetBuddyList(avatar.ID);
            LindenLoginHelper.SetClassifiedCategories(ref response);
            response.FirstName = avatar.GetAttribute(AvatarAttributes.FIRST_NAME).AsString() + " " + avatar.GetAttribute(AvatarAttributes.LAST_NAME).AsString();
            avatar.Attributes[AvatarAttributes.FIRST_NAME] = OSD.FromString(response.FirstName);
            response.HomeLookAt = avatar.GetAttribute(AvatarAttributes.HOME_LOOKAT).AsVector3();
            response.HomePosition = avatar.GetAttribute(AvatarAttributes.HOME_POSITION).AsVector3();
            response.HomeRegionX = avatar.GetAttribute(AvatarAttributes.HOME_REGION_X).AsUInteger();
            response.HomeRegionY = avatar.GetAttribute(AvatarAttributes.HOME_REGION_Y).AsUInteger();
            response.LastName = "<" + account + ">";
            avatar.Attributes[AvatarAttributes.LAST_NAME] = OSD.FromString(response.LastName);
            response.Login = true;
            response.Message = welcomeMessage;
            response.SessionID = sessionID;
            response.SecureSessionID = UUID.Random();

            return response;
        }

    }
}