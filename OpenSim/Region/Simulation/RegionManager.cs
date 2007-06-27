using System;
using System.Collections.Generic;
using System.Text;
using OpenSim.Framework.Communications;
using OpenSim.Framework;
using OpenSim.Framework.Types;
using OpenSim.Framework.Servers;

namespace OpenSim.Region.Environment
{
    public class RegionManager //needs renaming , but first we need to rename the namespace
    {
        protected AuthenticateSessionsBase authenticateHandler;
        protected RegionCommsListener regionCommsHost;
        protected CommunicationsManager commsManager;
        protected List<Caps> capsHandlers = new List<Caps>();
        protected BaseHttpServer httpListener;

        protected Scenes.Scene m_Scene;

        public ParcelManager parcelManager;
        public EstateManager estateManager;

        public RegionManager()
        {

        }

    }
}
