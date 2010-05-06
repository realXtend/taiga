using System;
using System.Collections.Generic;
using System.Text;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Tool.hbm2ddl;
using log4net;
using System.Reflection;
using Environment = NHibernate.Cfg.Environment;

namespace WebDAVSharp.NHibernateStorage
{
    public class NHibernateManager
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private string dialect;
        private Configuration configuration;
        private ISessionFactory sessionFactory;

        #region Initialization

        /// <summary>
        /// Initiate NHibernate Manager
        /// </summary>
        /// <param name="connect">NHibernate dialect, driver and connection string separated by ';'</param>
        /// <param name="store">Name of the store</param>
        public NHibernateManager(string connect, string store)
        {
            try
            {
                ParseConnectionString(connect);

                //To create sql file uncomment code below and write the name of the file (rewrites database, history will be erased)
                //SchemaExport exp = new SchemaExport(configuration);
                //exp.SetOutputFile("db_creation.sql");
                //exp.Create(false, true);

                // The above will sweep the db empty and creates the tables,
                // this update checks that the tables are there but wont erase the data
                SchemaUpdate update = new SchemaUpdate(configuration);
                update.Execute(false, true);

                sessionFactory = configuration.BuildSessionFactory();
            }
            catch (MappingException mapE)
            {
                if (mapE.InnerException != null)
                    Console.WriteLine("[NHIBERNATE]: Mapping not valid: {0}, {1}, {2}", mapE.Message, mapE.StackTrace, mapE.InnerException.ToString());
                else
                    m_log.ErrorFormat("[NHIBERNATE]: Mapping not valid: {0}, {1}", mapE.Message, mapE.StackTrace);
            }
            catch (HibernateException hibE)
            {
                Console.WriteLine("[NHIBERNATE]: HibernateException: {0}, {1}", hibE.Message, hibE.StackTrace);
            }
            catch (TypeInitializationException tiE)
            {
                Console.WriteLine("[NHIBERNATE]: TypeInitializationException: {0}, {1}", tiE.Message, tiE.StackTrace);
            }
        }

        /// <summary>
        /// Parses the connection string and creates the NHibernate configuration
        /// </summary>
        /// <param name="connect">NHibernate dialect, driver and connection string separated by ';'</param>
        private void ParseConnectionString(string connect)
        {
            // Split out the dialect, driver, and connect string
            char[] split = { ';' };
            string[] parts = connect.Split(split, 3);
            if (parts.Length != 3)
            {
                // TODO: make this a real exception type
                throw new Exception("Malformed Inventory connection string '" + connect + "'");
            }

            dialect = parts[0];

            // NHibernate setup
            configuration = new Configuration();
            configuration.SetProperty(Environment.ConnectionProvider,
                            "NHibernate.Connection.DriverConnectionProvider");
            configuration.SetProperty(Environment.Dialect,
                            "NHibernate.Dialect." + dialect);
            configuration.SetProperty(Environment.ConnectionDriver,
                            "NHibernate.Driver." + parts[1]);
            configuration.SetProperty(Environment.ConnectionString, parts[2]);
            //configuration.SetProperty(Environment.ShowSql, "true");
            //configuration.SetProperty(Environment.GenerateStatistics, "false");
            //configuration.AddAssembly("WebDAVSharp.NHibernateStorage");
            configuration.AddAssembly("ModCableBeach");

        }

        #endregion

        /// <summary>
        /// Gets object of given type from database with given id. 
        /// Uses stateless session for efficiency.
        /// </summary>
        /// <param name="type">Type of the object.</param>
        /// <param name="id">Id of the object.</param>
        /// <returns>The object or null if object was not found.</returns>
        public object Get(Type type, Object id)
        {
            using (IStatelessSession session = sessionFactory.OpenStatelessSession())
            {
                object obj = null;
                try
                {
                    obj = session.Get(type.FullName, id);
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[NHIBERNATE] {0} of id {1} loading threw exception: " + e.ToString(), type.Name, id);
                }
                return obj;
            }
        }

