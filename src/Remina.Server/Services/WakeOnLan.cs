using System.Net;
using System.Net.Sockets;

namespace Remina.Server.Services;

/// <summary>
/// Wake-on-LAN : envoie un « paquet magique » (6× 0xFF + 16× l'adresse MAC) en broadcast UDP.
/// Nécessite que le serveur soit sur le même réseau L2 que le poste cible.
/// </summary>
public static class WakeOnLan
{
    public static bool Send(string mac)
    {
        var bytes = ParseMac(mac);
        if (bytes is null) return false;

        var packet = new byte[102];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int i = 1; i <= 16; i++) Array.Copy(bytes, 0, packet, i * 6, 6);

        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            // Port 9 (discard) par convention ; broadcast dirigé sur le sous-réseau.
            udp.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 9));
            return true;
        }
        catch { return false; }
    }

    private static byte[]? ParseMac(string mac)
    {
        var parts = mac.Split(':', '-');
        if (parts.Length != 6)
        {
            // Format compact sans séparateur (12 caractères hexadécimaux).
            if (mac.Length == 12)
                parts = Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)).ToArray();
            else return null;
        }
        var b = new byte[6];
        for (int i = 0; i < 6; i++)
            if (!byte.TryParse(parts[i], System.Globalization.NumberStyles.HexNumber, null, out b[i]))
                return null;
        return b;
    }
}
