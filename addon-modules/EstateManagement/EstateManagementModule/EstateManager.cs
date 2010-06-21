using System;
using System.Reflection;
using System.Collections.Generic;
using log4net;
using System.Text;
using System.IO;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
//using Caps = OpenSim.Framework.Capabilities.Caps;
using OpenSim.Framework.Capabilities;


namespace EstateManagementModule
{
    #region HttpRequest handler code

    public delegate byte[] HttpRequestCallback(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse);

    public class StreamHandler : BaseStreamHandler
    {
        private HttpRequestCallback m_callback;

        public override string ContentType { get { return null; } }

        public StreamHandler(string httpMethod, string path, HttpRequestCallback callback) :
            base(httpMethod, path)
        {
            m_callback = callback;
        }

        public override byte[] Handle(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            return m_callback(path, request, httpRequest, httpResponse);
        }
    }
    #endregion

    public class EstateManagerBase
    {
        protected static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        protected bool CheckArgumentLenght(string[] cmd, int length, string usage)
        {
            if (cmd.Length < length)
            {
                m_log.InfoFormat(usage);
                return false;
            }
            return true;
        }    
    }

    public class EstateManager : EstateManagerBase, IRegionModule, EstateManagementModule.IEstateRegionManager
    {
        private Scene m_scene;
        private IEstateModule m_EstateModule; // TODO needed ??


        #region IRegionModule Members

        public void Initialise(OpenSim.Region.Framework.Scenes.Scene scene, Nini.Config.IConfigSource source)
        {
            m_scene = scene;
            m_scene.AddCommand(this, "EstateAddBan", "EstateAddBan <uuid>", "Add user to regions estate banlist", HandleAddRegionBan);
            m_scene.AddCommand(this, "EstateRemoveBan", "EstateRemoveBan <uuid>", "Remove user from regions estate banlist", HandleRemoveRegionBan);
            m_scene.AddCommand(this, "EstatePublic", "EstatePublic", "Set region to public mode", HandleSetRegionPublic);
            m_scene.AddCommand(this, "EstatePrivate", "EstatePrivate", "Set region to private mode", HandleSetRegionPrivate);
            m_scene.AddCommand(this, "EstateAddAccess", "EstateAddAccess <uuid>", "Add user to regions estate access list", HandleAddToRegionAccessList);
            m_scene.AddCommand(this, "EstateRemoveAccess", "EstateRemoveAccess <uuid>", "Remove user from regions estate access list", HandleRemoveFromRegionAccessList);

            m_scene.AddCommand(this, "EstateShowAccess", "EstateShowAccess", "Show estate access list", HandleShowEstateAccessList);
            m_scene.AddCommand(this, "EstateShowBan", "EstateShowBan", "Show estate ban list", HandleShowEstateBanList);
            m_scene.AddCommand(this, "EstateShowCurrent", "EstateShowCurrent", "Show estate id of currently selected region", HandleShowCurrentEstateID);
            m_scene.RegisterModuleInterface<IEstateRegionManager>(this);

            AddEstateRegionSettingsModificationCap(scene, this);
        }

        private void AddEstateRegionSettingsModificationCap(Scene scene, EstateManager estateManager)
        {
            try
            {
                scene.EventManager.OnRegisterCaps += this.RegisterCaps;
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ESTATEMANAGER]: Error starting estate manager: {0}, {1}", e.Message, e.StackTrace);
            }
        }

        public void RegisterCaps(UUID agentID, OpenSim.Framework.Capabilities.Caps caps)
        {
            if (CheckRights(agentID))
            {
                UUID capID = UUID.Random();
                m_log.InfoFormat("[ESTATEMANAGER]: Creating capability: /CAPS/{0}", capID);
                caps.RegisterHandler("EstateRegionSettingsModification", new StreamHandler("POST", "/CAPS/" + capID, ProcessEstateRegionSettingsModificationRequest));
            }
        }

