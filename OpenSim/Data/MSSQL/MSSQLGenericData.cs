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
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ''AS IS'' AND ANY
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
using OpenMetaverse;
using OpenSim.Framework;
using System.Data.SqlClient;
using System.Text;

namespace OpenSim.Data.MSSQL
{
    public class MSSQLGenericData : IGenericData
    {
        private string m_ConnectionString;

        public MSSQLGenericData(string connectionString)
        {
            m_ConnectionString = connectionString;
            using (SqlConnection conn = new SqlConnection(m_ConnectionString))
            {
                conn.Open();
                Migration m = new Migration(conn, GetType().Assembly, "GenericStore");
                m.Update();
            }
        }

        public string Get(string scope, string key)
        {
            string command = "SELECT `value` FROM `generic` WHERE `key` = ?key";
            if (!String.IsNullOrEmpty(scope))
                command += " AND `scope` = ?scope";

            using (SqlConnection conn = new SqlConnection(m_ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(command, conn))
                {
                    cmd.Parameters.AddWithValue("?key", key);
                    if (!String.IsNullOrEmpty(scope))
                        cmd.Parameters.AddWithValue("?scope", scope);

                    using (SqlDataReader result = cmd.ExecuteReader())
                    {
                        if (result.Read())
                            return result.GetString(0);
                        else
                            return null;
                    }
                }
            }
        }

        public bool Store(string scope, string key, string value)
        {
            if (String.IsNullOrEmpty(key))
                throw new ArgumentNullException("key");

            // Try to update first. If that fails, insert.
            // NOTE: Since this is a two part operation, it is prone to race
            // conditions like the rest of the OpenSim data storage code
            string command = "UPDATE `generic` SET `value` = ?value WHERE `key` = ?key";
            if (!String.IsNullOrEmpty(scope))
                command += " AND `scope` = ?scope";

            using (SqlConnection conn = new SqlConnection(m_ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(command, conn))
                {
                    cmd.Parameters.AddWithValue("?key", key);
                    cmd.Parameters.AddWithValue("?value", value);
                    if (!String.IsNullOrEmpty(scope))
                        cmd.Parameters.AddWithValue("?scope", scope ?? String.Empty);

                    int result = cmd.ExecuteNonQuery();

                    if (result < 1)
                    {
                        result = Insert(scope, key, value);
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
        }

        public bool Remove(string scope, string key)
        {
            string command = "DELETE FROM `generic` WHERE `key` = ?key";
            if (!String.IsNullOrEmpty(scope))
                command += " AND `scope` = ?scope";

            using (SqlConnection conn = new SqlConnection(m_ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(command, conn))
                {
                    cmd.Parameters.AddWithValue("?key", key);
                    if (!String.IsNullOrEmpty(scope))
                        cmd.Parameters.AddWithValue("?scope", scope);

                    int result = cmd.ExecuteNonQuery();

                    return (result > 0);
                }
            }
        }

        protected int Insert(string scope, string key, string value)
        {
            string command = "INSERT INTO `generic` (`scope`, `key`, `value`) VALUES (?scope, ?key, ?value)";

            using (SqlConnection conn = new SqlConnection(m_ConnectionString))
            {
                using (SqlCommand cmd = new SqlCommand(command, conn))
                {
                    cmd.Parameters.AddWithValue("?scope", scope ?? String.Empty);
                    cmd.Parameters.AddWithValue("?key", key);
                    cmd.Parameters.AddWithValue("?value", value);

                    return cmd.ExecuteNonQuery();
                }
            }
        }
    }
}
