using System.Net;
using System.Web;
using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;

namespace ModCableBeach.ServerConnectors
{

    /// <summary>
    /// Method callbacks that need to be proxied and checked for locks
    /// </summary>
    public interface ILockProxy
    {
        HttpStatusCode MkcolCallback(string path, string username, string[] if_headers);
        HttpStatusCode MoveCallback(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<String, HttpStatusCode> multiStatusValues);
        HttpStatusCode PutCallback(OSHttpRequest request, string path, string username, string[] if_headers);
        HttpStatusCode DeleteCallback(Uri uri, string username, out Dictionary<string, HttpStatusCode> multiStatus, string[] if_headers);
        HttpStatusCode PropPatchCallback(string username, Uri uri, string uriPath, string nspace, Dictionary<string, string> setProperties, List<string> removeProperties, out Dictionary<string, HttpStatusCode> multiStatus, string[] if_headers);
        WebDAVLockResponse LockHandler(WebDAVLockRequest request);
    }


    public class WebDAVLockHandler : ILockProxy, IRemoveLockInterface
    {
        private WebDAVServerConnector   m_WebDAVServerConnector;
        private WebDAVPropertyHandler   m_WebDAVPropertyHandler; // we restrict proppatching, when locks are applied

        public ITimeOutHandler          m_WebDAVTimeOutHandler;
        //public  WebDAVTimeOutHandler    m_WebDAVTimeOutHandler;
        public bool m_useTimeOuts = false;

        protected Dictionary<string, WebDAVLockResponse> lockedResources = new Dictionary<string, WebDAVLockResponse>();

        private string m_SubDomain; // - inventory or avatar (subfolder name in the webdav address
        // like http://localhost:8003/inventory/xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx/
        private string m_InitialLocalPath; // - the path in the inventory that webdav operations operate 
        // for example "/avatar/" or "/"

        public bool RemoveLock(string path) 
        {
            if (lockedResources.ContainsKey(path))
            {
                lockedResources.Remove(path);
                return true;
            }
            else
            {
                return false;
            }
        }

        public WebDAVLockHandler(WebDAVServerConnector connector, WebDAVPropertyHandler property_handler, string sub_domain, string initial_local_path) 
        {
            m_WebDAVServerConnector = connector;
            m_WebDAVPropertyHandler = property_handler;
            m_SubDomain = sub_domain;
            m_InitialLocalPath = initial_local_path;
        }

        public WebDAVLockResponse LockHandler(WebDAVLockRequest request)
        {
            WebDAVLockResponse response = new WebDAVLockResponse();
            lock (this) // make sure only one thread is locking the resource
            {
                string pathDec = HttpUtility.UrlDecode(request.Path);

                if (!lockedResources.ContainsKey(pathDec))
                {
                    // check the resource exists
                    if (!CheckIfExists(request))
                    {
                        response.HttpStatusCode = HttpStatusCode.NotFound;
                    }
                    else
                    {
                        string[] timeouts = request.RequestedTimeout;
                        string timeout = "Infinite";
                        if (m_useTimeOuts)
                        {
                            if (timeouts != null && timeouts.Length != 0)
                            {
                                timeout = m_WebDAVTimeOutHandler.AddTimeOutLock(timeouts, pathDec);
                            }
                        }
                        //create locktoken
                        //more information how to really generate the token, see: rfc2518 section 6.4
                        string locktoken = "opaquelocktoken:" + UUID.Random().ToString();
                        response.LockToken = locktoken;
                        response.LockScope = request.LockScope;
                        response.LockType = request.LockType;
                        response.Depth = "0";
                        response.Timeout = timeout;
                        response.OwnerNamespaceUri = request.OwnerNamespaceUri;
                        response.OwnerValue = request.OwnerValue;
                        response.OwnerValues = request.OwnerValues;
                        response.HttpStatusCode = HttpStatusCode.Created;
                        //lockedResources.Add(request.Path, response);
                        lockedResources.Add(pathDec, response);
                    }
                }
                else
                {
                    if (request.IfHeaders != null && request.IfHeaders.Length!=0) // it's refresh request
                    {
                        if (CheckIfHeaders(request.IfHeaders, pathDec))
                        { // its valid request refresh lock:
                            if (!CheckIfExists(request))
                            {
                                response.HttpStatusCode = HttpStatusCode.NotFound;
                            }
                            else
                            {
                                string[] timeouts = request.RequestedTimeout;
                                string timeout = "Infinite";

                                if (m_useTimeOuts)
                                {
                                    if (timeouts != null && timeouts.Length != 0)
                                    {
                                        timeout = m_WebDAVTimeOutHandler.AddTimeOutLock(request.RequestedTimeout, pathDec);
                                    }
                                }
                                //create locktoken
                                //more information how to really generate the token, see: rfc2518 section 6.4
                                string locktoken = "opaquelocktoken:" + UUID.Random().ToString();
                                response.LockToken = locktoken;
                                response.LockScope = request.LockScope;
                                response.LockType = request.LockType;
                                response.Depth = "0";
                                response.Timeout = timeout;
                                response.OwnerNamespaceUri = request.OwnerNamespaceUri;
                                response.OwnerValue = request.OwnerValue;
                                response.OwnerValues = request.OwnerValues;
                                response.HttpStatusCode = HttpStatusCode.Created;

                            }
                        }
                    }
                    else if (lockedResources[pathDec].LockScope == LockScope.shared && request.LockScope == LockScope.shared)
                    {
                        //quite special case. no implementation yet
                        response.HttpStatusCode = HttpStatusCode.InternalServerError; //locked
                        ;
                    }
                    else
                    {
                        response.HttpStatusCode = (HttpStatusCode)423; //locked
                    }
                }
            }
            return response;
        }

