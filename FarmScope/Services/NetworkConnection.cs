using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RASLogAggregator.Services;

/// <summary>
/// Monte un partage UNC (\\serveur\C$) avec des identifiants Windows alternatifs.
/// À utiliser dans un using : le partage est démonté à la libération.
/// </summary>
public sealed class NetworkConnection : IDisposable
{
    private readonly string _networkName;

    public NetworkConnection(string networkName, string username, string password)
    {
        _networkName = networkName;

        var netResource = new NetResource
        {
            Scope = ResourceScope.GlobalNetwork,
            ResourceType = ResourceType.Disk,
            DisplayType = ResourceDisplaytype.Share,
            RemoteName = networkName
        };

        int result = WNetAddConnection2(netResource, password, username, 0);
        // 1219 = identifiants en conflit avec une session existante : on tolère.
        if (result != 0 && result != 1219)
            throw new Win32Exception(result, $"Connexion à {networkName} impossible (code {result}).");
    }

    public void Dispose()
    {
        try { WNetCancelConnection2(_networkName, 0, true); } catch { /* best effort */ }
    }

    [DllImport("mpr.dll")]
    private static extern int WNetAddConnection2(NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll")]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [StructLayout(LayoutKind.Sequential)]
    private class NetResource
    {
        public ResourceScope Scope;
        public ResourceType ResourceType;
        public ResourceDisplaytype DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    private enum ResourceScope { Connected = 1, GlobalNetwork, Remembered, Recent, Context }
    private enum ResourceType { Any = 0, Disk = 1, Print = 2, Reserved = 8 }
    private enum ResourceDisplaytype { Generic, Domain, Server, Share, File, Group, Network, Root, Shareadmin, Directory, Tree, Ndscontainer }
}
