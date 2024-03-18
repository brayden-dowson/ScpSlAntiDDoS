using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Net;
using System.Net.Sockets;

namespace TheRiptide.Patches
{
    [HarmonyPatch(typeof(NetManager), nameof(NetManager.OnMessageReceived))]
    public class NetManagerOnMessageReceivedPatch
    {
        public static int BadDataCount = 0;
        public static long BadDataBytes = 0;

        public static bool Prefix(NetManager __instance, NetPacket packet, IPEndPoint remoteEndPoint)
        {
            int size = packet.Size;
            if (__instance.EnableStatistics)
            {
                __instance.Statistics.IncrementPacketsReceived();
                __instance.Statistics.AddBytesReceived(size);
            }
            if (__instance._ntpRequests.Count > 0 && __instance._ntpRequests.TryGetValue(remoteEndPoint, out NtpRequest _))
            {
                if (packet.Size < 48)
                    return false;
                byte[] numArray = new byte[packet.Size];
                Buffer.BlockCopy((Array)packet.RawData, 0, (Array)numArray, 0, packet.Size);
                NtpPacket packet1 = NtpPacket.FromServerResponse(numArray, DateTime.UtcNow);
                try
                {
                    packet1.ValidateReply();
                }
                catch (InvalidOperationException ex)
                {
                    packet1 = (NtpPacket)null;
                }
                if (packet1 == null)
                    return false;
                __instance._ntpRequests.Remove(remoteEndPoint);
                __instance._ntpEventListener?.OnNtpResponse(packet1);
            }
            else
            {
                if (__instance._extraPacketLayer != null)
                {
                    int offset = 0;
                    __instance._extraPacketLayer.ProcessInboundPacket(ref remoteEndPoint, ref packet.RawData, ref offset, ref packet.Size);
                    if (packet.Size == 0)
                        return false;
                }
                if (!packet.Verify())
                {
                    BadDataCount++;
                    BadDataBytes += size;
                    //NetDebug.WriteError("[NM] DataReceived: bad!");
                    __instance.PoolRecycle(packet);
                }
                else
                {
                    switch (packet.Property)
                    {
                        case PacketProperty.ConnectRequest:
                            if (NetConnectRequestPacket.GetProtocolId(packet) != 13)
                            {
                                __instance.SendRawAndRecycle(__instance.PoolGetWithProperty(PacketProperty.InvalidProtocol), remoteEndPoint);
                                return false;
                            }
                            break;
                        case PacketProperty.UnconnectedMessage:
                            if (!__instance.UnconnectedMessagesEnabled)
                                return false;
                            __instance.CreateEvent(NetEvent.EType.ReceiveUnconnected, remoteEndPoint: remoteEndPoint, readerSource: packet);
                            return false;
                        case PacketProperty.Broadcast:
                            if (!__instance.BroadcastReceiveEnabled)
                                return false;
                            __instance.CreateEvent(NetEvent.EType.Broadcast, remoteEndPoint: remoteEndPoint, readerSource: packet);
                            return false;
                        case PacketProperty.NatMessage:
                            if (!__instance.NatPunchEnabled)
                                return false;
                            __instance.NatPunchModule.ProcessMessage(remoteEndPoint, packet);
                            return false;
                    }
                    __instance._peersLock.EnterReadLock();
                    NetPeer netPeer;
                    bool flag1 = __instance._peersDict.TryGetValue(remoteEndPoint, out netPeer);
                    __instance._peersLock.ExitReadLock();
                    if (flag1 && __instance.EnableStatistics)
                    {
                        netPeer.Statistics.IncrementPacketsReceived();
                        netPeer.Statistics.AddBytesReceived((long)size);
                    }
                    switch (packet.Property)
                    {
                        case PacketProperty.ConnectRequest:
                            NetConnectRequestPacket connRequest = NetConnectRequestPacket.FromData(packet);
                            if (connRequest == null)
                                break;
                            __instance.ProcessConnectRequest(remoteEndPoint, netPeer, connRequest);
                            break;
                        case PacketProperty.ConnectAccept:
                            if (!flag1)
                                break;
                            NetConnectAcceptPacket packet2 = NetConnectAcceptPacket.FromData(packet);
                            if (packet2 == null || !netPeer.ProcessConnectAccept(packet2))
                                break;
                            __instance.CreateEvent(NetEvent.EType.Connect, netPeer);
                            break;
                        case PacketProperty.Disconnect:
                            if (flag1)
                            {
                                DisconnectResult disconnectResult = netPeer.ProcessDisconnect(packet);
                                if (disconnectResult == DisconnectResult.None)
                                {
                                    __instance.PoolRecycle(packet);
                                    break;
                                }
                                __instance.DisconnectPeerForce(netPeer, disconnectResult == DisconnectResult.Disconnect ? DisconnectReason.RemoteConnectionClose : DisconnectReason.ConnectionRejected, SocketError.Success, packet);
                            }
                            else
                                __instance.PoolRecycle(packet);
                            __instance.SendRawAndRecycle(__instance.PoolGetWithProperty(PacketProperty.ShutdownOk), remoteEndPoint);
                            break;
                        case PacketProperty.PeerNotFound:
                            if (flag1)
                            {
                                if (netPeer.ConnectionState != ConnectionState.Connected)
                                    break;
                                if (packet.Size == 1)
                                {
                                    netPeer.ResetMtu();
                                    __instance.SendRaw(NetConnectAcceptPacket.MakeNetworkChanged(netPeer), remoteEndPoint);
                                    break;
                                }
                                if (packet.Size != 2 || packet.RawData[1] != (byte)1)
                                    break;
                                __instance.DisconnectPeerForce(netPeer, DisconnectReason.PeerNotFound, SocketError.Success, (NetPacket)null);
                                break;
                            }
                            if (packet.Size <= 1)
                                break;
                            bool flag2 = false;
                            if (__instance.AllowPeerAddressChange)
                            {
                                NetConnectAcceptPacket connectAcceptPacket = NetConnectAcceptPacket.FromData(packet);
                                if (connectAcceptPacket != null && connectAcceptPacket.PeerNetworkChanged && connectAcceptPacket.PeerId < __instance._peersArray.Length)
                                {
                                    __instance._peersLock.EnterUpgradeableReadLock();
                                    NetPeer peers = __instance._peersArray[connectAcceptPacket.PeerId];
                                    if (peers != null && peers.ConnectTime == connectAcceptPacket.ConnectionTime && (int)peers.ConnectionNum == (int)connectAcceptPacket.ConnectionNumber)
                                    {
                                        if (peers.ConnectionState == ConnectionState.Connected)
                                        {
                                            peers.InitiateEndPointChange();
                                            if (__instance._peerAddressChangedListener != null)
                                                __instance.CreateEvent(NetEvent.EType.PeerAddressChanged, peers, remoteEndPoint);
                                        }
                                        flag2 = true;
                                    }
                                    __instance._peersLock.ExitUpgradeableReadLock();
                                }
                            }
                            __instance.PoolRecycle(packet);
                            if (flag2)
                                break;
                            NetPacket withProperty = __instance.PoolGetWithProperty(PacketProperty.PeerNotFound, 1);
                            withProperty.RawData[1] = (byte)1;
                            __instance.SendRawAndRecycle(withProperty, remoteEndPoint);
                            break;
                        case PacketProperty.InvalidProtocol:
                            if (!flag1 || netPeer.ConnectionState != ConnectionState.Outgoing)
                                break;
                            __instance.DisconnectPeerForce(netPeer, DisconnectReason.InvalidProtocol, SocketError.Success, (NetPacket)null);
                            break;
                        default:
                            if (flag1)
                            {
                                netPeer.ProcessPacket(packet);
                                break;
                            }
                            __instance.SendRawAndRecycle(__instance.PoolGetWithProperty(PacketProperty.PeerNotFound), remoteEndPoint);
                            break;
                    }
                }
            }
            return false;
        }
    }
}
