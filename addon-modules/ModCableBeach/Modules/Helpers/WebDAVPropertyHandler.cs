using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web;
using OpenSim.Server.Base;
using OpenSim.Services.Interfaces;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Base;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using CableBeachMessages;
using System.Collections;
using System.Reflection;


namespace ModCableBeach.ServerConnectors
{
    /// <summary>
    /// Class dedicated to handling properties the WebDAVServerConnector file was starting to grow enoumously big
    /// 
    /// WebDAV method Handler fuctions:
    ///     PropFindHandler
    ///     PropPatchHandler
    /// 
    /// Other Helper methods
    ///     CopyProperty - When WebDAV resouce is copied and property needs to be copied
    ///     MoveProperty - When WebDAV resouce is moved and property needs to be moved
    /// </summary>
    public class WebDAVPropertyHandler 
    {
        private WebDAVServerConnector   m_WebDAVServerConnector;
        private IInventoryService       m_InventoryService;
        private IAssetService           m_AssetService;
        private IPropertyProvider       m_PropertyProvider;
        private WebDAVLockHandler       m_LockHandler;

        private string                  m_SubDomain; // - inventory or avatar (subfolder name in the webdav address
                                                     // like http://localhost:8003/inventory/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx/
        private string                  m_InitialLocalPath; // - the path in the inventory that webdav operations operate 
                                                            // for example "/avatar/" or "/"

        public WebDAVPropertyHandler(WebDAVServerConnector connector,
                                     IInventoryService inventory_service,
                                     IAssetService asset_service,
                                     IPropertyProvider property_provider,
                                     WebDAVLockHandler lock_handler,
                                     string domain,
                                     string local_initial_path) 
        {
            m_WebDAVServerConnector     = connector;
            m_InventoryService          = inventory_service;
            m_AssetService              = asset_service;
            m_PropertyProvider          = property_provider;
            m_SubDomain                 = domain;
            m_InitialLocalPath          = local_initial_path;
            m_LockHandler               = lock_handler;
        }

        public IList<IWebDAVResource> PropFindHandler(string username, string path, DepthHeader depth)
        {
            string[] parts = path.Split('/');
            //if (parts.Length >= 3 && parts[1] == "inventory")
            if (parts.Length >= 3 && parts[1] == m_SubDomain)
            {
                string localPath = "";
                UUID agentID = WebDAVServerConnector.AgentIDFromRequestPath(m_SubDomain, m_InitialLocalPath, path, ref localPath);
                if (agentID != UUID.Zero)
                {
                    List<IWebDAVResource> davEntries = new List<IWebDAVResource>();
                    InventoryNodeBase invObject = m_WebDAVServerConnector.PathToInventory(agentID, localPath);
                    if (invObject == null)
                        return davEntries;
                    path = HttpUtility.UrlPathEncode(path);

                    IWebDAVResource resource = null;
                    resource = m_PropertyProvider.Load(path);
                    if (resource == null)
                    {
                        resource = WebDAVServerConnector.InventoryToDAV(path, invObject);
                        m_PropertyProvider.Save(resource);
                    }

                    // Only add the root to response if the client wants it
                    if (depth != DepthHeader.InfinityNoRoot && depth != DepthHeader.OneNoRoot)
                        davEntries.Add(resource);
                    if (invObject is InventoryFolderBase && (depth == DepthHeader.One || depth == DepthHeader.Infinity))
                    {
                        InventoryFolderBase folder = (InventoryFolderBase)invObject;

                        RecursiveGetProperties(agentID, path, folder, ref davEntries, depth);
                    }
                    return davEntries;
                }
            }

            if (parts.Length == 2 || (parts.Length == 3 && parts[2] == String.Empty))
            {
                // Client requested PROPFIND for inventory/ or something alike
                // we probably will not send other users inventory listings because they're private

                List<IWebDAVResource> davEntries = new List<IWebDAVResource>();
                //TODO: Change the DateTimes to something meaningful
                davEntries.Add(new WebDAVFolder(path, DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, false));
                return davEntries;
            }
            throw new NotImplementedException();
        }

