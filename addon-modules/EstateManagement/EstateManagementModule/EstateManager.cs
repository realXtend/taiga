using System;
using System.Reflection;
using System.Collections.Generic;
using log4net;
using System.Text;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;

namespace EstateManagementModule
{
    public class EstateManager : IRegionModule
    {
        private Scene m_scene;
        private IEstateModule m_EstateModule; // TODO needed ??

        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        #region IRegionModule Members

        public void Initialise(OpenSim.Region.Framework.Scenes.Scene scene, Nini.Config.IConfigSource source)
        {
            m_scene = scene;
            m_scene.AddCommand(this, "EstateAddBan", "EstateAddBan <uuid>", "Add user to regions estate banlist", HandleAddRegionBan);
            m_scene.AddCommand(this, "EstateRemoveBan", "EstateRemoveBan <uuid>", "Remove user from regions estate banlist", HandleRemoveRegionBan);
            m_scene.AddCommand(this, "EstateSetToPublic", "EstateSetToPublic", "Set region to public mode", HandleSetRegionPublic);
            m_scene.AddCommand(this, "EstateSetToPrivate", "EstateSetToPrivate", "Set region to private mode", HandleSetRegionPrivate);
            m_scene.AddCommand(this, "EstateAddToAccessList", "EstateAddToAccessList <uuid>", "Add user to regions estate access list", HandleAddToRegionAccessList);
            m_scene.AddCommand(this, "EstateRemoveFromAccessList", "EstateRemoveFromAccessList <uuid>", "Remove user from regions estate access list", HandleRemoveFromRegionAccessList);

            m_scene.AddCommand(this, "EstateShowAccessList", "EstateShowAccessList", "Show estate access list", HandleShowEstateAccessList);
            m_scene.AddCommand(this, "EstateShowBanList", "EstateShowBanList", "Show estate access list", HandleShowBanAccessList);
            m_scene.AddCommand(this, "EstateShowCurrentEstateID", "EstateShowCurrentEstateID", "Show estate id of currently selected region", HandleShowCurrentEstateID);
            //m_scene.AddCommand(this, "EstateSetRegionsEstateID", "EstateSetRegionsEstateID <uint>", "Set estate id of currently selected region", HandleSetCurrentEstateID);
        }

        public void PostInitialise()
        {
            m_EstateModule = m_scene.RequestModuleInterface<IEstateModule>();
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "EstateManager"; }
        }

        public bool IsSharedModule
        {
            get { return false; }
        }

        #endregion

        #region Command Handlers

        private void HandleAddRegionBan(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateAddBan <uuid>"))
            {
                string uuidStr = cmd[1];
                UUID uuid = UUID.Parse(cmd[1]);                
                OpenSim.Framework.EstateBan eb = new OpenSim.Framework.EstateBan();
                eb.BannedUserID = uuid;
                eb.EstateID = m_scene.RegionInfo.EstateSettings.EstateID;
                eb.BannedHostAddress = "0.0.0.0";
                eb.BannedHostIPMask = "0.0.0.0";
                m_scene.RegionInfo.EstateSettings.AddBan(eb);
                m_scene.RegionInfo.EstateSettings.Save();
                m_log.InfoFormat("Ban added");
            }            
        }

        private void HandleRemoveRegionBan(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveBan <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveBan(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
                m_log.InfoFormat("Ban removed");
            }
        }

        private void HandleSetRegionPublic(string module, string[] cmd)
        {
            m_scene.RegionInfo.EstateSettings.PublicAccess = true;
            m_scene.RegionInfo.EstateSettings.Save();
        }

        private void HandleSetRegionPrivate(string module, string[] cmd)
        {
            m_scene.RegionInfo.EstateSettings.PublicAccess = false;
            m_scene.RegionInfo.EstateSettings.Save();
        }

        private void HandleAddToRegionAccessList(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateAddToAccessList <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.AddEstateUser(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
            }
        }
        private void HandleRemoveFromRegionAccessList(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveFromAccessList <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveEstateUser(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
            }
        }

        private void HandleShowEstateAccessList(string module, string[] cmd)
        {
            foreach(UUID uuid in m_scene.RegionInfo.EstateSettings.EstateAccess)
            {
                m_log.InfoFormat(uuid.ToString());
            }
        }
        private void HandleShowBanAccessList(string module, string[] cmd)
        {
            foreach(OpenSim.Framework.EstateBan eb in m_scene.RegionInfo.EstateSettings.EstateBans)
            {
                m_log.InfoFormat(eb.BannedUserID.ToString());
            }
        }
        private void HandleShowCurrentEstateID(string module, string[] cmd)
        {
            m_log.InfoFormat(m_scene.RegionInfo.EstateSettings.EstateID.ToString());
        }

        private void HandleSetCurrentEstateID(string module, string[] cmd)
        {
            if (CheckArgumentLenght(cmd, 2, "Usage: EstateSetRegionsEstateID <uint>"))
            {
                // Not implemented
                // Can't access scene's m_storageManager and EstateDataStore, so cannot change and load other Estate
            }            
        }

        #endregion

        private bool CheckArgumentLenght(string[] cmd, int length, string usage)
        {
            if (cmd.Length < length)
            {
                m_log.InfoFormat(usage);
                return false;
            }
            return true;
        }
    }
}
