
// Test RealProxy


using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using System.Runtime.Remoting.Proxies;
using System.Runtime.Remoting.Messaging;
using System.Reflection;

namespace ModCableBeach.ServerConnectors
{

    /// <summary>
    /// Class for enabling timeouts, without use of threads, timers or any other resource consuming implementation
    /// Creates dynamically proxy interface by help of RealProxy
    /// </summary>
    public class WebDAVTransparentLockTimeOutHandler : RealProxy, IRemoveLockInterface, ITimeOutHandler
    {
        public static long MAX_TIME_OUT = 60 * 60 * 24 * 7; //max time out in seconds (-1 = infinite)
        public static bool FORCE_SERVER_TIMEOUT = false; // ignore client timeout request

        private object m_Instance;
        protected OrderedTimeLockDictionary m_TimeOuts;

        public WebDAVTransparentLockTimeOutHandler(object instance, Type type, IRemoveLockInterface lockRemover) : base (type)
        {
            m_TimeOuts = new OrderedTimeLockDictionary(lockRemover);
            this.m_Instance = instance;
        }

        public long DesideTimeOut(string[] timeouts)
        {
            if (FORCE_SERVER_TIMEOUT)
            {
                return MAX_TIME_OUT;
            }
            if (MAX_TIME_OUT == -1) // infinite timeout, accept first suggestion that is correct format
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

        // replaces ILockProxy interface
        public override IMessage Invoke(IMessage msg)
        {
            CheckExpiringTimeOutLocks();

            MethodCallMessageWrapper mc = new MethodCallMessageWrapper((IMethodCallMessage)msg);
            MethodInfo method = mc.MethodBase as MethodInfo;
            // invoking ILockProxy methods
            object res = method.Invoke(
                  this.m_Instance,
                  ((method.Attributes & MethodAttributes.SpecialName) == MethodAttributes.SpecialName) ? mc.InArgs : mc.Args);

            return new ReturnMessage(res, mc.Args, mc.Args.Length, mc.LogicalCallContext, mc);
        }

        public static WebDAVTransparentLockTimeOutHandler CreateProxyHandler<T>(object instance, IRemoveLockInterface lockHandler)
        {
            WebDAVTransparentLockTimeOutHandler handler = new WebDAVTransparentLockTimeOutHandler(instance, typeof(T), lockHandler);
            //handler.m_Manager = manager;
            return handler;        
        }

        public static T CreateProxy<T>(object instance, IRemoveLockInterface lockHandler)
        {
            if (instance != null)
            {
                WebDAVTransparentLockTimeOutHandler handler = new WebDAVTransparentLockTimeOutHandler(instance, typeof(T), lockHandler);
                //T ret = (T)new WebDAVTransparentLockTimeOutHandler(instance, typeof(T), lockHandler).GetTransparentProxy();
                T ret = (T)handler.GetTransparentProxy();
                return ret;
            }
            return default(T);
        }

        
        #region IRemoveLockInterface Members

        public bool RemoveLock(string path)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}

//*/