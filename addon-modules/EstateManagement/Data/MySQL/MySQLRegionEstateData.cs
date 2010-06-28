using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using log4net;
using MySql.Data.MySqlClient;
using EstateManagementModule;
using OpenSim.Framework;
using OpenMetaverse;

namespace EstateManagement.Data.MySQL
{
    /// <summary>
    /// Class for modifying regions estate, with mysql databases
    /// wery much untested code
    /// </summary>
    public class MySQLRegionEstateData : IRegionEstateModification
    {
        string m_connectionString;
        private static readonly ILog m_log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);


        #region IRegionEstateModification Members

        public void Initialise(string connectstring)
        {
            m_connectionString = connectstring;

            try
            {
                m_log.Info("[MySQLRegionEstateData]: MySql - connecting: " + Util.GetDisplayConnectionString(m_connectionString));
            }
            catch (Exception e)
            {
                m_log.Error("[MySQLRegionEstateData]: Exception: failed to connect database\n" + e.ToString());
            }

        }

        public void SetRegionsEstate(UUID region, int estate)
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                MySqlCommand cmd =
                    new MySqlCommand("update estate_map set EstateID=?estateId where RegionID=?regionId", dbcon);
                try
                {
                    using (cmd)
                    {
                        cmd.Parameters.AddWithValue("?estateId", estate);
                        cmd.Parameters.AddWithValue("?regionId", region.ToString());
                        cmd.ExecuteNonQuery();
                        cmd.Dispose();
                    }
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[MySQLRegionEstateData]: MySQL failure modifying regions {0} estate to {1}. Error: {2}",
                        region.ToString(), estate, e.Message);
                    throw e;
                }
                dbcon.Close();
            }
        }

        public Dictionary<int, string> GetEstates()
        {
            using (MySqlConnection dbcon = new MySqlConnection(m_connectionString))
            {
                dbcon.Open();

                Dictionary<int, string> resp = new Dictionary<int, string>();

                MySqlCommand cmd =
                    new MySqlCommand("select EstateName, EstateID from estate_settings", dbcon);
                try
                {
                    using (cmd)
                    {
                        MySqlDataReader reader = cmd.ExecuteReader();
                        while (reader.Read())
                        {
                            string name = reader.GetString(0);
                            uint estateid = reader.GetUInt32(1);
                            resp.Add((int)estateid, name);
                        }
                    }
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[MySQLRegionEstateData]: MySQL failure querying estates {0}.", e.Message);
                    throw e;
                }
                dbcon.Close();

                return resp;
            }
        }

        #endregion
    }
}