        public HttpStatusCode PropPatchHandler(string username, Uri uri, string uriPath, string nspace, 
                                               Dictionary<string, string> setProperties, 
                                               List<string> removeProperties, 
                                               out Dictionary<string, HttpStatusCode> multiStatus,
                                               string[] if_headers)
        {
            //NOTE: The method is atomic: All property changes defined in the request are made, or none is made.
            multiStatus = null;
            string localPath = "";
            string path = uri.LocalPath;
            //UUID agentID = AgentIDFromRequestPath("inventory", "/", path, ref localPath);
            UUID agentID = WebDAVServerConnector.AgentIDFromRequestPath(m_SubDomain, m_InitialLocalPath, path, ref localPath);
            InventoryFolderBase root = m_InventoryService.GetRootFolder(agentID);
            List<IWebDAVResource> davEntries = new List<IWebDAVResource>();

            string searchPath = HttpUtility.UrlPathEncode(uriPath); //convert %20 in to space etc.
            // cut inventory and UUID from path
            searchPath = "/" + searchPath.Split(new char[] { '/' }, 4)[3];

            InventoryNodeBase invObject = m_WebDAVServerConnector.PathToInventory(agentID, searchPath);
            if (invObject == null) //Couldn't find the path requested.
            {
                return HttpStatusCode.NotFound;
            }

            IWebDAVResource resource = null;
            if (m_PropertyProvider != null)
            {
                resource = m_PropertyProvider.Load(uriPath);
            }
            if (resource == null) //didn't find, now try converting from inventory object
            {
                resource = WebDAVServerConnector.InventoryToDAV(uriPath, invObject);
            }
            multiStatus = new Dictionary<string, HttpStatusCode>();
            if (CheckRequiredProperties(removeProperties, ref multiStatus)) // failed dependency
            {
                foreach (string s in setProperties.Keys)
                {
                    if (!multiStatus.ContainsKey(s))
                        multiStatus.Add(s, (HttpStatusCode)424); //HTTP/1.1 424 Failed Dependency
                }

                foreach (string s in removeProperties)
                {
                    if (!multiStatus.ContainsKey(s))
                        multiStatus.Add(s, (HttpStatusCode)424); //HTTP/1.1 424 Failed Dependency
                }
                return (HttpStatusCode)207;
            }
            else
            {
                if (nspace == "DAV:")
                {
                    foreach (KeyValuePair<string, string> property in setProperties)
                    {
                        if (IsStandardProperty(property.Key))
                        {
                            HttpStatusCode status = SetStandardProperty(ref resource, property.Key, property.Value);
                            multiStatus.Add(property.Key, status);
                        }
                        else
                        {
                            SetCustomProperty(nspace, property.Key, property.Value, ref multiStatus, ref resource);
                        }
                    }

                    foreach (string property in removeProperties)
                    {
                        RemoveCustomProperty(nspace, property, ref multiStatus, ref resource);
                    }
                }
                else
                {
                    foreach (KeyValuePair<string, string> property in setProperties)
                    {
                        SetCustomProperty(nspace, property.Key, property.Value, ref multiStatus, ref resource);
                    }

                    foreach (string property in removeProperties)
                    {
                        RemoveCustomProperty(nspace, property, ref multiStatus, ref resource);
                    }
                }
                m_PropertyProvider.Save(resource);

                if (multiStatus.Count >= 1)
                    return (HttpStatusCode)207;
            }
            return HttpStatusCode.Forbidden;
        }

        private void RecursiveGetProperties(UUID agent_id, string path, InventoryFolderBase folder, ref List<IWebDAVResource> dav_entries, DepthHeader depth)
        {
            InventoryCollection ic = m_InventoryService.GetFolderContent(agent_id, folder.ID);

            foreach (InventoryFolderBase ifb in ic.Folders)
            {
                string resourcePath = CollectObjectProperties((InventoryNodeBase)ifb, ref dav_entries, path);
                if (depth == DepthHeader.Infinity)
                    RecursiveGetProperties(agent_id, resourcePath, (InventoryFolderBase)ifb, ref dav_entries, depth);
            }
            foreach (InventoryNodeBase iib in ic.Items)
            {
                //string itemPath;
                //if(path.EndsWith("/")){ itemPath = path.Substring(0, path.Length -1);} else { itemPath = path;}
                string resourcePath = CollectObjectProperties((InventoryNodeBase)iib, ref dav_entries, path);
            }
        }

