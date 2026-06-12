using MySql.Data.MySqlClient;

public class DbService
{
    // 將連線字串存在這裡，未來要改密碼只要改這裡
    private readonly string _connString = "server=127.0.0.1;user=root;database=ar_furniture_db;port=3306;password=06210621";

    public MySqlConnection GetConnection()
    {
        return new MySqlConnection(_connString);
    }
}