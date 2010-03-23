using System;
using System.Collections.Generic;
using System.Text;
using Nini.Config;

namespace ModCableBeach
{
    public interface IPropertyProvider
    {
        void Initialize(IConfigSource config);

        bool Save(IWebDAVResource resource);
        IWebDAVResource Load(string path);
        bool Remove(IWebDAVResource resource);

        List<string> LoadFolderCustomPropertiesPresentation(string root_path);

    }

    internal class DummyPropertyProvider : IPropertyProvider
    {
        private Dictionary<string, IWebDAVResource> properties = new Dictionary<string, IWebDAVResource>();

        #region IPropertyProvider Members

        public void Initialize(IConfigSource config) 
        { 
            
        }

        public bool Save(IWebDAVResource resource)
        {
            try // for duplicate key entries
            {
                properties[resource.Path] = resource;
                Console.WriteLine("Set property " + resource.Path);
                return true;
            }
            catch (System.Exception exep)
            {
                return false;
            }
        }

        public IWebDAVResource Load(string path)
        {
            if (!properties.ContainsKey(path))
            {
                if (path.EndsWith("/"))
                {
                    return new WebDAVFolder(path, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, false);
                }
                else
                {
                    return new WebDAVFile(path, "", 0, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, false, false);
                }
            }
            else
            {
                return properties[path];
            }
        }

        public bool Remove(IWebDAVResource resource) 
        {
            properties.Remove(resource.Path);
            return true;
        }

        public List<string> LoadFolderCustomPropertiesPresentation(string root_path) 
        {
            List<string> keyList = new List<string>();
            foreach (string key in properties.Keys) 
            { 
                if(key.StartsWith(root_path))
                    keyList.Add(key);
            }
            return keyList;
        }

        #endregion
    }
}
