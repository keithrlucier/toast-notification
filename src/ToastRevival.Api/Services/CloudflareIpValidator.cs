using System.Net;
using System.Net.Sockets;

namespace ToastRevival.Api.Services;

/// <summary>
/// BF-2 (Keith 2026-06-01): the brute-force rate limiter and audit ClientIp() must only
/// trust the <c>CF-Connecting-IP</c> header when the request actually arrived through
/// Cloudflare — otherwise an attacker hitting the origin directly can forge that header
/// and mint a fresh rate-limit bucket per request. This validator checks the real socket
/// peer (<c>HttpContext.Connection.RemoteIpAddress</c>) against Cloudflare's published
/// egress ranges. We do NOT geo-restrict — Toast devices/operators legitimately connect
/// from across the US and the Philippines; we only decide whether to BELIEVE the header.
///
/// Standing rule (Keith): when we control the DNS + web layer, lock the origin to
/// Cloudflare IPs as the default for every site.
///
/// Cloudflare CIDR ranges rarely change (~1–2x/year). Source of truth:
/// https://www.cloudflare.com/ips/ (also https://api.cloudflare.com/client/v4/ips).
/// If the origin is fronted by nginx on loopback, the peer is 127.0.0.1 — see
/// <see cref="ResolveTrustedClientIp"/> for how that is handled.
/// </summary>
public interface ICloudflareIpValidator
{
    /// <summary>True if <paramref name="peer"/> is within a published Cloudflare egress range.</summary>
    bool IsCloudflareEgress(IPAddress? peer);
}

public sealed class CloudflareIpValidator : ICloudflareIpValidator
{
    // https://www.cloudflare.com/ips-v4 + ips-v6 (verify on deploy; changes ~1–2x/year).
    private static readonly (IPAddress Network, int Prefix)[] Ranges = BuildRanges(new[]
    {
        // IPv4
        "173.245.48.0/20", "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
        "141.101.64.0/18", "108.162.192.0/18", "190.93.240.0/20", "188.114.96.0/20",
        "197.234.240.0/22", "198.41.128.0/17", "162.158.0.0/15", "104.16.0.0/13",
        "104.24.0.0/14", "172.64.0.0/13", "131.0.72.0/22",
        // IPv6
        "2400:cb00::/32", "2606:4700::/32", "2803:f800::/32", "2405:b500::/32",
        "2405:8100::/32", "2a06:98c0::/29", "2c0f:f248::/32",
    });

    public bool IsCloudflareEgress(IPAddress? peer)
    {
        if (peer is null) return false;
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();

        foreach (var (network, prefix) in Ranges)
        {
            if (peer.AddressFamily == network.AddressFamily && InSubnet(peer, network, prefix))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The client IP to trust for rate-limiting / audit. Honors <c>CF-Connecting-IP</c>
    /// ONLY when the socket peer is a verified Cloudflare egress IP; otherwise falls back to
    /// the socket peer. When the origin sits behind a loopback reverse proxy (nginx), the peer
    /// is loopback — we then trust <c>CF-Connecting-IP</c> if present (nginx only forwards what
    /// Cloudflare set; nginx itself never sets that header), else the first X-Forwarded-For hop.
    /// </summary>
    public static string ResolveTrustedClientIp(HttpContext ctx)
    {
        var validator = ctx.RequestServices.GetService(typeof(ICloudflareIpValidator)) as ICloudflareIpValidator;
        var peer = ctx.Connection.RemoteIpAddress;

        var trustHeader = peer is not null
            && (validator?.IsCloudflareEgress(peer) == true || IPAddress.IsLoopback(peer));

        if (trustHeader)
        {
            var cf = ctx.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();

            var xff = ctx.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(xff)) return xff.Split(',')[0].Trim();
        }

        return peer?.ToString() ?? "anon";
    }

    private static bool InSubnet(IPAddress ip, IPAddress network, int prefix)
    {
        var ipBytes = ip.GetAddressBytes();
        var netBytes = network.GetAddressBytes();
        if (ipBytes.Length != netBytes.Length) return false;

        int fullBytes = prefix / 8;
        int remBits = prefix % 8;

        for (int i = 0; i < fullBytes; i++)
            if (ipBytes[i] != netBytes[i]) return false;

        if (remBits == 0) return true;
        int mask = 0xFF << (8 - remBits) & 0xFF;
        return (ipBytes[fullBytes] & mask) == (netBytes[fullBytes] & mask);
    }

    private static (IPAddress, int)[] BuildRanges(string[] cidrs)
    {
        var result = new (IPAddress, int)[cidrs.Length];
        for (int i = 0; i < cidrs.Length; i++)
        {
            var slash = cidrs[i].IndexOf('/');
            var net = IPAddress.Parse(cidrs[i][..slash]);
            var prefix = int.Parse(cidrs[i][(slash + 1)..]);
            result[i] = (net, prefix);
        }
        return result;
    }
}
