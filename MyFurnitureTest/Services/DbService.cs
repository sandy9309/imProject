using MySql.Data.MySqlClient;

public class DbService
{
    private readonly string _connString = "server=163.13.202.116;user=root;database=ar_furniture_db;port=3306;password=06210621";
    public MySqlConnection GetConnection()
    {
        return new MySqlConnection(_connString);
    }
}