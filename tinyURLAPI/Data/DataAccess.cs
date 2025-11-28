using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;


namespace tinyURLAPI.Data
{
    public class DataAccess
    {
        private readonly string _connectionString;
        public DataAccess(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("AzureSql");
        }

        // Always returns an open SQL connection
        public IDbConnection CreateConnection()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
