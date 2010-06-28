using System;
using System.Collections.Generic;
using System.Reflection;
//using System.Linq;
using System.Text;
using OpenSim.Framework;
using OpenSim.Region.Framework;
using Nini.Config;
using OpenMetaverse;
using log4net;

namespace EstateManagementModule
{
    /// <summary>
    /// class for handling requests for changing regions estate, 
    /// will probably support only mysql for now,(a bit hackish approach)
    /// </summary>
    class EstateRegionHandler
    {
        RegionInfo m_RegionInfo;
        StorageManager m_storageManager;
        string m_storageDll;
        string m_estateStorageManagementDll;
        string m_storageConnString;
        string m_estateConnString;
        bool m_enabled = false;
        protected static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        EstateManagementStorageManager m_EstateManagementStorageManager;
        //IRegionEstateModification m_RegionEstateModification;

        public EstateRegionHandler(RegionInfo regionInfo, Nini.Config.IConfigSource source)
        { 
            m_RegionInfo = regionInfo;

            // Since there's no way to access storage manager from region module -> creating own storagemanager
            IConfig startupConfig = source.Configs["Startup"];
            if (startupConfig != null)
            {
                //m_storageDll = startupConfig.GetString("storage_plugin", "OpenSim.Data.MySQL.dll");
                m_storageDll = startupConfig.GetString("storage_plugin", "");


                m_storageConnString = startupConfig.GetString("storage_connection_string", "");
                m_estateConnString = startupConfig.GetString("estate_connection_string", "");
                if (m_storageConnString != "")
                {
                    if (m_estateConnString == "")
                    {
                        m_estateConnString = m_storageConnString;
                    }
                    try
                    {
                        m_estateStorageManagementDll = ContstructEstateStorageManagementDllName(m_storageDll);
                        //m_EstateManagementStorageManager = new EstateManagementStorageManager(m_storageDll, m_estateConnString);
                        m_EstateManagementStorageManager = new EstateManagementStorageManager(m_estateStorageManagementDll, m_estateConnString);
                        m_storageManager = new StorageManager(m_storageDll, m_storageConnString, m_estateConnString);
                        m_enabled = true;
                    }
                    catch (Exception e) // only supporting mysql for now, other than OpenSim.Data.MySQL.dll will fail
                    {
                        m_log.ErrorFormat("[ESTATEMANAGEMENTMODULE]: Failed to create estate management handler: {0}",
                            e.Message);                    

                        m_enabled = false;
                    }
                }
            }
        }

        private string ContstructEstateStorageManagementDllName(string storageDll)
        {
            string[] parts = storageDll.Split('.');
            return "EstateManagement." + string.Join(".", parts, 1, 3);
        }

        //public bool SetRegionsEstate(UUID uuid)
        public bool SetRegionsEstate(int estateID)
        {
            try
            {
                if(m_enabled)
                {
                    //first set new estate id to region in db
                    m_EstateManagementStorageManager.RegionEstateModification.SetRegionsEstate(m_RegionInfo.RegionID, estateID);
                    //then load the new estatesettings
                    m_RegionInfo.EstateSettings = m_storageManager.EstateDataStore.LoadEstateSettings(m_RegionInfo.RegionID);
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ESTATEMANAGER]: Error setting regions estate: {0}, {1}", e.Message, e.StackTrace);
                return false;
            }
        }

        public Dictionary<int, string> GetEstates()
        {
            try
            {
                if (m_enabled)
                {
                    return m_EstateManagementStorageManager.RegionEstateModification.GetEstates();
                }
                else return new Dictionary<int, string>();
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ESTATEMANAGER]: Error fetching estates: {0}, {1}", e.Message, e.StackTrace);
                return new Dictionary<int,string>();
            }
        }

        public KeyValuePair<uint, string> GetCurrentEstate()
        {
            EstateSettings estateSettings = m_storageManager.EstateDataStore.LoadEstateSettings(this.m_RegionInfo.RegionID);
            return new KeyValuePair<uint, string>(estateSettings.EstateID, estateSettings.EstateName);
        }

    }
}