        public HttpStatusCode UnlockHandler(string path, string locktoken, string username)
        {
            string decPath = HttpUtility.UrlDecode(path);

            if (lockedResources.ContainsKey(decPath))
            {
                WebDAVLockResponse response = lockedResources[decPath];
                if (response.LockToken == locktoken)
                {
                    if (true) //username check should be here
                    {
                        lockedResources.Remove(decPath);
                        return HttpStatusCode.NoContent;
                    }
                    else
                    {
                        return HttpStatusCode.Unauthorized;
                    }
                }
                else
                {
                    return HttpStatusCode.PreconditionFailed;
                }
            }
            else
            {
                return HttpStatusCode.PreconditionFailed;
            }
        }

        private bool CheckIfExists(WebDAVLockRequest request)
        {
            string localPath = "";
            UUID agentID = WebDAVServerConnector.AgentIDFromRequestPath(m_SubDomain, m_InitialLocalPath, request.Path, ref localPath);
            InventoryNodeBase invObject = m_WebDAVServerConnector.PathToInventory(agentID, localPath);
            if (invObject != null)
                return true;
            return false;
        }


        #region ILockProxy Members

        public HttpStatusCode MkcolCallback(string path, string username, string[] if_headers)
        {
            if (!lockedResources.ContainsKey(path))
            {
                string parentPath = CheckParentPath(path);
                if (parentPath==null)
                {
                    return m_WebDAVServerConnector.InventoryNewColHandler(path, username, if_headers);
                }
                else 
                {
                    if (CheckIfHeaders(if_headers, parentPath))
                        return m_WebDAVServerConnector.InventoryNewColHandler(path, username, if_headers);
                    else
                        return (HttpStatusCode)423; //locked
                }
            }
            else
            {
                if (CheckIfHeaders(if_headers, path))
                {
                    string parentPath = CheckParentPath(path);
                    if (parentPath == null)
                    {
                        return m_WebDAVServerConnector.InventoryNewColHandler(path, username, if_headers);
                    }
                    else 
                    {
                        if (CheckIfHeaders(if_headers, parentPath))
                            return m_WebDAVServerConnector.InventoryNewColHandler(path, username, if_headers);
                        else
                            return (HttpStatusCode)423; //locked                        
                    }
                }
                else
                    return (HttpStatusCode)423; //locked
            }
        }

