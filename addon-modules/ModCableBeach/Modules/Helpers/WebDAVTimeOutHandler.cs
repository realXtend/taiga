
using System;
//using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace ModCableBeach.ServerConnectors
{

    public interface IRemoveLockInterface 
    {
        bool RemoveLock(string path);
    }

    public interface ITimeOutHandler
    {
        string AddTimeOutLock(string[] timeouts, string path);
    }

    /// <summary>
    /// Class that stores timeouts for lazy evaluation, that does not use threads
    /// </summary>
    public class OrderedTimeLockDictionary 
    {
        IRemoveLockInterface m_LockHandler;

        // timeout times starting from nearest, ending to longest
        List<KeyValuePair<long, string>> m_timeOutTimes;

        public OrderedTimeLockDictionary(IRemoveLockInterface lockHandler) 
        {
            m_LockHandler = lockHandler;
            m_timeOutTimes = new List<KeyValuePair<long, string>>();
        }

        public bool Insert(long timeOutTime, string path)
        {
            if (timeOutTime < DateTime.Now.Ticks) { return false; } // allready elapsed

            if (m_timeOutTimes.Count == 0) { m_timeOutTimes.Add(new KeyValuePair<long, string>(timeOutTime, path)); return true; }

            for (int i = 0; i < m_timeOutTimes.Count; i++) 
            {
                KeyValuePair<long, string> pair = m_timeOutTimes[i];
                if (pair.Key > timeOutTime)
                { 
                    m_timeOutTimes.Insert(i, new KeyValuePair<long,string>(timeOutTime, path));
                    return true;
                }
            }
            m_timeOutTimes.Add(new KeyValuePair<long, string>(timeOutTime, path)); // add to end
            return true;
        }

        public void RemoveExpired()
        {
            long currentTime = DateTime.Now.Ticks;
            int range = 0;
            for (int i = 0; i < m_timeOutTimes.Count; i++)
            {
                if (m_timeOutTimes[i].Key <= currentTime) 
                {
                    m_LockHandler.RemoveLock(m_timeOutTimes[i].Value);
                    range = i+1;
                }
                else if (m_timeOutTimes[i].Key > currentTime) 
                {
                    break;
                }
            }
            if (range > 0) { m_timeOutTimes.RemoveRange(0, range); }
        }

        internal bool RemoveIfExists(string path)
        {
            foreach (KeyValuePair<long, string> pair in m_timeOutTimes) 
            {
                if (pair.Value == path) 
                {
                    m_timeOutTimes.Remove(pair);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// NOTE!: Currently this class is replaced with WebDAVTransparentLockTimeOutHandler that provides more generic sollution
    /// Class for enabling timeouts, without use of threads, timers or any other resource consuming implementation
    /// </summary>
    public class WebDAVTimeOutHandler : ILockProxy, IRemoveLockInterface, ITimeOutHandler
    {
        public static long MAX_TIME_OUT = 60*60*24*7; //max time out in seconds (-1 = infinite)
        public static bool FORCE_SERVER_TIMEOUT = false; // ignore client timeout request

        protected WebDAVLockHandler m_LockHandler;
        protected OrderedTimeLockDictionary m_TimeOuts;


        public WebDAVTimeOutHandler(WebDAVLockHandler lockHandler)
        { 
            m_LockHandler = lockHandler;
            m_TimeOuts = new OrderedTimeLockDictionary(lockHandler);
        }

        public long DesideTimeOut(string[] timeouts) 
        {
            if (FORCE_SERVER_TIMEOUT)
            {
                return MAX_TIME_OUT;
            }
            if (MAX_TIME_OUT==-1) // infinite timeout, accept first suggestion that is correct format
            {
                foreach (string timeout in timeouts)
                {
                    if (timeout.Equals("Infinite"))
                        return -1;
                    else if (timeout.StartsWith("Second-"))
                    {
                        try
                        {
                            return long.Parse(timeout.Substring(7));
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                    }
                }
                return -1;
            }
            else // accept first timeout that is below max timeout and is correct formst
            {
                foreach (string timeout in timeouts)
                {
                    if (timeout == "Infinite") { continue; }
                    else if (timeout.StartsWith("Second-"))
                    {
                        try
                        {
                            long lTimeout = long.Parse(timeout.Substring(7));
                            if (lTimeout <= MAX_TIME_OUT)
                            {
                                return lTimeout;
                            }
                            else { continue; }
                        }
                        catch (Exception)
                        {
                            continue;
                        }
                    }
                    else { continue; }// Mallformed needs to start with Second-, discard
                }
            }
            return MAX_TIME_OUT; // there were no acceptable timeouts suggested by client
        }

        public long SecondsToTimeOutTicksTime(long seconds) 
        {
            long now = DateTime.Now.Ticks;
            // => total_ticks = 10000000 * total_secs
            long timeoutTicksTime = now + seconds * 10000000;
            return timeoutTicksTime;
        }

        public long CurrentTime() 
        {
            return DateTime.Now.Ticks;
        }

        public string AddTimeOutLock(string[] timeouts, string path)
        {
            long timeout = DesideTimeOut(timeouts);
            long timeoutTime = SecondsToTimeOutTicksTime(timeout);
            // check if there allready exists timeout, then this is refresh to that timeout
            // and we need to remove earlier timeout
            m_TimeOuts.RemoveIfExists(path);

            if (timeout != -1)
            {
                if (m_TimeOuts.Insert(timeoutTime, path))
                {
                    return "Second-" + timeout.ToString();
                }
                else 
                {
                    if (MAX_TIME_OUT == -1) { return "Infinite"; }
                    else { return "Second-" + MAX_TIME_OUT.ToString(); }
                }
            }
            else
            {
                return "Infinite";
            }
        }

        protected void CheckExpiringTimeOutLocks() 
        {
            m_TimeOuts.RemoveExpired();
        }

        #region ILockProxy Members

        public System.Net.HttpStatusCode MkcolCallback(string path, string username, string[] if_headers)
        {
            CheckExpiringTimeOutLocks();
            return m_LockHandler.MkcolCallback(path, username, if_headers);
        }

        public System.Net.HttpStatusCode MoveCallback(string username, System.Uri uri, string destination, DepthHeader depth, bool overwrite, string[] ifHeaders, out System.Collections.Generic.Dictionary<string, System.Net.HttpStatusCode> multiStatusValues)
        {
            CheckExpiringTimeOutLocks();
            return m_LockHandler.MoveCallback(username, uri, destination, depth, overwrite, ifHeaders, out multiStatusValues);
        }

        public System.Net.HttpStatusCode PutCallback(OpenSim.Framework.Servers.HttpServer.OSHttpRequest request, string path, string username, string[] if_headers)
        {
            CheckExpiringTimeOutLocks();
            return m_LockHandler.PutCallback(request, path, username, if_headers);
        }

        public System.Net.HttpStatusCode DeleteCallback(System.Uri uri, string username, out System.Collections.Generic.Dictionary<string, System.Net.HttpStatusCode> multiStatus, string[] if_headers)
        {
            CheckExpiringTimeOutLocks();
            return m_LockHandler.DeleteCallback(uri, username, out multiStatus, if_headers);
        }

        public System.Net.HttpStatusCode PropPatchCallback(string username, System.Uri uri, string uriPath, string nspace, System.Collections.Generic.Dictionary<string, string> setProperties, System.Collections.Generic.List<string> removeProperties, out System.Collections.Generic.Dictionary<string, System.Net.HttpStatusCode> multiStatus, string[] if_headers)
        {
            CheckExpiringTimeOutLocks();
            return m_LockHandler.PropPatchCallback(username, uri, uriPath, nspace, setProperties, removeProperties, out multiStatus, if_headers);
        }

        public WebDAVLockResponse LockHandler(WebDAVLockRequest request) 
        { 
            CheckExpiringTimeOutLocks();
            return m_LockHandler.LockHandler(request);
        }
        #endregion

        #region IRemoveLockInterface Members

        public bool RemoveLock(string path)
        {
            return this.m_LockHandler.RemoveLock(path);
        }

        #endregion
    }
}