        private string CollectObjectProperties(InventoryNodeBase node, ref List<IWebDAVResource> dav_entries, string path)
        {
            string resourcePath;
            string name = node.Name;
            name = HttpUtility.UrlPathEncode(name); //encode spaces to %20 etc
            if (path.EndsWith("/"))
                resourcePath = path + name;
            else
                resourcePath = path + "/" + name;

            if (node is InventoryFolderBase)
                resourcePath = resourcePath + "/";

            IWebDAVResource resource = m_PropertyProvider.Load(resourcePath);
            if (resource == null)
            {
                resource = WebDAVServerConnector.InventoryToDAV(resourcePath, node);
                m_PropertyProvider.Save(resource);
            }
            if (resource != null)
                dav_entries.Add(resource);
            return resourcePath;
        }

        bool CheckRequiredProperties(List<string> removeProperties, ref Dictionary<string, HttpStatusCode> multiStatus)
        {
            bool failedDependency = false;
            if (removeProperties.Contains("creationdate"))
            {
                multiStatus.Add("creationdate", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            if (removeProperties.Contains("displayname"))
            {
                multiStatus.Add("displayname", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            if (removeProperties.Contains("getcontentlanguage"))
            {
                multiStatus.Add("getcontentlanguage", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            if (removeProperties.Contains("getcontentlength"))
            {
                multiStatus.Add("getcontentlength", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            if (removeProperties.Contains("getcontenttype"))
            {
                multiStatus.Add("getcontenttype", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            if (removeProperties.Contains("getlastmodified"))
            {
                multiStatus.Add("getlastmodified", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            if (removeProperties.Contains("resourcetype"))
            {
                multiStatus.Add("resourcetype", HttpStatusCode.Forbidden);
                failedDependency = true;
            }
            return failedDependency;
        }

        private bool IsStandardProperty(string name)
        {
            switch (name)
            {
                case "creationdate":
                case "displayname":
                case "getcontentlanguage":
                case "getcontentlength":
                case "getcontenttype":
                case "getlastmodified":
                case "resourcetype":
                    return true;
                default:
                    return false;
            }
        }

        private void SetCustomProperty(string nspace, string name, string value, ref Dictionary<string, HttpStatusCode> multiStatus,
            ref IWebDAVResource resource)
        {
            WebDAVProperty foundProp = null;
            foreach (WebDAVProperty prop in resource.CustomProperties)
            {
                if (prop.Namespace == nspace && prop.Name == name)
                {
                    foundProp = prop;
                    break;
                }
            }
            if (foundProp != null)
                foundProp.Value = value;
            else
            {
                resource.CustomProperties.Add(new WebDAVProperty(name, nspace, value));
            }
            multiStatus.Add(name, HttpStatusCode.OK);
        }

        private HttpStatusCode SetStandardProperty(ref IWebDAVResource resource, string name, string value)
        {
            HttpStatusCode ret = HttpStatusCode.OK;
            switch (name)
            {
                case "creationdate":
                    DateTime c_date = resource.CreationDate;
                    if (!DateTime.TryParse(value, out c_date))
                        ret = HttpStatusCode.Conflict;
                    else
                        resource.CreationDate = c_date;
                    break;

                case "displayname":
                case "getcontentlanguage":
                    return HttpStatusCode.InternalServerError; //TODO: implement

                case "getcontentlength":
                    int c_length = 0;
                    if (!Int32.TryParse(value, out c_length))
                        ret = HttpStatusCode.Conflict;
                    else
                        resource.ContentLength = c_length;
                    break;

                case "getcontenttype":
                    resource.ContentType = value;
                    break;

                case "getlastmodified":
                    DateTime lm_date = resource.LastModifiedDate;
                    if (!DateTime.TryParse(value, out lm_date))
                        ret = HttpStatusCode.Conflict;
                    else
                        resource.LastModifiedDate = lm_date;
                    break;

                case "resourcetype":
                    return HttpStatusCode.InternalServerError; //TODO: implement
                default:
                    return HttpStatusCode.InternalServerError;
            }
            return ret;
        }

        private void RemoveCustomProperty(string nspace, string name, ref Dictionary<string, HttpStatusCode> multiStatus, ref IWebDAVResource resource)
        {
            WebDAVProperty foundProp = null;
            foreach (WebDAVProperty prop in resource.CustomProperties)
            {
                if (prop.Namespace == nspace && prop.Name == name)
                {
                    foundProp = prop;
                    break;
                }
            }
            if (foundProp != null)
            {
                // it's not enough we just remove it here we need to do it db too
                resource.CustomProperties.Remove(foundProp);
            }
            multiStatus.Add(name, HttpStatusCode.OK);
        }

        public bool CopyProperties(string original_path, string destiny_path)
        {
            IWebDAVResource resource = m_PropertyProvider.Load(original_path);
            if (resource != null)
            {
                IWebDAVResource copy = null;
                if (resource is WebDAVFile)
                {
                    WebDAVFile file = (WebDAVFile)resource;
                    copy = new WebDAVFile(destiny_path, file.ContentType, file.ContentLength, file.CreationDate, file.LastModifiedDate,
                                                     file.LastAccessedDate, file.IsHidden, file.IsReadOnly);
                }
                if (resource is WebDAVFolder)
                {
                    WebDAVFolder folder = (WebDAVFolder)resource;
                    copy = new WebDAVFolder(destiny_path, folder.CreationDate, folder.LastModifiedDate, folder.LastAccessedDate, folder.IsHidden);
                }
                foreach (WebDAVProperty davProperty in resource.CustomProperties)
                {
                    copy.CustomProperties.Add(davProperty);
                }
                return m_PropertyProvider.Save(copy);
            }
            return false;
        }

        public bool MoveProperties(string original_path, string destiny_path)
        {
            IWebDAVResource resource = m_PropertyProvider.Load(original_path);
            if (resource != null)
            {
                m_PropertyProvider.Remove(resource);
                resource.Path = destiny_path;
                m_PropertyProvider.Save(resource);
                return true;
            }
            return false;
        }

        public bool CopyProperties(UUID agent_id, InventoryNodeBase node, string destiny_path)
        {
            string originalPath = "/inventory/" + agent_id.ToString() + "/" + m_WebDAVServerConnector.NodeToPath(agent_id, node);
            CopyProperties(originalPath, destiny_path);
            return true;
        }

        public bool MoveProperties(UUID agent_id, InventoryNodeBase node, string destiny_path)
        {
            string originalPath = m_WebDAVServerConnector.NodeToPath(agent_id, node);
            MoveProperties(originalPath, destiny_path);
            return true;
        }

        public bool DeleteProperties(string path) 
        {
            // just delete items custom properties
            IWebDAVResource item = m_PropertyProvider.Load(path);
            m_PropertyProvider.Remove(item);
            return true;
        }

        internal List<string> PreparePropertiesMove(UUID agent_id, InventoryNodeBase source_node)
        {
            string rootPath = "/" + m_SubDomain + "/" + agent_id.ToString() + "/" + m_WebDAVServerConnector.NodeToPath(agent_id, source_node);
            List<string> allPaths = m_PropertyProvider.LoadFolderCustomPropertiesPresentation(rootPath);
            // set root path to last element
            allPaths.Remove(rootPath);
            allPaths.Add(rootPath); 
            return allPaths;
        }

        internal void MoveProperties(UUID agent_id, InventoryNodeBase source_node, List<string> custom_properties)
        {
            string rootOrigin = custom_properties[custom_properties.Count - 1];
            custom_properties.Remove(rootOrigin);
            string rootDestiny = "/" + m_SubDomain + "/" + agent_id.ToString() + "/" + m_WebDAVServerConnector.NodeToPath(agent_id, source_node);
            IWebDAVResource folder = m_PropertyProvider.Load(rootOrigin);
            m_PropertyProvider.Remove(folder);
            folder.Path = rootDestiny;
            m_PropertyProvider.Save(folder);
            foreach (string path in custom_properties)
            {
                IWebDAVResource resource = m_PropertyProvider.Load(path);
                m_PropertyProvider.Remove(resource);
                string endPart = resource.Path.Substring(rootOrigin.Length);
                resource.Path = rootDestiny + endPart;
                m_PropertyProvider.Save(resource);
            }
        }
    }
}