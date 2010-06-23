using System;
using System.Reflection;
using System.Collections.Generic;
using System.Text;
using log4net;

namespace EstateManagementModule
{
    class EstateManagementStorageManager
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        
        protected IRegionEstateModification m_regionEstateModificationPlugin;

        public IRegionEstateModification RegionEstateModification
        {
            get { return m_regionEstateModificationPlugin; }
        }


        public EstateManagementStorageManager(string dllName, string connectionstring)
        {
            m_log.Info("[DATASTORE]: Attempting to load " + dllName);
            Assembly pluginAssembly = Assembly.LoadFrom(dllName);

            foreach (Type pluginType in pluginAssembly.GetTypes())
            {
                if (pluginType.IsPublic)
                {
                    Type typeInterface = pluginType.GetInterface("IRegionEstateModification", true);

                    if (typeInterface != null)
                    {
                        IRegionEstateModification plug =
                            (IRegionEstateModification)Activator.CreateInstance(pluginAssembly.GetType(pluginType.ToString()));
                        plug.Initialise(connectionstring);

                        m_regionEstateModificationPlugin = plug;

                        m_log.Info("[DATASTORE]: Added IRegionEstateModification Interface");
                    }
                }
            }

        }
    }
}
