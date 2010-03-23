using System;
using System.Collections.Generic;
using System.Text;
//using WebDAVSharp;
using ModCableBeach;
using log4net;
using System.Reflection;
using NHibernate;
using NHibernate.Criterion;

namespace WebDAVSharp.NHibernateStorage
{
    public class NHibernateIWebDAVResource
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public bool Inizialized = false;

        public NHibernateManager Manager;

        public void Initialise(string connect)
        {
            m_log.InfoFormat("[NHIBERNATE] Initializing NHibernateIWebDAVResource");
            Assembly assembly = GetType().Assembly;
            Manager = new NHibernateManager(connect, "");
            Inizialized = true;
        }

        public void Dispose() { }

        public bool SaveResource(IWebDAVResource resource)
        {
            try
            {
                IList<WebDAVProperty> properties = resource.CustomProperties;

                // Check first if properties exist and do update instead of making dozens of copies of the same thing.. 
                IWebDAVResource r = GetResource(resource.Path);
                if (r != null)
                {
                    if (Manager.Update(resource)) { return UpdateProperties(resource.Id, properties); }
                    return false;
                }
                else
                {
                    Manager.Insert(resource);
                    if (properties != null &&
                        properties.Count > 0)
                    {
                        foreach (WebDAVProperty prop in properties) // if resource did not exist, certainly its properties dont exist,
                                                                    // so just insert them
                        {
                            prop.ResourceId = resource.Id;
                            Manager.Insert(prop);
                        }
                    }
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Update each property if exists, create if doesnt, 
        /// if there's properties that are in db but in not in properties list with same resource_id remove them
        /// </summary>
        /// <param name="properties"></param>
        /// <returns></returns>
        private bool UpdateProperties(int resource_id, IList<WebDAVProperty> properties)
        {
            ISession session = Manager.GetSession();

            // remove unvanted properties
            ICriteria criteria = session.CreateCriteria(typeof(WebDAVProperty)); 
            criteria.Add(Expression.Eq("ResourceId", resource_id));
            foreach (WebDAVProperty p in criteria.List())
            {
                if (!PropertyInList(p.Name, resource_id, properties)) 
                {
                    Manager.Delete(p);    
                }
            }

            foreach (WebDAVProperty prop in properties)
            {
                prop.ResourceId = resource_id;
                ICriteria cr = session.CreateCriteria(typeof(WebDAVProperty));

                System.Collections.IList list = cr.List();
                cr.Add(Expression.Eq("ResourceId", resource_id));
                System.Collections.IList idList = cr.List();
                cr.Add(Expression.Eq("Name", prop.Name));
                System.Collections.IList nameList = cr.List();

                if (PropertyInList(prop.Name, resource_id, list))
                {
                    Manager.Update(prop);
                }
                else 
                {
                    Manager.Insert(prop);
                }
            }
            return false;
        }

        private bool PropertyInList(string name, int resource_id, IList<WebDAVProperty> properties)
        {
            foreach(WebDAVProperty prop in properties){
                if (prop.Name == name && prop.ResourceId == resource_id) return true;
            }
            return false;
        }

        private bool PropertyInList(string name, int resource_id, System.Collections.IList properties)
        {
            foreach (WebDAVProperty prop in properties)
            {
                if (prop.Name == name && prop.ResourceId == resource_id) return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the resource.
        /// </summary>
        /// <param name="path">The path to the resource.</param>
        /// <returns>The resource or null if not found</returns>
        public IWebDAVResource GetResource(string path)
        {
            ICriteria criteria = Manager.GetSession().CreateCriteria(typeof(IWebDAVResource));
            criteria.Add(Expression.Eq("Path", path));

            System.Collections.IList list = criteria.List();
            if (list.Count > 0)
            {
                IWebDAVResource res = (IWebDAVResource)list[0];
                ICriteria cr2 = Manager.GetSession().CreateCriteria(typeof(WebDAVProperty));
                cr2.Add(Expression.Eq("ResourceId", res.Id));
                foreach (WebDAVProperty prop in cr2.List())
                    if (!res.CustomProperties.Contains(prop))
                        res.CustomProperties.Add(prop);
                return res;
            }

            return null;
        }

        public bool Remove(IWebDAVResource resProp)
        {
            if (resProp != null)
            {
                // need to also delete possible custom propertis
                DeleteResourcesCustomProperties(resProp);
                return Manager.Delete(resProp);
            }
            else
            {
                return false;
            }
        }

        private void DeleteResourcesCustomProperties(IWebDAVResource resProp)
        {
            ICriteria cr = Manager.GetSession().CreateCriteria(typeof(WebDAVProperty));
            cr.Add(Expression.Eq("ResourceId", resProp.Id));
            foreach (WebDAVProperty p in cr.List()) 
            {
                Manager.Delete(p);
            }
        }

        public bool Remove(string path)
        {
            IWebDAVResource res = GetResource(path);
            if (res != null)
            {
                return Manager.Delete(res);
            }
            else
            {
                return false;
            }
        }

        public List<string> LoadCollection(string path)
        {
            List<string> list = new List<string>();
            ICriteria criteria = Manager.GetSession().CreateCriteria(typeof(IWebDAVResource));
            criteria.Add(Expression.Like("Path", path +"%"));
            foreach (IWebDAVResource res in criteria.List())
            {
                list.Add(res.Path);
            }
            return list;
        }
    }
}
