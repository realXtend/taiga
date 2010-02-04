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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using log4net;
using OpenMetaverse;
using OpenSim.Framework;
using MySql.Data.MySqlClient;

namespace OpenSim.Data.MySQL
{
    public class MySqlGenericData : IGenericData
    {
        private static readonly ILog m_log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private string m_connectionString;
        private object m_dbLock = new object();

        public MySqlGenericData(string connect)
        {
            m_connectionString = connect;

            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();
                Migration m = new Migration(dbcon, this.GetType().Assembly, "GenericStore");
                m.Update();
            }
        }

        public string Get(string scope, string key)
        {
            string command = "SELECT `value` FROM `generic` WHERE `key` = ?key";
            if (!String.IsNullOrEmpty(scope))
                command += " AND `scope` = ?scope";

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    using (MySqlCommand cmd = new MySqlCommand(command, dbcon))
                    {
                        cmd.Parameters.AddWithValue("?key", key);
                        if (!String.IsNullOrEmpty(scope))
                            cmd.Parameters.AddWithValue("?scope", scope);

                        try
                        {
                            using (MySqlDataReader dbReader = cmd.ExecuteReader(CommandBehavior.SingleRow))
                            {
                                if (dbReader.Read())
                                    return dbReader.GetString(0);
                            }
                        }
                        catch (Exception e)
                        {
                            m_log.Error("[GENERIC DB]: MySql failure fetching key " + key + ": " + e.Message);
                        }
                    }
                }
            }

            return null;
        }

        public bool Store(string scope, string key, string value)
        {
            if (String.IsNullOrEmpty(key))
                throw new ArgumentNullException("key");

            string command = "INSERT INTO `generic` (`scope`, `key`, `value`) VALUES (?scope, ?key, ?value) ON DUPLICATE KEY UPDATE `value` = ?value";

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    try
                    {
                        using (MySqlCommand cmd = new MySqlCommand(command, dbcon))
                        {
                            cmd.Parameters.AddWithValue("?scope", scope ?? String.Empty);
                            cmd.Parameters.AddWithValue("?key", key);
                            cmd.Parameters.AddWithValue("?value", value);

                            int result = cmd.ExecuteNonQuery();
                            cmd.Dispose();

                            // mysql_affected_rows is 1 if a new row was inserted and 2 if
                            // "ON DUPLICATE KEY UPDATE" executed
                            return (result > 1);
                        }
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[GENERIC DB]: MySQL failure creating tuple ({0},{1}). Error: {2}",
                            key, value, e.Message);
                    }
                }
            }

            return false;
        }

        public bool Remove(string scope, string key)
        {
            string command = "DELETE FROM `generic` WHERE `key` = ?key";
            if (!String.IsNullOrEmpty(scope))
                command += " AND `scope` = ?scope";

            lock (m_dbLock)
            {
                using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
                {
                    dbcon.Open();

                    try
                    {
                        using (MySqlCommand cmd = new MySqlCommand(command, dbcon))
                        {
                            cmd.Parameters.AddWithValue("?key", key);
                            if (!String.IsNullOrEmpty(scope))
                                cmd.Parameters.AddWithValue("?scope", scope);

                            int result = cmd.ExecuteNonQuery();
                            cmd.Dispose();

                            return (result > 0);
                        }
                    }
                    catch (Exception e)
                    {
                        m_log.ErrorFormat("[GENERIC DB]: MySQL failure removing key {0}. Error: {1}",
                            key, e.Message);
                    }
                }
            }

            return false;
        }
    }
}
