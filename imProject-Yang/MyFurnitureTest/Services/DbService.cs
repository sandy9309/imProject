using MySql.Data.MySqlClient;

public class DbService
{
    // 將連線字串存在這裡，未來要改密碼只要改這裡
    private readonly string _connString;

    public DbService(IConfiguration configuration)
    {
        _connString = configuration.GetConnectionString("FurnitureDb")
            ?? Environment.GetEnvironmentVariable("FURNITURE_DB_CONNECTION")
            ?? throw new InvalidOperationException(
                "Database connection is not configured. Set ConnectionStrings:FurnitureDb or FURNITURE_DB_CONNECTION.");
    }
    public MySqlConnection GetConnection()
    {
        return new MySqlConnection(_connString);
    }
}
