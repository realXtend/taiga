using System;
using System.Collections.Generic;
using System.Text;
using System.Net;

namespace ModCableBeach
{
    public class WebDAVLockResponse
    {
        public LockType LockType;
        public LockScope LockScope;

        /// <summary>
        /// Possible valuse: Infinity, 0
        /// </summary>
        public string Depth;
        public string OwnerNamespaceUri = String.Empty;
        public string OwnerValue = String.Empty;
        public Dictionary<string, string> OwnerValues = new Dictionary<string, string>();
        public string LockToken;

        /// <summary>
        /// Timeout can be either "Infinite", or "Second-n" where n is timeout in seconds
        /// </summary>
        public string Timeout;

        /// <summary>
        /// If resource is locked, or resource is shared locked and client requests exclusice lock, return 423 Locked.
        /// 200 OK for successful lock, 201 Created for lock-null.
        /// 401 Unauthorized or 403 Forbidden if user doesn't have permission to create lock
        /// </summary>
        public HttpStatusCode HttpStatusCode;
    }
}
