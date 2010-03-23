using System;
using System.Collections.Generic;
//using System.Linq;
using System.Text;
//using ExtensionLoader;
using WebDAVSharp;
using WebDAVSharp.NHibernateStorage;
using Nini.Config;
//using ExtensionLoader.Config;

namespace ModCableBeach.PropertyStorage.NHibernate
{
    public class NHibernatePropertyStorage : IPropertyProvider
    {
        protected NHibernateIWebDAVResource storageModule;
        private string name = "NHibernateStorage";
        private string connectionString;

        public NHibernatePropertyStorage()
        {
        }

        public void Initialize(IConfigSource config) 
        {
            IConfig serverConfig = config.Configs["CableBeachService"];
            if (serverConfig == null)
                throw new Exception("No WebDAV section in config file");
                
            if(!serverConfig.Contains("ConnectionString"))
                throw new Exception("No ConnectionString specified in WebDAV section in config file");

            connectionString = serverConfig.GetString("ConnectionString");

            storageModule = new NHibernateIWebDAVResource();
            storageModule.Initialise(connectionString);
        }

        ~NHibernatePropertyStorage()
        {
            storageModule.Dispose();
        }

        #region IPropertyProvider Members

        public bool Save(IWebDAVResource resource)
        {
            return storageModule.SaveResource(resource);
        }

        public IWebDAVResource Load(string path)
        {
            return storageModule.GetResource(path);
        }

        public bool Remove(IWebDAVResource resource)
        {
            storageModule.Remove(resource);
            return false;
        }

        public List<string> LoadFolderCustomPropertiesPresentation(string root_path)
        {
            return storageModule.LoadCollection(root_path);
        }

        #endregion

    }
}