        public HttpStatusCode MoveCallback(string username, Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out Dictionary<string, HttpStatusCode> multiStatusValues)
        {
            string path = uri.LocalPath;
            Dictionary<string, WebDAVLockResponse> allLockedNodes;
            Dictionary<string, WebDAVLockResponse> lockedNodes = CheckAllLockedChildren(ifHeaders, path, out allLockedNodes);
            if (lockedNodes.Count != 0) 
            {
                multiStatusValues = new Dictionary<string, HttpStatusCode>();
                this.FailingDepsToMultiStatus(lockedNodes, ref multiStatusValues);
                return (HttpStatusCode)424; // failed dependency                
            }

            multiStatusValues = new Dictionary<string, HttpStatusCode>(); // empty status vals
            string parentPath = CheckParentPath(path);
            if (parentPath != null)
            {
                if (!CheckIfHeaders(ifHeaders, parentPath))
                {
                    multiStatusValues.Add(parentPath, (HttpStatusCode)423);
                    return (HttpStatusCode)424; //locked                    
                }
            }

            if (!lockedResources.ContainsKey(path) || !lockedResources.ContainsKey(path + "/"))
            {
                // update path to new value
                //WebDAVLockResponse resp = lockedResources[path];
                //lockedResources.Remove(path);
                string newPath = destination.Substring(destination.IndexOf("/" + m_SubDomain + "/"));
                //lockedResources.Add(newPath, resp);
                if(allLockedNodes.Count>0)
                {
                    //string newPath = destination.Substring(destination.IndexOf("/" + m_SubDomain + "/"));
                    this.SendAllChildrenNodeLocksToNewDestionation(allLockedNodes, path, newPath);
                }
                return m_WebDAVServerConnector.InventoryMoveHandler(username, uri, destination, depth, overwrite, 
                                                                            ifHeaders, out  multiStatusValues);

            }
            else
            {
                if (!CheckIfHeaders(ifHeaders, path))
                {
                    // update path to new value
                    WebDAVLockResponse resp = lockedResources[path];
                    lockedResources.Remove(path);
                    string newPath = destination.Substring(destination.IndexOf("/" + m_SubDomain + "/"));
                    lockedResources.Add(newPath, resp);
                    if (allLockedNodes.Count > 0)
                    {
                        this.SendAllChildrenNodeLocksToNewDestionation(allLockedNodes, path, newPath);
                    }
                    return m_WebDAVServerConnector.InventoryMoveHandler(username, uri, destination, depth, overwrite, ifHeaders, out  multiStatusValues);
                }
                multiStatusValues = new Dictionary<string, HttpStatusCode>(); // empty status vals
                return (HttpStatusCode)423; //locked
            }
        }

        public HttpStatusCode PutCallback(OSHttpRequest request, string path, string username, string[] if_headers)
        {
            string parentPath = CheckParentPath(path);
            if (parentPath != null)
            {
                if (!CheckIfHeaders(if_headers, parentPath))
                {
                    return (HttpStatusCode)423; //locked
                }
            }

            if (!lockedResources.ContainsKey(path) || !lockedResources.ContainsKey(path + "/"))
            {
                return m_WebDAVServerConnector.InventoryPutHandler(request, path, username, if_headers);
            }
            else
            {
                if (CheckIfHeaders(if_headers, path))
                    return m_WebDAVServerConnector.InventoryPutHandler(request, path, username, if_headers);
                else
                    return (HttpStatusCode)423; //locked
            }
        }

        public HttpStatusCode DeleteCallback(Uri uri, string username, out Dictionary<string, HttpStatusCode> multiStatus, string[] if_headers)
        {
            string path = uri.LocalPath;

            Dictionary<string, WebDAVLockResponse> allLockedNodes;
            Dictionary<string, WebDAVLockResponse> lockedNodes = CheckAllLockedChildren(if_headers, path, out allLockedNodes);
            if (lockedNodes.Count != 0) {
                multiStatus = new Dictionary<string, HttpStatusCode>();
                this.FailingDepsToMultiStatus(lockedNodes, ref multiStatus);
                //return (HttpStatusCode)424; // failed dependency
                return (HttpStatusCode)207; // need to return this value to avoid internal server error
            }
            multiStatus = new Dictionary<string, HttpStatusCode>();

            string parentPath = CheckParentPath(path);
            if (parentPath != null) 
            {
                if (CheckIfHeaders(if_headers, parentPath))
                {
                    multiStatus.Add(parentPath, (HttpStatusCode)423);
                    return (HttpStatusCode)207;
                }
            }

            if (!lockedResources.ContainsKey(path) || !lockedResources.ContainsKey(path + "/"))
            {
                if(allLockedNodes.Count>0)
                    DeleteAllChildrenNodeLocks(allLockedNodes);
                return m_WebDAVServerConnector.InventoryDeleteHandler(uri, username, out multiStatus, if_headers);
            }
            else
            {
                if (CheckIfHeaders(if_headers, path))
                {
                    if (allLockedNodes.Count > 0)
                        DeleteAllChildrenNodeLocks(allLockedNodes);
                    return m_WebDAVServerConnector.InventoryDeleteHandler(uri, username, out multiStatus, if_headers);
                }
                else
                {
                    multiStatus.Add(path, (HttpStatusCode)423); // empty status vals
                    return (HttpStatusCode)207; // 242locked
                }
            }
        }

