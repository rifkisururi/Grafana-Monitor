using Npgsql;

namespace Grafana_Monitor.Data;

public static class PostgresUrlParser
{
    public static string ToNpgsqlConnectionString(string databaseUrl)
    {
        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "postgresql" && uri.Scheme != "postgres") ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("DATABASE_URL harus berupa URI PostgreSQL yang valid.");
        }

        string username;
        string password;
        try
        {
            var credentials = uri.UserInfo.Split(':', 2);
            if (credentials.Length != 2)
            {
                throw new FormatException();
            }

            username = Uri.UnescapeDataString(credentials[0]);
            password = Uri.UnescapeDataString(credentials[1]);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("DATABASE_URL harus memuat username dan password.");
        }

        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("DATABASE_URL harus memuat username dan nama database.");
        }

        var query = ParseQuery(uri.Query);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = username,
            Password = password,
            Database = database,
            SslMode = SslMode.Require,
            Pooling = true,
            ApplicationName = "monitor-demo-api"
        };

        if (query.TryGetValue("sslmode", out var sslMode) &&
            !sslMode.Equals("require", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("DATABASE_URL hanya mengizinkan sslmode=require.");
        }

        if (query.TryGetValue("channel_binding", out var channelBinding))
        {
            builder.ChannelBinding = channelBinding.ToLowerInvariant() switch
            {
                "require" => ChannelBinding.Require,
                "prefer" => ChannelBinding.Prefer,
                "disable" => ChannelBinding.Disable,
                _ => throw new InvalidOperationException("Nilai channel_binding di DATABASE_URL tidak valid.")
            };
        }

        if (query.TryGetValue("pooling", out var pooling))
        {
            if (!bool.TryParse(pooling, out var poolingEnabled))
            {
                throw new InvalidOperationException("Nilai pooling di DATABASE_URL tidak valid.");
            }

            builder.Pooling = poolingEnabled;
        }

        if (query.TryGetValue("maximum pool size", out var maxPoolSize) ||
            query.TryGetValue("max_pool_size", out maxPoolSize))
        {
            if (!int.TryParse(maxPoolSize, out var parsedMaxPoolSize) || parsedMaxPoolSize < 1)
            {
                throw new InvalidOperationException("Nilai ukuran pool pada DATABASE_URL tidak valid.");
            }

            builder.MaxPoolSize = parsedMaxPoolSize;
        }

        return builder.ConnectionString;
    }

    private static Dictionary<string, string> ParseQuery(string queryString)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in queryString.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = item.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2
                ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                : string.Empty;
            result[key] = value;
        }

        return result;
    }
}
