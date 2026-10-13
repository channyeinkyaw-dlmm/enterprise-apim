using System.Net;

namespace EnterpriseApim.Core;

public static class IpRanges
{
    /// <summary>Parses "203.0.113.0/24" or a single address like "203.0.113.5".</summary>
    public static bool TryParse(string value, out System.Net.IPNetwork network)
    {
        network = default;
        var text = value.Trim();
        if (text.Length == 0) return false;

        if (!text.Contains('/'))
        {
            if (!IPAddress.TryParse(text, out var address)) return false;
            var bits = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;
            text = $"{address}/{bits}";
        }

        return System.Net.IPNetwork.TryParse(text, out network);
    }
}