        public HttpStatusCode PropPatchCallback(string username, Uri uri, string uriPath, string nspace, Dictionary<string, string> setProperties, List<string> removeProperties, out Dictionary<string, HttpStatusCode> multiStatus, string[] if_headers)
        {
            string path = uri.LocalPath;

            multiStatus = new Dictionary<string, HttpStatusCode>();
            string parentPath = CheckParentPath(path);
            if (parentPath != null) 
            {
                if (CheckIfHeaders(if_headers, parentPath))
                {
                    multiStatus.Add(parentPath, (HttpStatusCode)423);
                    return (HttpStatusCode)424;
                }                
            }

            if (!lockedResources.ContainsKey(path) || !lockedResources.ContainsKey(path + "/"))
            {
                    return m_WebDAVPropertyHandler.PropPatchHandler(username, uri, uriPath, nspace, setProperties, removeProperties, out multiStatus, if_headers);
            }
            else
            {
                if (CheckIfHeaders(if_headers, path))
                {
                    return m_WebDAVPropertyHandler.PropPatchHandler(username, uri, uriPath, nspace, setProperties, removeProperties, out multiStatus, if_headers);
                }
                else
                {
                    multiStatus = new Dictionary<string, HttpStatusCode>(); // empty status vals
                    return (HttpStatusCode)423; //locked
                }
            }
        }

        #endregion

        /// <summary>
        /// Check that there's none of the folders parent paths that are locked
        /// </summary>
        /// <param name="path"></param>
        /// <returns> false if nothing is locked </returns>
        /// TODO: this checking is not totally complete should check all parents
        private string CheckParentPath(string path)
        {
            // create parent paths:
            string[] split = path.Split('/');
            string currentPath = "/";
            for (int i = 1; i < split.Length; i++) 
            {
                currentPath = currentPath + split[i] + "/";
                if (this.lockedResources.ContainsKey(currentPath))
                    return currentPath;
                if (this.lockedResources.ContainsKey(currentPath.Substring(0, currentPath.Length-1))) // without the trailing '/'
                    return currentPath.Substring(0, currentPath.Length - 1);
            }
            return null; 
        }

        private bool CheckIfHeaders(string[] headers, string path)
        {
            if (headers != null)
            {
                foreach (string s in headers)
                {
                    if ("(" + lockedResources[path].LockToken + ")" == s)
                        return true;
                }
            }
            return false;
        }

        private void FailingDepsToMultiStatus(Dictionary<string, WebDAVLockResponse> failingDeps, ref Dictionary<string, HttpStatusCode> multi_status)
        { 
            foreach(KeyValuePair<string, WebDAVLockResponse> pair in failingDeps)
            {
                multi_status.Add(pair.Key, (HttpStatusCode)423); //locked
            }
        }

        private bool SendAllChildrenNodeLocksToNewDestionation(Dictionary<string, WebDAVLockResponse> children_nodes, string old_path, string new_path)
        {
            foreach (KeyValuePair<string, WebDAVLockResponse> pair in children_nodes) 
            {
                if (pair.Key != old_path) // check that we're not moving the root that's allready moved to new location
                {
                    WebDAVLockResponse resp = lockedResources[pair.Key];
                    lockedResources.Remove(pair.Key);
                    string endPath = pair.Key.Substring(old_path.Length);
                    lockedResources.Add(new_path + endPath, resp);
                }
            }
            return false;
        }

        private bool DeleteAllChildrenNodeLocks(Dictionary<string, WebDAVLockResponse> children_nodes)
        {
            foreach (KeyValuePair<string, WebDAVLockResponse> pair in children_nodes)
            {
                lockedResources.Remove(pair.Key);
            }
            return false;
        }

        /// <summary>
        /// returns children that are not released with locked tokens, sets all_locked_children parameter
        /// </summary>
        /// <param name="tokens"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private Dictionary<string, WebDAVLockResponse> CheckAllLockedChildren(string[] tokens, string path, 
                                        out Dictionary<string, WebDAVLockResponse> all_locked_children)
        {
            Dictionary<string, WebDAVLockResponse> failingDeps = new Dictionary<string, WebDAVLockResponse>();
            Dictionary<string, WebDAVLockResponse> all = GetAllLockedChildren(path);
            all_locked_children = all;
            if (tokens == null && all.Count > 0) {
                return all;
            }

            if (tokens != null)
            {
                foreach (KeyValuePair<string, WebDAVLockResponse> pair in all)
                {
                    bool authorized = false;
                    foreach (string token in tokens)
                    {
                        if ("(" + pair.Value.LockToken + ")" == token)
                            authorized = true;
                    }
                    if (authorized == false)
                        failingDeps.Add(pair.Key, pair.Value);

                }
            }
            return failingDeps;
        }

        private Dictionary<string, WebDAVLockResponse> GetAllLockedChildren(string path)
        {
            //List<string> children = new List<string>();
            Dictionary<string, WebDAVLockResponse> children = new Dictionary<string,WebDAVLockResponse>();
            foreach (string key in lockedResources.Keys)
            {
                if (key.StartsWith(path))
                    children.Add(key, lockedResources[key]);
            }
            return children;
        }

    }
}