        private byte[] ProcessEstateRegionSettingsModificationRequest(string path, Stream request, OSHttpRequest httpRequest, OSHttpResponse httpResponse)
        {
            byte[] data = httpRequest.GetBody();
            m_log.Info("[ESTATEMANAGER]: Processing EstateRegionSettingsModification packet");

            string datastring = System.Text.ASCIIEncoding.ASCII.GetString(data);
            m_log.Debug("[ESTATEMANAGER]: " + datastring);
            string respstring = "";

            if (datastring != "")
            {
                string[] valuePairs = datastring.Split('&');
                string method = valuePairs[0].Split('=')[0];
                if (method == "PublicAccess")
                {
                    try
                    {
                        Boolean publicAccess = Convert.ToBoolean(valuePairs[0].Split('=')[1]);
                        m_scene.RegionInfo.EstateSettings.PublicAccess = publicAccess;
                        m_scene.RegionInfo.EstateSettings.Save();
                        m_log.Info("[ESTATEMANAGER]: PublicAccess set to: " + publicAccess.ToString());
                        respstring = "Success:PublicAccess=" + publicAccess.ToString();
                    }
                    catch (Exception e)
                    {
                        m_log.Warn("[ESTATEMANAGER]: Failed to set PublicAccess: " + e.Message);
                        respstring = "Failure:Failed to set PublicAccess: " + e.Message;
                    }                    
                }
            }
            
            System.Text.ASCIIEncoding encoding = new System.Text.ASCIIEncoding();
            return encoding.GetBytes(respstring);
        }

        private bool CheckRights(UUID agentID)
        {
            if (agentID == m_scene.RegionInfo.EstateSettings.EstateOwner)
                return true;
            UUID[] managers = m_scene.RegionInfo.EstateSettings.EstateManagers;
            foreach (UUID id in managers)
            {
                if (id == agentID)
                    return true;
            }
            return false;
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

        public void HandleAddRegionBan(string module, string[] cmd)
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

        public void HandleRemoveRegionBan(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveBan <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveBan(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
                m_log.InfoFormat("Ban removed");
            }
        }

        public void HandleSetRegionPublic(string module, string[] cmd)
        {
            m_scene.RegionInfo.EstateSettings.PublicAccess = true;
            m_scene.RegionInfo.EstateSettings.Save();
        }

        public void HandleSetRegionPrivate(string module, string[] cmd)
        {
            m_scene.RegionInfo.EstateSettings.PublicAccess = false;
            m_scene.RegionInfo.EstateSettings.Save();
        }

        public void HandleAddToRegionAccessList(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateAddToAccessList <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.AddEstateUser(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
            }
        }
        
        public void HandleRemoveFromRegionAccessList(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveFromAccessList <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveEstateUser(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
            }
        }

        public void HandleShowEstateAccessList(string module, string[] cmd)
        {
            m_log.InfoFormat("Region: " + m_scene.RegionInfo.RegionName + " access list");
            foreach(UUID uuid in m_scene.RegionInfo.EstateSettings.EstateAccess)
            {
                m_log.InfoFormat(uuid.ToString());
            }
        }
        
        public void HandleShowEstateBanList(string module, string[] cmd)
        {
            m_log.InfoFormat("Region: " + m_scene.RegionInfo.RegionName + " ban list");
            foreach(OpenSim.Framework.EstateBan eb in m_scene.RegionInfo.EstateSettings.EstateBans)
            {
                m_log.InfoFormat(eb.BannedUserID.ToString());
            }
        }
        
        public void HandleShowCurrentEstateID(string module, string[] cmd)
        {
            m_log.InfoFormat(m_scene.RegionInfo.EstateSettings.EstateID.ToString());
        }

        public void HandleSetCurrentEstateID(string module, string[] cmd)
        {
            if (CheckArgumentLenght(cmd, 2, "Usage: EstateSetRegionsEstateID <uint>"))
            {
                // Not implemented
                // Can't access scene's m_storageManager and EstateDataStore, so cannot change and load other Estate
            }            
        }

        #endregion
    }

    public class EstateSimManager : EstateManagerBase, IRegionModule
    {
        private List<Scene> m_scenes = new List<Scene>();
        private List<IEstateRegionManager> m_estateManagers = new List<IEstateRegionManager>();

        #region IRegionModule Members

