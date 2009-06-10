/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using log4net;
using Nini.Config;
using System;
using System.Collections.Generic;
using System.Reflection;
using OpenSim.Framework;
using OpenSim.Services.Interfaces;
using OpenSim.Services.Connectors;
using OpenMetaverse;

namespace OpenSim.Services.InventoryService
{
    public class HGInventoryService : ISessionAuthInventoryService
    {
        private static readonly ILog m_log =
                LogManager.GetLogger(
                MethodBase.GetCurrentMethod().DeclaringType);

        private Dictionary<string, InventoryServicesConnector> m_connectors = new Dictionary<string, InventoryServicesConnector>();

        public HGInventoryService(IConfigSource source)
        {
            IConfig moduleConfig = source.Configs["Modules"];
            if (moduleConfig != null)
            {

                IConfig assetConfig = source.Configs["InventoryService"];
                if (assetConfig == null)
                {
                    m_log.Error("[HG INVENTORY SERVICE]: InventoryService missing from OpenSim.ini");
                    return;
                }

                m_log.Info("[HG INVENTORY SERVICE]: HG inventory service enabled");
            }
        }

        private bool StringToUrlAndUserID(string id, out string url, out string userID)
        {
            url = String.Empty;
            userID = String.Empty;

            Uri assetUri;

            if (Uri.TryCreate(id, UriKind.Absolute, out assetUri) &&
                    assetUri.Scheme == Uri.UriSchemeHttp)
            {
                url = "http://" + assetUri.Authority;
                userID = assetUri.LocalPath.Trim(new char[] { '/' });
                return true;
            }

            return false;
        }
        private ISessionAuthInventoryService GetConnector(string url)
        {
            InventoryServicesConnector connector = null;
            lock (m_connectors)
            {
                if (m_connectors.ContainsKey(url))
                {
                    connector = m_connectors[url];
                }
                else
                {
                    // We're instantiating this class explicitly, but this won't
                    // work in general, because the remote grid may be running
                    // an inventory server that has a different protocol.
                    // Eventually we will want a piece of protocol asking
                    // the remote server about its kind. Definitely cool thing to do!
                    connector = new InventoryServicesConnector(url);
                    m_connectors.Add(url, connector);
                }
            }
            return connector;
        }

        public string Host
        {
            get { return string.Empty; }
        }

        public void GetUserInventory(string id, UUID sessionID, InventoryReceiptCallback callback)
        {
            string url = string.Empty;
            string userID = string.Empty;

            if (StringToUrlAndUserID(id, out url, out userID))
            {
                ISessionAuthInventoryService connector = GetConnector(url);
                connector.GetUserInventory(userID, sessionID, callback);
            }

        }

        public bool AddFolder(string userID, InventoryFolderBase folder, UUID sessionID)
        {
            // TODO
            return false;
        }

        public bool UpdateFolder(string userID, InventoryFolderBase folder, UUID sessionID)
        {
            // TODO
            return false;
        }

        public bool MoveFolder(string userID, InventoryFolderBase folder, UUID sessionID)
        {
            // TODO
            return false;
        }

        public bool PurgeFolder(string userID, InventoryFolderBase folder, UUID sessionID)
        {
            // TODO
            return false;
        }

        public bool AddItem(string userID, InventoryItemBase item, UUID sessionID)
        {
            // TODO
            return false;
        }

        public bool UpdateItem(string userID, InventoryItemBase item, UUID sessionID)
        {
            // TODO
            return false;
        }

        public bool DeleteItem(string userID, InventoryItemBase item, UUID sessionID)
        {
            // TODO
            return false;
        }

        public InventoryItemBase QueryItem(string userID, InventoryItemBase item, UUID sessionID)
        {
            // TODO
            return null;
        }

        public InventoryFolderBase QueryFolder(string userID, InventoryFolderBase folder, UUID sessionID)
        {
            // TODO
            return null;
        }

    }
}