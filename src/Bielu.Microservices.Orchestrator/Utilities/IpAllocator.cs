using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Bielu.Microservices.Orchestrator.Models;

namespace Bielu.Microservices.Orchestrator.Utilities;

/// <summary>
/// Allocates the first free IP address inside a network's IPAM subnet given the
/// set of IPs already in use by existing endpoints. Supports IPv4 and IPv6.
/// </summary>
/// <remarks>
/// Allocation rules (mirroring Docker's default IPAM behaviour as closely as is
/// possible client-side):
/// <list type="bullet">
///   <item>Network and broadcast addresses (first / last in the subnet) are skipped.</item>
///   <item>The IPAM <c>Gateway</c> and any <c>AuxAddress</c> entries are skipped.</item>
///   <item>Addresses already assigned to existing endpoints are skipped.</item>
///   <item>If <c>IPRange</c> is set, allocation is constrained to that sub-range
///         (otherwise the full subnet is used).</item>
/// </list>
/// This is a best-effort, race-prone helper; the runtime remains the source of
/// truth and may reject the chosen IP if it has been concurrently claimed.
/// </remarks>
public static class IpAllocator
{
    /// <summary>
    /// Tries to find the first free IPv4 address from the given IPAM configs and
    /// the set of in-use addresses. Returns <c>null</c> if no IPv4 subnet is
    /// configured or no free address is available.
    /// </summary>
    public static string? TryAllocateNextFreeIPv4(
        IEnumerable<NetworkIpamConfig>? ipamConfigs,
        IEnumerable<string>? inUseAddresses)
        => TryAllocateNextFree(ipamConfigs, inUseAddresses, AddressFamily.InterNetwork);

    /// <summary>
    /// Tries to find the first free IPv6 address from the given IPAM configs and
    /// the set of in-use addresses. Returns <c>null</c> if no IPv6 subnet is
    /// configured or no free address is available.
    /// </summary>
    public static string? TryAllocateNextFreeIPv6(
        IEnumerable<NetworkIpamConfig>? ipamConfigs,
        IEnumerable<string>? inUseAddresses)
        => TryAllocateNextFree(ipamConfigs, inUseAddresses, AddressFamily.InterNetworkV6);

    private static string? TryAllocateNextFree(
        IEnumerable<NetworkIpamConfig>? ipamConfigs,
        IEnumerable<string>? inUseAddresses,
        AddressFamily family)
    {
        if (ipamConfigs == null)
        {
            return null;
        }

        var used = BuildInUseSet(inUseAddresses, family);

        foreach (var config in ipamConfigs)
        {
            if (string.IsNullOrWhiteSpace(config.Subnet))
            {
                continue;
            }

            if (!TryParseCidr(config.Subnet, out var subnetIp, out var prefix) ||
                subnetIp.AddressFamily != family)
            {
                continue;
            }

            var (networkInt, broadcastInt) = GetSubnetBounds(subnetIp, prefix);

            // Constrain to IPRange if provided.
            var startInt = networkInt + 1;            // skip network address
            var endInt = broadcastInt - 1;            // skip broadcast/last
            if (family == AddressFamily.InterNetworkV6)
            {
                // IPv6 has no broadcast; still avoid the all-zero address.
                endInt = broadcastInt;
            }

            if (!string.IsNullOrWhiteSpace(config.IPRange) &&
                TryParseCidr(config.IPRange, out var rangeIp, out var rangePrefix) &&
                rangeIp.AddressFamily == family)
            {
                var (rStart, rEnd) = GetSubnetBounds(rangeIp, rangePrefix);
                if (rStart > startInt) startInt = rStart;
                if (rEnd < endInt) endInt = rEnd;
            }

            // Build excluded-IP set: in-use + gateway + aux addresses.
            var excluded = new HashSet<BigInteger>(used);
            AddExcluded(excluded, config.Gateway, family);
            if (config.AuxAddress != null)
            {
                foreach (var aux in config.AuxAddress.Values)
                {
                    AddExcluded(excluded, aux, family);
                }
            }

            for (var candidate = startInt; candidate <= endInt; candidate++)
            {
                if (excluded.Contains(candidate))
                {
                    continue;
                }

                return BigIntegerToIp(candidate, family).ToString();
            }
        }

        return null;
    }

    private static HashSet<BigInteger> BuildInUseSet(IEnumerable<string>? addresses, AddressFamily family)
    {
        var set = new HashSet<BigInteger>();
        if (addresses == null)
        {
            return set;
        }

        foreach (var raw in addresses)
        {
            AddExcluded(set, raw, family);
        }

        return set;
    }

    private static void AddExcluded(HashSet<BigInteger> set, string? value, AddressFamily family)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // Strip optional CIDR suffix (e.g. "172.19.0.2/16").
        var slash = value!.IndexOf('/');
        var ipPart = slash >= 0 ? value[..slash] : value;

        if (IPAddress.TryParse(ipPart, out var ip) && ip.AddressFamily == family)
        {
            set.Add(IpToBigInteger(ip));
        }
    }

    private static bool TryParseCidr(string cidr, out IPAddress ip, out int prefix)
    {
        ip = IPAddress.None;
        prefix = 0;

        if (string.IsNullOrWhiteSpace(cidr))
        {
            return false;
        }

        var parts = cidr.Split('/');
        if (!IPAddress.TryParse(parts[0], out var parsed))
        {
            return false;
        }

        var maxPrefix = parsed.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (parts.Length == 1)
        {
            ip = parsed;
            prefix = maxPrefix;
            return true;
        }

        if (!int.TryParse(parts[1], out var p) || p < 0 || p > maxPrefix)
        {
            return false;
        }

        ip = parsed;
        prefix = p;
        return true;
    }

    private static (BigInteger Network, BigInteger Broadcast) GetSubnetBounds(IPAddress ip, int prefix)
    {
        var totalBits = ip.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        var ipInt = IpToBigInteger(ip);

        if (prefix == totalBits)
        {
            return (ipInt, ipInt);
        }

        var hostBits = totalBits - prefix;
        var hostMask = (BigInteger.One << hostBits) - 1;
        var network = ipInt & ~hostMask;
        // Re-mask to the address space size to avoid sign issues.
        var spaceMask = (BigInteger.One << totalBits) - 1;
        network &= spaceMask;
        var broadcast = (network | hostMask) & spaceMask;
        return (network, broadcast);
    }

    private static BigInteger IpToBigInteger(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        // BigInteger expects little-endian; IP bytes are big-endian. Append a
        // zero byte to keep the number unsigned.
        Array.Reverse(bytes);
        var withSign = new byte[bytes.Length + 1];
        Array.Copy(bytes, withSign, bytes.Length);
        return new BigInteger(withSign);
    }

    private static IPAddress BigIntegerToIp(BigInteger value, AddressFamily family)
    {
        var size = family == AddressFamily.InterNetworkV6 ? 16 : 4;
        var raw = value.ToByteArray();
        var bytes = new byte[size];
        var copyLen = Math.Min(raw.Length, size);
        Array.Copy(raw, 0, bytes, 0, copyLen);
        Array.Reverse(bytes);
        return new IPAddress(bytes);
    }
}