        public void Initialise(Scene scene, Nini.Config.IConfigSource source)
        {
            scene.AddCommand(this, "EstateSimAddBan", "EstateSimAddBan <uuid>", 
                "Add user to every regions estate banlist in the whole sim", HandleAddSimBan);
            scene.AddCommand(this, "EstateSimRemoveBan", "EstateSimRemoveBan <uuid>", 
                "Remove user from every regions estate banlist in the whole sim", HandleRemoveSimBan);
            scene.AddCommand(this, "EstateSimToPublic", "EstateSimToPublic", 
                "Set every region in the sim to public mode", HandleSetSimPublic);
            scene.AddCommand(this, "EstateSimToPrivate", "EstateSimToPrivate", 
                "Set every region in the sim to private mode", HandleSetSimPrivate);
            scene.AddCommand(this, "EstateSimAddAccess", "EstateSimAddAccess <uuid>", 
                "Add user to every regions estate access list in the whole sim", HandleAddToSimAccessList);
            scene.AddCommand(this, "EstateSimRemoveAccess", "EstateSimRemoveAccess <uuid>", 
                "Remove user from every regions estate access list in the whole sim", HandleRemoveFromSimAccessList);
            scene.AddCommand(this, "EstateSimShowAccess", "EstateSimShowAccess", "Show estate access lists of the sim", HandleShowEstateAccessList);
            scene.AddCommand(this, "EstateSimShowBan", "EstateSimShowBan", "Show sim ban lists", HandleShowSimBanLists);

            scene.AddCommand(this, "EstateHELP", "EstateHELP", "Shows just estate commands help", HandleEstateHELP);
            m_scenes.Add(scene);

        }

        public void PostInitialise()
        {
            foreach (Scene scene in m_scenes)
            {
                IEstateRegionManager estateInteraface = scene.RequestModuleInterface<IEstateRegionManager>();
                m_estateManagers.Add(estateInteraface);
            }
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "EstateSimManager"; }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }

        #endregion

        #region Command Handlers

        private void HandleAddSimBan(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleAddRegionBan(module, cmd);
            }
        }

        private void HandleRemoveSimBan(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleRemoveRegionBan(module, cmd);
            }
        }

        private void HandleSetSimPublic(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleSetRegionPublic(module, cmd);
            }
        }

        private void HandleSetSimPrivate(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleSetRegionPrivate(module, cmd);
            }
        }

        private void HandleAddToSimAccessList(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleAddToRegionAccessList(module, cmd);
            }
        }

        private void HandleRemoveFromSimAccessList(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleRemoveFromRegionAccessList(module, cmd);
            }            
        }

        private void HandleShowEstateAccessList(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleShowEstateAccessList(module, cmd);
            }            
        }

        private void HandleShowSimBanLists(string module, string[] cmd)
        {
            foreach (IEstateRegionManager m in m_estateManagers)
            {
                m.HandleShowEstateBanList(module, cmd);
            }            
        }

        private void HandleEstateHELP(string module, string[] cmd)
        {
            m_log.InfoFormat("");
            m_log.InfoFormat("Region Estate commands:");
            m_log.InfoFormat("");
            m_log.InfoFormat("EstateAddBan <uuid>" + "Add user to regions estate banlist");
            m_log.InfoFormat("EstateRemoveBan <uuid>" + "Remove user from regions estate banlist");
            m_log.InfoFormat("EstatePublic" + "Set region to public mode");
            m_log.InfoFormat("EstatePrivate" + "Set region to private mode");
            m_log.InfoFormat("EstateAddAccess <uuid>" + "Add user to regions estate access list");
            m_log.InfoFormat("EstateRemoveAccess <uuid>" + "Remove user from regions estate access list");
            m_log.InfoFormat("EstateShowAccess" + "Show estate access list");
            m_log.InfoFormat("EstateShowBan" + "Show estate ban list");
            m_log.InfoFormat("EstateShowCurrent" + "Show estate id of currently selected region");
            m_log.InfoFormat("");
            m_log.InfoFormat("Sim Estate commands:");
            m_log.InfoFormat("");
            m_log.InfoFormat("EstateSimAddBan <uuid>" + " Add user to every regions estate banlist in the whole sim");
            m_log.InfoFormat("EstateSimRemoveBan <uuid>" + " Remove user from every regions estate banlist in the whole sim");
            m_log.InfoFormat("EstateSimToPublic" + " Set every region in the sim to public mode");
            m_log.InfoFormat("EstateSimToPrivate" + " Set every region in the sim to private mode");
            m_log.InfoFormat("EstateSimAddAccess <uuid>" + " Add user to every regions estate access list in the whole sim");
            m_log.InfoFormat("EstateSimRemoveAccess <uuid>" + " Remove user from every regions estate access list in the whole sim");
            m_log.InfoFormat("EstateSimShowAccess" + " Show estate access lists of the sim");
            m_log.InfoFormat("EstateSimShowBan" + " Show sim ban lists");
        }

        #endregion
    }

}
