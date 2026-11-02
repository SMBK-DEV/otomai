using System.Collections.Concurrent;
using System.Net;
using Bubble.Core.Network.Proxy;
using Bubble.Core.Services;
using BubbleBot.Cli.Services;
using Serilog;

namespace BubbleBot.Cli;

public class BotManager : Singleton<BotManager>
{
    public const bool NoLog = false;
    
    public readonly ConcurrentDictionary<string, BotClient> Clients = new();
    public readonly ConcurrentDictionary<string, BotGameClient> GameClients = new();
    public readonly HashSet<string> AccountsErrors = new();

    public HashSet<string> GetBotNames()
    {
        return [..GameClients.Select(x => $"{x.Key} ({x.Value.Info.Name})" +
                                          $" ({x.Value.TreasureHuntData.ChassesSuccess}/{x.Value.TreasureHuntData.ChassesDones})").ToArray()];
    }
    
    public bool IsBotName(string name)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "BubbleBot",
                                $"{name}.txt");
        if (File.Exists(path))
        {
            return true;
        }
        
        return GameClients.Any(x => x.Value.Info.Name == name);
    }
    
    public ConcurrentDictionary<string, BotClient> GetClients()
    {
        return Clients;
    }
    
    public ConcurrentDictionary<string, BotGameClient> GetGameClients()
    {
        return GameClients;
    }
    
    public void UpdateConsoleTitle()
    {
        Console.Title = $@"BubbleBot - {Clients.Count} clients - {string.Join(" / ", GetBotNames())}";
    }
    
    public BotGameClient? GetBotByCharacterId(long characterId)
    {
        return GameClients.Values.FirstOrDefault(x => x.PlayerId == characterId);
    }
    
    public async Task AddBot(string username, BotSettings? settings = null)
    {
        if (Clients.TryGetValue(username, out var v) && DateTime.UtcNow - v.LastReconnect < TimeSpan.FromMinutes(1))
        {
            return;
        }

        AccountService.Instance.LoadAccounts();

        var ankamaHost = GetIpFromHost("dofus2-co-production.ankama-games.com");
        const int ankamaPort = 443;

        var account = AccountService.Instance.GetAccount(username);
        
        if(account == null || !account.ToLoad)
        {
            Log.Logger.Error("Account {id} not found", username);
            return;
        }
        
        var proxyIp = string.Empty;
        var proxyPort = 0;
        var proxyUsername = string.Empty;
        var proxyPassword = string.Empty;
        
        if (!string.IsNullOrWhiteSpace(account.Proxy))
        {
            if (!TryParseSocks5Proxy(account.Proxy, out proxyIp, out proxyPort, out proxyUsername, out proxyPassword))
            {
                Log.Logger.Error("Invalid or unsupported SOCKS5 proxy configured for account {id}. Expected socks5://host:port, socks5://host:port@user:password, or socks5://user:password@host:port.", username);
                return;
            }

            if (!IPAddress.TryParse(proxyIp, out _))
            {
                try
                {
                    proxyIp = GetIpFromHost(proxyIp);
                }
                catch (Exception exception)
                {
                    Log.Logger.Error(exception, "Could not resolve proxy host {ProxyHost} for account {id}", proxyIp, username);
                    return;
                }
            }
        }

        if (File.Exists("accounts.txt"))
        {
            var accounts = (await File.ReadAllLinesAsync("accounts.txt"));
            var accountIndex = accounts.Any(x => x.StartsWith(username, StringComparison.InvariantCultureIgnoreCase));

            if (!accountIndex)
            {
                Log.Logger.Error("Account {id} not found in account.txt", username);
                account.ToLoad = false;
                return;
            }
        }

        var (key, certId, certHash, webProxy) =
            await AnkamaService.Instance.ConnectAsync(account.Username, proxyIp, proxyPort, proxyUsername, proxyPassword);

        if (settings == null)
        {
            settings = new BotSettings
            {
                Id = username,
                Account = account,
                ApiKey = key,
                Address = ankamaHost,
                Port = ankamaPort,
                ServerId = account.Server,
                Proxy = string.IsNullOrEmpty(account.Proxy) ? null
                    : new Socks5Options(proxyIp,
                                        proxyPort,
                                        ankamaHost,
                                        ankamaPort,
                                        proxyUsername,
                                        proxyPassword),
                IsBank = account.IsBank,
                IsKoli = account.IsKoli,
                WebProxy = webProxy,
                CertificateId = certId,
                CertificateHash = certHash,
            };
        }
        else
        {
            settings.ApiKey = key;
            settings.CertificateId = certId;
            settings.CertificateHash = certHash;
        }

        var botClient = new BotClient(settings);

        if (Clients.TryGetValue(username, out var value))
        {
            value.Disconnect();
            value.Dispose();
            botClient.LastReconnect = value.LastReconnect;
        }
        
        botClient.ConnectAsync();
        Clients[username] = botClient;
    }

    private static bool TryParseSocks5Proxy(
        string value,
        out string host,
        out int port,
        out string username,
        out string password)
    {
        host = string.Empty;
        port = 0;
        username = string.Empty;
        password = string.Empty;

        var separator = value.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0 ||
            !value[..separator].Equals("socks5", StringComparison.OrdinalIgnoreCase))
            return false;

        var authority = value[(separator + 3)..].Trim();
        if (authority.Length == 0 || authority.Contains('/') || authority.Contains('?') || authority.Contains('#'))
            return false;

        string endpoint;
        string credentials = string.Empty;
        var at = authority.LastIndexOf('@');
        if (at >= 0)
        {
            var left = authority[..at];
            var right = authority[(at + 1)..];

            // Older BubbleBot format: host:port@username:password.
            if (TryReadProxyEndpoint(left, out _, out _))
            {
                endpoint = left;
                credentials = right;
            }
            // Standard URI format: username:password@host:port.
            else
            {
                endpoint = right;
                credentials = left;
            }
        }
        else
        {
            endpoint = authority;
        }

        if (!TryReadProxyEndpoint(endpoint, out host, out port))
            return false;

        if (credentials.Length > 0)
        {
            var separatorIndex = credentials.IndexOf(':');
            if (separatorIndex < 0)
                username = Uri.UnescapeDataString(credentials);
            else
            {
                username = Uri.UnescapeDataString(credentials[..separatorIndex]);
                password = Uri.UnescapeDataString(credentials[(separatorIndex + 1)..]);
            }
        }

        return true;
    }

    private static bool TryReadProxyEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (!Uri.TryCreate("socks5://" + endpoint, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) || uri.Port is < 1 or > 65535)
            return false;

        host = uri.Host;
        port = uri.Port;
        return true;
    }

    public void AddGameBotClient(BotGameClient client)
    {
        if(GameClients.TryGetValue(client.BotId, out var value))
        { 
            client.TreasureHuntData.ChassesDones = value.TreasureHuntData.ChassesDones;
            client.TreasureHuntData.ChassesSuccess = value.TreasureHuntData.ChassesSuccess;
            client.ConnectedAt = value.ConnectedAt;
            client.Inventory = value.Inventory;
            client.LastFlagRequest = value.LastFlagRequest;
            client.ZaapMode = value.ZaapMode;
            client.CurrentZaapIndex = value.CurrentZaapIndex;
            client.LastZaapTaken = value.LastZaapTaken;
            value.IsStopped = true;
            value.Disconnect();
        }
        
        GameClients[client.BotId] = client;
        UpdateConsoleTitle();
    }

    public static string GetIpFromHost(string host)
    {
        var ips = Dns.GetHostAddresses(host);
        return ips[0].ToString();
    }
    
}
