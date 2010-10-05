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
//using OpenSim.Services.Interfaces;


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

    public abstract class EstateManagerBase : EstateManagementModule.IEstateRegionManager
    {
        protected Scene m_scene;
        protected List<Scene> m_scenes = new List<Scene>();


        public string maincommand;
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

        #region IEstateRegionManager unimplemented Members

        public abstract void HandleAddRegionBan(string module, string[] cmd);

        public abstract void HandleAddToRegionAccessList(string module, string[] cmd);

        public abstract void HandleRemoveFromRegionAccessList(string module, string[] cmd);

        public abstract void HandleRemoveRegionBan(string module, string[] cmd);

        public abstract void HandleSetCurrentEstateID(string module, string[] cmd);

        public abstract void HandleSetRegionPrivate(string module, string[] cmd);

        public abstract void HandleSetRegionPublic(string module, string[] cmd);

        public abstract void HandleShowEstateBanList(string module, string[] cmd);

        public abstract void HandleShowCurrentEstateID(string module, string[] cmd);

        public abstract void HandleShowEstateAccessList(string module, string[] cmd);

        public abstract void HandleShowEstateManagerList(string module, string[] cmd);

        public abstract void HandleAddEstateManager(string module, string[] cmd);

        public abstract void HandleRemoveEstateManager(string module, string[] cmd);

        #endregion
    }

    public class EstateManager : EstateManagerBase, IRegionModule, EstateManagementModule.IEstateRegionManager
    {
        //private Scene m_scene;
        //private List<Scene> m_scenes = new List<Scene>();

        private IEstateModule m_EstateModule; // TODO needed ??
        private EstateRegionHandler m_EstateRegionHandler = null;

        #region IRegionModule Members

        public void Initialise(OpenSim.Region.Framework.Scenes.Scene scene, Nini.Config.IConfigSource source)
        {
            m_scenes.Add(scene);
            m_scene = scene;

            scene.AddCommand(this, "estate", "estate <action> [<uuid>]", "Type \"estate help\" to view longer help", HandleEstateCommand);
            scene.AddCommand(this, "estatesim", "estatesim <action> [<uuid>]", "Type \"estatesim help\" to view longer help", HandleEstateSimCommand);

            //m_scene.AddCommand(this, "EstateAddBan", "EstateAddBan <uuid>", " Add user to regions estate banlist", HandleAddRegionBan);
            //m_scene.AddCommand(this, "EstateRemoveBan", "EstateRemoveBan <uuid>", " Remove user from regions estate banlist", HandleRemoveRegionBan);
            //m_scene.AddCommand(this, "EstatePublic", "EstatePublic", " Set region to public mode", HandleSetRegionPublic);


            //m_scene.AddCommand(this, """, """, " Set region to private mode", HandleSetRegionPrivate);
            //m_scene.AddCommand(this, "EstateAddAccess", "EstateAddAccess <uuid>", " Add user to regions estate access list", HandleAddToRegionAccessList);
            //m_scene.AddCommand(this, "EstateRemoveAccess", "EstateRemoveAccess <uuid>", " Remove user from regions estate access list", HandleRemoveFromRegionAccessList);

            //m_scene.AddCommand(this, "EstateAddManager", "EstateAddManager <uuid>", " Add user to regions estate managerlist", HandleAddEstateManager);


            //m_scene.AddCommand(this, "EstateRemoveManager", "EstateRemoveManager <uuid>", " Remove user from regions estate managerlist", HandleRemoveEstateManager);
            
            //m_scene.AddCommand(this, "EstateShowAccess", "EstateShowAccess", " Show estate access list", HandleShowEstateAccessList);

            //m_scene.AddCommand(this, "EstateShowBan", "EstateShowBan", " Show estate ban list", HandleShowEstateBanList);
            //m_scene.AddCommand(this, "EstateShowManagers", "EstateShowManagers", " Show estate manager list", HandleShowEstateManagerList);


            //m_scene.AddCommand(this, "EstateShowCurrent", "EstateShowCurrent", " Show estate id of currently selected region", HandleShowCurrentEstateID);
            
            m_scene.RegisterModuleInterface<IEstateRegionManager>(this);

            AddEstateRegionSettingsModificationCap(scene, this);
            m_EstateRegionHandler = new EstateRegionHandler(scene.RegionInfo, source);
        }


        public void HandleEstateCommand(string module, string[] cmdparams)
        {
            //foreach (Scene s in m_scenes)
            //{
            //    m_log.Info(s.RegionInfo.RegionName);
            //}

            if (cmdparams[0] == "estatesim")
            {
                //m_scene already set
            }
            else if (OpenSim.Framework.Console.MainConsole.Instance.ConsoleScene != null && OpenSim.Framework.Console.MainConsole.Instance.ConsoleScene is Scene)
            {
                m_scene = (Scene)OpenSim.Framework.Console.MainConsole.Instance.ConsoleScene;
                //OpenSim.Framework.
                //m_scene.GridService.
                //OpenSim.Framework.Console.MainConsole.Instance.RunCommand(
                //current scene to m_scene for time when console comman is processed. This is because current scene can change in between of processing
            }
            else
            {
                if (m_scenes.Count == 1)
                    m_scene = m_scenes[0];
                else
                {
                    m_log.ErrorFormat("[ESTATEMANAGER]: More than one scene in region. Set current scene with command \"change region <region name>\" \nSee regions with \"show regions\"");
                    return;
                }
            }

            try
            {
                bool showHelp = false;
                if (cmdparams.Length > 1)
                {
                    string command = cmdparams[1].ToLower(); //[0] == estate or estatesim
                    switch (command)
                    {
                        case "help":
                            showHelp = true;
                            break;
                        case "addban":
                            string[] cmd2 = new string[2];
                            cmd2[0] = "";
                            cmd2[1] = cmdparams[2];
                            HandleAddRegionBan("", cmd2);
                            break;
                        case "removeban":
                            string[] cmd3 = new string[2];
                            cmd3[0] = "";
                            cmd3[1] = cmdparams[2];
                            HandleRemoveRegionBan("", cmd3);
                            break;
                        case "setpublic":
                            string[] cmd4 = new string[2];
                            cmd4[0] = "";
                            //cmd4[1] = cmdparams[2];
                            HandleSetRegionPublic("", cmd4);
                            break;
                        case "setprivate":
                            string[] cmd5 = new string[2];
                            cmd5[0] = "";
                            //cmd5[1] = cmdparams[2];
                            HandleSetRegionPrivate("", cmd5);
                            break;
                        case "addaccess":
                            string[] cmd6 = new string[2];
                            cmd6[0] = "";
                            cmd6[1] = cmdparams[2];
                            HandleAddToRegionAccessList("", cmd6);
                            break;
                        case "removeaccess":
                            string[] cmd7 = new string[2];
                            cmd7[0] = "";
                            cmd7[1] = cmdparams[2];
                            HandleRemoveFromRegionAccessList("", cmd7);
                            break;
                        case "addmanager":
                            string[] cmd8 = new string[2];
                            cmd8[0] = "";
                            cmd8[1] = cmdparams[2];
                            HandleAddEstateManager("", cmd8);
                            break;
                        case "removemanager":
                            string[] cmd9 = new string[2];
                            cmd9[0] = "";
                            cmd9[1] = cmdparams[2];
                            HandleRemoveEstateManager("", cmd9);
                            break;
                        case "showaccess":
                            string[] cmd10 = new string[2];
                            cmd10[0] = "";
                            //cmd10[1] = cmdparams[2];
                            HandleShowEstateAccessList("", cmd10);
                            break;
                        case "showban":
                            string[] cmd11 = new string[2];
                            cmd11[0] = "";
                            //cmd11[1] = cmdparams[2];
                            HandleShowEstateBanList("", cmd11);
                            break;
                        case "showmanagers":
                            string[] cmd12 = new string[2];
                            cmd12[0] = "";
                            //cmd12[1] = cmdparams[2];
                            HandleShowEstateManagerList("", cmd12);
                            break;
                        default:
                            showHelp = true;
                            break;
                    }
                }
                else showHelp = true;

                if (showHelp)
                {
                    ShowHelp();
                }
            }
            catch (Exception e)
            {
                m_log.ErrorFormat("[ESTATEMANAGER]: Failed to execute estate command. Exception {0} was thrown.", e);
            }

            m_scene = null;
        }

        public void HandleEstateSimCommand(string module, string[] cmdparams)
        {
            if (cmdparams[1].ToLower() == "help")
                ShowSimHelp();
            else
            {
                foreach (Scene s in m_scenes)
                {
                    //m_log.Info(s.RegionInfo.RegionName);
                    Scene temp = m_scene;
                    m_scene = s;
                    HandleEstateCommand("", cmdparams);
                    m_scene = temp;
                }
            }
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
                
                if (method == "GetEstates")
                {
                    try
                    {
                        string resp = "";
                        Dictionary<int,string> estates = m_EstateRegionHandler.GetEstates();
                        foreach (KeyValuePair<int, string> kvp in estates)
                        {
                            resp += kvp.Key.ToString() + "=" + kvp.Value + "\n";
                        }
                        if (resp.Length > 1) { resp = resp.Substring(0, resp.Length - 1); } // remove last \n

                        KeyValuePair<uint, string> current = m_EstateRegionHandler.GetCurrentEstate();

                        respstring = "Success:\ncurrent estate:" + current.Key + "=" + current.Value + "\n  estates:\n" + resp;
                    }
                    catch (Exception e)
                    {
                        respstring = "Failure:Failed to fetch estates: " + e.Message;
                    }                        
                }
                
                if (method == "SetRegionEstate")
                {
                    try
                    {
                        int estateID = int.Parse(valuePairs[0].Split('=')[1]);
                        m_EstateRegionHandler.SetRegionsEstate(estateID);
                        respstring = "Success: Set regionId to : " + estateID.ToString();
                    }
                    catch (Exception e)
                    {
                        respstring = "Failure:Failed to set regionId: " + e.Message;
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
            get { return true; }
        }

        #endregion


        #region Command Handlers

        public void ShowHelp()
        {
            m_log.InfoFormat("[ESTATEMANAGER]: ");
            m_log.InfoFormat("");
            m_log.InfoFormat("Region Estate commands:");
            m_log.InfoFormat("");
            m_log.InfoFormat("estate ban <uuid>" + " Add user to regions estate banlist");
            m_log.InfoFormat("estate removeban <uuid>" + " Remove user from regions estate banlist");
            m_log.InfoFormat("estate setpublic" + " Set region to public mode");
            m_log.InfoFormat("estate setprivate" + " Set region to private mode");
            m_log.InfoFormat("estate addaccess <uuid>" + " Add user to regions estate access list");
            m_log.InfoFormat("estate removeaccess <uuid>" + " Remove user from regions estate access list");
            m_log.InfoFormat("estate addmanager <uuid>" + " Add user to regions estate banlist");
            m_log.InfoFormat("estate removemanager <uuid>" + " Remove user from regions estate banlist");
            m_log.InfoFormat("estate showaccess" + " Show estate access list");
            m_log.InfoFormat("estate showban" + " Show estate ban list");
            m_log.InfoFormat("estate showmanagers", " Show estate manager list");
            //m_log.InfoFormat("EstateShowCurrent" + " Show estate id of currently selected region");
        }

        public void ShowSimHelp() 
        {
            m_log.InfoFormat("");
            m_log.InfoFormat("Sim Estate commands:");
            m_log.InfoFormat("");

            m_log.InfoFormat("estatesim addban <uuid>" + " Add user to every regions estate banlist in the whole sim");
            m_log.InfoFormat("estatesim removeban <uuid>" + " Remove user from every regions estate banlist in the whole sim");
            m_log.InfoFormat("estatesim setpublic" + " Set every region in the sim to public mode");
            m_log.InfoFormat("estatesim setprivate" + " Set every region in the sim to private mode");
            m_log.InfoFormat("estatesim addacces <uuid>" + " Add user to every regions estate access list in the whole sim");
            m_log.InfoFormat("estatesim removeacces <uuid>" + " Remove user from every regions estate access list in the whole sim");
            m_log.InfoFormat("estatesim addmanager <uuid>" + " Add user to every regions estate manager list in the whole sim");
            m_log.InfoFormat("estatesim removemanager <uuid>" + " Remove user from every regions estate manager list in the whole sim");
            m_log.InfoFormat("estatesim showaccess" + " Show estate access lists of the sim");
            m_log.InfoFormat("estatesim showban" + " Show sim ban lists");
            m_log.InfoFormat("estatesim showmanagers" + " Show sim manager lists");
        }

        public override void HandleAddRegionBan(string module, string[] cmd)
        {
            //if (CheckArgumentLenght(cmd, 2, "Usage: EstateAddBan <uuid>"))
            if (CheckArgumentLenght(cmd, 2, "Usage: estate ban <uuid>"))
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
                m_log.InfoFormat("[ESTATEMANAGER]: Ban added");
            }
        }

        public override void HandleRemoveRegionBan(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveBan <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveBan(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
                m_log.InfoFormat("[ESTATEMANAGER]: Ban removed");
            }
        }

        public override void HandleAddEstateManager(string module, string[] cmd)
        {
            if (CheckArgumentLenght(cmd, 2, "Usage: EstateAddManager <uuid>"))
            {
                string uuidStr = cmd[1];
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.AddEstateManager(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
                m_log.InfoFormat("[ESTATEMANAGER]: Manager added");
            }
        }

        public override void HandleRemoveEstateManager(string module, string[] cmd)
        {
            if (CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveManager <uuid>"))
            {
                string uuidStr = cmd[1];
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveEstateManager(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
                m_log.InfoFormat("[ESTATEMANAGER]: Manager removed");
            }
        }
        
        public override void HandleSetRegionPublic(string module, string[] cmd)
        {
            m_scene.RegionInfo.EstateSettings.PublicAccess = true;
            m_scene.RegionInfo.EstateSettings.Save();
            m_log.InfoFormat("[ESTATEMANAGER]: Region set to public");
        }

        public override void HandleSetRegionPrivate(string module, string[] cmd)
        {
            m_scene.RegionInfo.EstateSettings.PublicAccess = false;
            m_scene.RegionInfo.EstateSettings.Save();
            m_log.InfoFormat("[ESTATEMANAGER]: Region set to private");
        }

        public override void HandleAddToRegionAccessList(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateAddToAccessList <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.AddEstateUser(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
            }
        }
        
        public override void HandleRemoveFromRegionAccessList(string module, string[] cmd)
        {
            if(CheckArgumentLenght(cmd, 2, "Usage: EstateRemoveFromAccessList <uuid>"))
            {
                UUID uuid = UUID.Parse(cmd[1]);
                m_scene.RegionInfo.EstateSettings.RemoveEstateUser(uuid);
                m_scene.RegionInfo.EstateSettings.Save();
            }
        }

        public override void HandleShowEstateAccessList(string module, string[] cmd)
        {
            m_log.InfoFormat("[ESTATEMANAGER]: Region: " + m_scene.RegionInfo.RegionName + " access list");
            foreach(UUID uuid in m_scene.RegionInfo.EstateSettings.EstateAccess)
            {
                m_log.InfoFormat("[ESTATEMANAGER]: " + uuid.ToString());
            }
        }
        
        public override void HandleShowEstateBanList(string module, string[] cmd)
        {
            m_log.InfoFormat("[ESTATEMANAGER]: Region: " + m_scene.RegionInfo.RegionName + " ban list");
            foreach(OpenSim.Framework.EstateBan eb in m_scene.RegionInfo.EstateSettings.EstateBans)
            {
                m_log.InfoFormat("[ESTATEMANAGER]: " + eb.BannedUserID.ToString());
            }
        }

        public override void HandleShowEstateManagerList(string module, string[] cmd)
        {
            m_log.InfoFormat("[ESTATEMANAGER]: Region: " + m_scene.RegionInfo.RegionName + " manager list");
            foreach (UUID uuid in m_scene.RegionInfo.EstateSettings.EstateManagers)
            {
                m_log.InfoFormat("[ESTATEMANAGER]: " + uuid.ToString());
            }
        }
        
        public override void HandleShowCurrentEstateID(string module, string[] cmd)
        {
            m_log.InfoFormat("[ESTATEMANAGER]: " + m_scene.RegionInfo.EstateSettings.EstateID.ToString());
        }

        public override void HandleSetCurrentEstateID(string module, string[] cmd)
        {
            if (CheckArgumentLenght(cmd, 2, "Usage: EstateSetRegionsEstateID <uint>"))
            {
                // Not implemented
                // Can't access scene's m_storageManager and EstateDataStore, so cannot change and load other Estate
            }            
        }

        #endregion


    }


    //public class EstateSimManager : EstateManagerBase, IRegionModule, IHandleEstateManagementException
    //{
    //    //private List<Scene> m_scenes = new List<Scene>();
    //    //private List<IEstateRegionManager> m_estateManagers = new List<IEstateRegionManager>();
    //    private System.Collections.Generic.Dictionary<string, IEstateRegionManager> m_estateManagers = new System.Collections.Generic.Dictionary<string, IEstateRegionManager>();

    //    #region IHandleEstateManagementException Members
    //    public void HandleException(string region)
    //    {
    //        m_log.Info("Could not perform action on region: " + region);
    //    }
    //    #endregion


    //    #region IRegionModule Members

    //    public void Initialise(Scene scene, Nini.Config.IConfigSource source)
    //    {

    //        scene.AddCommand(this, "estatesim", "estatesim <action> [<uuid>]", "Type \"estatesim help\" to view longer help", HandleEstateCommand);


    //        //scene.AddCommand(this, "EstateSimAddBan", "EstateSimAddBan <uuid>",
    //        //    "Add user to every regions estate banlist in the whole sim", HandleAddSimBan);
    //        //scene.AddCommand(this, "EstateSimRemoveBan", "EstateSimRemoveBan <uuid>",
    //        //    "Remove user from every regions estate banlist in the whole sim", HandleRemoveSimBan);
    //        //scene.AddCommand(this, "EstateSimToPublic", "EstateSimToPublic",
    //        //    "Set every region in the sim to public mode", HandleSetSimPublic);
    //        //scene.AddCommand(this, "EstateSimToPrivate", "EstateSimToPrivate",
    //        //    "Set every region in the sim to private mode", HandleSetSimPrivate);
    //        //scene.AddCommand(this, "EstateSimAddAccess", "EstateSimAddAccess <uuid>",
    //        //    "Add user to every regions estate access list in the whole sim", HandleAddToSimAccessList);
    //        //scene.AddCommand(this, "EstateSimRemoveAccess", "EstateSimRemoveAccess <uuid>",
    //        //    "Remove user from every regions estate access list in the whole sim", HandleRemoveFromSimAccessList);

    //        //scene.AddCommand(this, "EstateSimAddManager", "EstateSimAddManager <uuid>",
    //        //    "Add user to every regions estate manager list in the whole sim", HandleAddToSimManagerList);
    //        //scene.AddCommand(this, "EstateSimRemoveManager", "EstateSimRemoveManager <uuid>",
    //        //    "Remove user from every regions estate manager list in the whole sim", HandleRemoveFromSimManagerList);

    //        //scene.AddCommand(this, "EstateSimShowAccess", "EstateSimShowAccess", "Show estate access lists of the sim", HandleShowEstateAccessList);
    //        //scene.AddCommand(this, "EstateSimShowBan", "EstateSimShowBan", "Show sim ban lists", HandleShowSimBanLists);
    //        //scene.AddCommand(this, "EstateSimShowManagers", "EstateSimShowManagers", "Show sim manager lists", HandleShowSimManagerLists);

    //        //scene.AddCommand(this, "EstateHELP", "EstateHELP", "Shows just estate commands help", HandleEstateHELP);
    //        m_scenes.Add(scene);
    //    }

    //    public void PostInitialise()
    //    {
    //        foreach (Scene scene in m_scenes)
    //        {
    //            IEstateRegionManager estateInteraface = scene.RequestModuleInterface<IEstateRegionManager>();
    //            //scene.RegionInfo.RegionName
    //            IEstateRegionManager proxy = EstateTransparentExceptionHandler.CreateProxy<IEstateRegionManager>(estateInteraface, this, scene.RegionInfo.RegionName);
                
    //            //m_estateManagers.Add(estateInteraface);
    //            //m_estateManagers.Add(proxy);
    //            m_estateManagers.Add(scene.RegionInfo.RegionName, proxy);
    //        }
    //    }

    //    public void Close()
    //    {
    //    }

    //    public string Name
    //    {
    //        get { return "EstateSimManager"; }
    //    }

    //    public bool IsSharedModule
    //    {
    //        get { return true; }
    //    }

    //    #endregion

    //    #region Command Handlers

    //    public override void HandleAddRegionBan(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            //m.HandleAddRegionBan(module, cmd);
    //            kvp.Value.HandleAddRegionBan(module, cmd);
    //        }
    //    }

    //    public override void HandleRemoveRegionBan(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleRemoveRegionBan(module, cmd);
    //        }
    //    }

    //    public override void HandleSetRegionPublic(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleSetRegionPublic(module, cmd);
    //        }
    //    }

    //    public override void HandleSetRegionPrivate(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleSetRegionPrivate(module, cmd);
    //        }
    //    }

    //    public override void HandleAddToRegionAccessList(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleAddToRegionAccessList(module, cmd);
    //        }
    //    }

    //    public override void HandleRemoveFromRegionAccessList(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleRemoveFromRegionAccessList(module, cmd);
    //        }
    //    }

    //    public override void HandleShowEstateAccessList(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleShowEstateAccessList(module, cmd);
    //        }
    //    }

    //    public override void HandleShowEstateBanList(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleShowEstateBanList(module, cmd);
    //        }
    //    }

    //    public override void HandleShowEstateManagerList(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            m_log.Info("Should be region " + kvp.Key);
    //            kvp.Value.HandleShowEstateManagerList(module, cmd);
    //        }
    //    }

    //    public override void HandleAddEstateManager(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleAddEstateManager(module, cmd);
    //        }
    //    }

    //    public override void HandleRemoveEstateManager(string module, string[] cmd)
    //    {
    //        //foreach (IEstateRegionManager m in m_estateManagers)
    //        foreach (System.Collections.Generic.KeyValuePair<string, IEstateRegionManager> kvp in m_estateManagers)
    //        {
    //            kvp.Value.HandleRemoveEstateManager(module, cmd);
    //        }
    //    }

    //    //private void HandleEstateHELP(string module, string[] cmd)
    //    public override void ShowHelp()
    //    {
    //        //m_log.InfoFormat("[ESTATEMANAGER]: ");
    //        //m_log.InfoFormat("");
    //        //m_log.InfoFormat("Region Estate commands:");
    //        //m_log.InfoFormat("");
    //        //m_log.InfoFormat("EstateAddBan <uuid>" + " Add user to regions estate banlist");
    //        //m_log.InfoFormat("EstateRemoveBan <uuid>" + " Remove user from regions estate banlist");
    //        //m_log.InfoFormat("EstatePublic" + " Set region to public mode");
    //        //m_log.InfoFormat("EstatePrivate" + " Set region to private mode");
    //        //m_log.InfoFormat("EstateAddAccess <uuid>" + " Add user to regions estate access list");
    //        //m_log.InfoFormat("EstateRemoveAccess <uuid>" + " Remove user from regions estate access list");
    //        //m_log.InfoFormat("EstateAddManager <uuid>" + " Add user to regions estate banlist");
    //        //m_log.InfoFormat("EstateRemoveManager <uuid>" + " Remove user from regions estate banlist");
    //        //m_log.InfoFormat("EstateShowAccess" + " Show estate access list");
    //        //m_log.InfoFormat("EstateShowBan" + " Show estate ban list");
    //        //m_log.InfoFormat("EstateShowManagers <uuid>", " Show estate manager list");
    //        //m_log.InfoFormat("EstateShowCurrent" + " Show estate id of currently selected region");

    //        m_log.InfoFormat("");
    //        m_log.InfoFormat("Sim Estate commands:");
    //        m_log.InfoFormat("");

    //        m_log.InfoFormat("estatesim addban <uuid>" + " Add user to every regions estate banlist in the whole sim");
    //        m_log.InfoFormat("estatesim removeban <uuid>" + " Remove user from every regions estate banlist in the whole sim");
    //        m_log.InfoFormat("estatesim setpublic" + " Set every region in the sim to public mode");
    //        m_log.InfoFormat("estatesim setprivate" + " Set every region in the sim to private mode");
    //        m_log.InfoFormat("estatesim addacces <uuid>" + " Add user to every regions estate access list in the whole sim");
    //        m_log.InfoFormat("estatesim removeacces <uuid>" + " Remove user from every regions estate access list in the whole sim");
    //        m_log.InfoFormat("estatesim addmanager <uuid>" + " Add user to every regions estate manager list in the whole sim");
    //        m_log.InfoFormat("estatesim removemanager <uuid>" + " Remove user from every regions estate manager list in the whole sim");
    //        m_log.InfoFormat("estatesim showaccess" + " Show estate access lists of the sim");
    //        m_log.InfoFormat("estatesim showban" + " Show sim ban lists");
    //        m_log.InfoFormat("estatesim showmanagers" + " Show sim manager lists");



    //        //m_log.InfoFormat("EstateSimAddBan <uuid>" + " Add user to every regions estate banlist in the whole sim");
    //        //m_log.InfoFormat("EstateSimRemoveBan <uuid>" + " Remove user from every regions estate banlist in the whole sim");
    //        //m_log.InfoFormat("EstateSimToPublic" + " Set every region in the sim to public mode");
    //        //m_log.InfoFormat("EstateSimToPrivate" + " Set every region in the sim to private mode");
    //        //m_log.InfoFormat("EstateSimAddAccess <uuid>" + " Add user to every regions estate access list in the whole sim");
    //        //m_log.InfoFormat("EstateSimRemoveAccess <uuid>" + " Remove user from every regions estate access list in the whole sim");

    //        //m_log.InfoFormat("EstateSimAddManager <uuid>" + " Add user to every regions estate manager list in the whole sim");
    //        //m_log.InfoFormat("EstateSimRemoveManager <uuid>" + " Remove user from every regions estate manager list in the whole sim");

    //        //m_log.InfoFormat("EstateSimShowAccess" + " Show estate access lists of the sim");
    //        //m_log.InfoFormat("EstateSimShowBan" + " Show sim ban lists");
    //        //m_log.InfoFormat("EstateSimShowManagers" + " Show sim manager lists");
    //    }

    //    public override void HandleSetCurrentEstateID(string module, string[] cmd)
    //    {}
    //    public override void HandleShowCurrentEstateID(string module, string[] cmd)
    //    {}
    //    #endregion
    //}

}