        /// <summary>
        /// Gets object of given type from database with given id. 
        /// Use this method for objects containing collections. For flat objects stateless mode is more efficient.
        /// </summary>
        /// <param name="type">Type of the object.</param>
        /// <param name="id">Id of the object.</param>
        /// <returns>The object or null if object was not found.</returns>
        public object GetWithStatefullSession(Type type, Object id)
        {
            using (ISession session = sessionFactory.OpenSession())
            {
                object obj = null;
                try
                {
                    obj = session.Get(type.FullName, id);
                }
                catch (Exception e)
                {
                    m_log.ErrorFormat("[NHIBERNATE] {0} of id {1} loading threw exception: " + e.ToString(), type.Name, id);
                }
                return obj;
            }

        }

        /// <summary>
        /// Inserts given object to database.
        /// Uses stateless session for efficiency.
        /// </summary>
        /// <param name="obj">Object to be insterted.</param>
        /// <returns>Identifier of the object. Useful for situations when NHibernate generates the identifier.</returns>
        public object Insert(object obj)
        {
            try
            {
                using (IStatelessSession session = sessionFactory.OpenStatelessSession())
                {
                    using (ITransaction transaction = session.BeginTransaction())
                    {
                        Object identifier = session.Insert(obj);
                        transaction.Commit();
                        return identifier;
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[NHIBERNATE] issue inserting object ", e);
                return null;
            }
        }

        /// <summary>
        /// Inserts given object to database.
        /// Use this method for objects containing collections. For flat objects stateless mode is more efficient.
        /// </summary>
        /// <param name="obj">Object to be insterted.</param>
        /// <returns>Identifier of the object. Useful for situations when NHibernate generates the identifier.</returns>
        public object InsertWithStatefullSession(object obj)
        {
            try
            {
                using (ISession session = sessionFactory.OpenSession())
                {
                    using (ITransaction transaction = session.BeginTransaction())
                    {
                        Object identifier = session.Save(obj);
                        transaction.Commit();
                        return identifier;
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[NHIBERNATE] issue inserting object ", e);
                return null;
            }
        }

        /// <summary>
        /// Updates given object to database.
        /// Uses stateless session for efficiency.
        /// </summary>
        /// <param name="obj">Object to be updated.</param>
        /// <returns>True if operation was succesful.</returns>
        public bool Update(object obj)
        {
            try
            {
                using (IStatelessSession session = sessionFactory.OpenStatelessSession())
                {
                    using (ITransaction transaction = session.BeginTransaction())
                    {
                        session.Update(obj);
                        transaction.Commit();
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[NHIBERNATE] issue updating object ", e);
                return false;
            }
        }

        /// <summary>
        /// Updates given object to database.
        /// Use this method for objects containing collections. For flat objects stateless mode is more efficient.
        /// </summary>
        /// <param name="obj">Object to be updated.</param>
        /// <returns>True if operation was succesful.</returns>
        public bool UpdateWithStatefullSession(object obj)
        {
            try
            {
                using (ISession session = sessionFactory.OpenSession())
                {
                    using (ITransaction transaction = session.BeginTransaction())
                    {
                        session.Update(obj);
                        transaction.Commit();
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[NHIBERNATE] issue updating object ", e);
                return false;
            }
        }

        /// <summary>
        /// Deletes given object from database.
        /// </summary>
        /// <param name="obj">Object to be deleted.</param>
        /// <returns>True if operation was succesful.</returns>
        public bool Delete(object obj)
        {
            try
            {
                using (IStatelessSession session = sessionFactory.OpenStatelessSession())
                {
                    using (ITransaction transaction = session.BeginTransaction())
                    {
                        session.Delete(obj);
                        transaction.Commit();
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                m_log.Error("[NHIBERNATE] issue deleting object ", e);
                return false;
            }
        }

        /// <summary>
        /// Returns statefull session which can be used to execute custom nhibernate or sql queries.
        /// </summary>
        /// <returns>Statefull session</returns>
        public ISession GetSession()
        {
            return sessionFactory.OpenSession();
        }
    }
}
