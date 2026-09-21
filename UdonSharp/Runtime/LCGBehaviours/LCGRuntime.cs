using JetBrains.Annotations;
using System;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace UdonSharp
{
    /// <summary>
    /// Scene-wide router for compiler-generated LCG packet frames.
    /// </summary>
    [PublicAPI]
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public sealed class LCGRuntime : UdonSharpBehaviour
    {
        public const int ProtocolVersion = 2;
        public const int HeaderSize = 28;
        public const int MaxFrameBytes = 12 * 1024;
        public const int MaxPendingFieldPackets = 128;
        public const int MaxPendingMethodPackets = 128;
        public const int MaxPacketsPerFrame = 16;

        [SerializeField] private LCGRuntimePlayer mailboxTemplate;
        [SerializeField] private LCGNetworkZone[] zones = new LCGNetworkZone[0];
        [SerializeField] private UdonBehaviour[] receivers = new UdonBehaviour[0];
        [SerializeField] private bool[] playerObjectReceivers = new bool[0];
        [SerializeField] private string[] packetAddresses = new string[0];
        [SerializeField] private int[] packetReceiverIds = new int[0];
        [SerializeField] private int[] packetAuthorities = new int[0];
        [SerializeField] private int[] packetKinds = new int[0];
        [SerializeField] private int[] packetValueTypes = new int[0];
        [SerializeField] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  private object[] packetDefaultValues = new object[0];
        [SerializeField] private int[] packetParameterOffsets = new int[0];
        [SerializeField] private int[] packetParameterCounts = new int[0];
        [SerializeField] private string[] packetParameterNames = new string[0];
        [SerializeField] private int[] packetParameterTypes = new int[0];
        [SerializeField] private string[] packetCallbackEvents = new string[0];
        [SerializeField] private string[] packetCallbackParameters = new string[0];

        // Compiler-written send registers. Their names are part of the compiler/runtime ABI.
        [HideInInspector] public UdonBehaviour __lcgSenderReceiver;
        [HideInInspector] public int __lcgSenderReceiverId;
        [HideInInspector] public int __lcgSenderZoneId;
        [HideInInspector] public int __lcgSenderEpoch;
        [HideInInspector] public int __lcgSenderType;
        [HideInInspector] public string __lcgSenderAddress;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderValue;
        [HideInInspector] public bool __lcgSenderForce;
        [HideInInspector] public int __lcgSenderTargetMode;
        [HideInInspector] public VRCPlayerApi __lcgSenderPlayer;
        [HideInInspector] public int __lcgSenderArgCount;
        [HideInInspector] public int __lcgSenderArgType0;
        [HideInInspector] public int __lcgSenderArgType1;
        [HideInInspector] public int __lcgSenderArgType2;
        [HideInInspector] public int __lcgSenderArgType3;
        [HideInInspector] public int __lcgSenderArgType4;
        [HideInInspector] public int __lcgSenderArgType5;
        [HideInInspector] public int __lcgSenderArgType6;
        [HideInInspector] public int __lcgSenderArgType7;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg0;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg1;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg2;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg3;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg4;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg5;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg6;
        [HideInInspector] [VRC.Udon.Serialization.OdinSerializer.OdinSerialize] /* UdonSharp auto-upgrade: serialization */  public object __lcgSenderArg7;
        [HideInInspector] public GameObject __lcgObjectSyncTarget;

        private int sequence;
#if LCG_NETWORK_DIAGNOSTICS
        private int deliveryDiagnostics;

        private void TraceDelivery(int flag, string message)
        {
            if ((deliveryDiagnostics & flag) != 0)
                return;
            deliveryDiagnostics |= flag;
            Debug.Log("[LCG sync] " + message);
        }
#endif
        private int pendingFieldCount;
        private bool fieldFlushScheduled;
        private int[] pendingFieldReceiverIds = new int[MaxPendingFieldPackets];
        private int[] pendingFieldOwnerIds = new int[MaxPendingFieldPackets];
        private int[] pendingFieldZoneIds = new int[MaxPendingFieldPackets];
        private int[] pendingFieldTypes = new int[MaxPendingFieldPackets];
        private string[] pendingFieldAddresses = new string[MaxPendingFieldPackets];
        private object[] pendingFieldPayloads = new object[MaxPendingFieldPackets];
        private int sentFieldCount;
        private int[] sentFieldReceiverIds = new int[MaxPendingFieldPackets];
        private int[] sentFieldOwnerIds = new int[MaxPendingFieldPackets];
        private int[] sentFieldZoneIds = new int[MaxPendingFieldPackets];
        private string[] sentFieldAddresses = new string[MaxPendingFieldPackets];
        private object[] sentFieldPayloads = new object[MaxPendingFieldPackets];
        private int pendingMethodHead;
        private int pendingMethodTail;
        private int pendingMethodCount;
        private bool methodFlushScheduled;
        private object[] pendingMethodFrames = new object[MaxPendingMethodPackets];
        private int[] pendingMethodZoneIds = new int[MaxPendingMethodPackets];
        private int[] pendingMethodTargets = new int[MaxPendingMethodPackets];
        private VRCPlayerApi[] pendingMethodPlayers = new VRCPlayerApi[MaxPendingMethodPackets];
        private int[] receivePlayerIds = new int[0];
        private int[] receiveZoneIds = new int[0];
        private int[] receiveReceiverIds = new int[0];
        private int[] receiveOwnerIds = new int[0];
        private int[] receiveEpochs = new int[0];
        private int[] receiveSequences = new int[0];

        internal void Configure(LCGRuntimePlayer playerMailbox, LCGNetworkZone[] sceneZones,
            UdonBehaviour[] sceneReceivers, string[] addresses = null, int[] addressReceiverIds = null,
            int[] authorities = null, int[] kinds = null, int[] parameterOffsets = null,
            int[] parameterCounts = null, string[] parameterNames = null, int[] parameterTypes = null,
            string[] callbackEvents = null, string[] callbackParameters = null, int[] valueTypes = null)
        {
            mailboxTemplate = playerMailbox;
            zones = sceneZones ?? new LCGNetworkZone[0];
            receivers = sceneReceivers ?? new UdonBehaviour[0];
            packetAddresses = addresses ?? new string[0];
            packetReceiverIds = addressReceiverIds ?? new int[0];
            packetAuthorities = authorities ?? new int[0];
            packetKinds = kinds ?? new int[0];
            packetValueTypes = valueTypes ?? new int[0];
            packetParameterOffsets = parameterOffsets ?? new int[0];
            packetParameterCounts = parameterCounts ?? new int[0];
            packetParameterNames = parameterNames ?? new string[0];
            packetParameterTypes = parameterTypes ?? new int[0];
            packetCallbackEvents = callbackEvents ?? new string[0];
            packetCallbackParameters = callbackParameters ?? new string[0];
        }

        internal void SetPacketDefaultValues(object[] defaultValues)
        {
            packetDefaultValues = defaultValues ?? new object[0];
        }

        internal void SetPlayerObjectReceivers(bool[] flags)
        {
            playerObjectReceivers = flags ?? new bool[0];
        }

        private bool IsPlayerObjectReceiver(int receiverId)
        {
            return receiverId >= 0 && receiverId < playerObjectReceivers.Length && playerObjectReceivers[receiverId];
        }

        // -1 identifies a scene receiver; -2 rejects an unbound or missing clone.
        private int GetReceiverOwnerId(int receiverId, UdonBehaviour instance)
        {
            if (receiverId < 0 || receiverId >= receivers.Length)
                return -2;
            if (!IsPlayerObjectReceiver(receiverId))
                return -1;
            if (instance == null || instance == receivers[receiverId])
                return -2;
            VRCPlayerApi owner = Networking.GetOwner(instance.gameObject);
            if (!Utilities.IsValid(owner) || ResolveReceiver(receiverId, owner.playerId) != instance)
                return -2;
            return owner.playerId;
        }

        private UdonBehaviour ResolveReceiver(int receiverId, int ownerId)
        {
            if (receiverId < 0 || receiverId >= receivers.Length || receivers[receiverId] == null)
                return null;
            if (!IsPlayerObjectReceiver(receiverId))
                return ownerId == -1 ? receivers[receiverId] : null;
            if (ownerId < 0)
                return null;
            VRCPlayerApi owner = VRCPlayerApi.GetPlayerById(ownerId);
            if (!Utilities.IsValid(owner))
                return null;
            return (UdonBehaviour)Networking.FindComponentInPlayerObjects(owner, receivers[receiverId]);
        }

        public void RestoreZoneDefaults(int zoneId)
        {
            for (int i = 0; i < packetAddresses.Length; i++)
            {
                if (i >= packetKinds.Length || i >= packetReceiverIds.Length || i >= packetDefaultValues.Length ||
                    packetKinds[i] != 0)
                    continue;
                int receiverId = packetReceiverIds[i];
                if (receiverId < 0 || receiverId >= receivers.Length || receivers[receiverId] == null)
                    continue;
                object receiverZone = receivers[receiverId].GetProgramVariable("__lcgZoneId");
                if (receiverZone != null && receiverZone.GetType() == typeof(int) && (int)receiverZone == zoneId)
                    receivers[receiverId].SetProgramVariable(packetAddresses[i], packetDefaultValues[i]);
            }
        }

        public void __lcgSendField()
        {
            int ownerId = GetReceiverOwnerId(__lcgSenderReceiverId, __lcgSenderReceiver);
            if (ownerId < -1)
                return;
            byte[] payload = EncodeValue(__lcgSenderType, __lcgSenderValue);
            if (payload == null)
                return;

            int pending = FindFieldKey(pendingFieldReceiverIds, pendingFieldOwnerIds, pendingFieldZoneIds, pendingFieldAddresses,
                pendingFieldCount, __lcgSenderReceiverId, ownerId, __lcgSenderZoneId, __lcgSenderAddress);
            if (pending >= 0)
            {
                byte[] previous = (byte[])pendingFieldPayloads[pending];
                if (!__lcgSenderForce && ByteArraysEqual(previous, payload))
                    return;
                pendingFieldTypes[pending] = __lcgSenderType;
                pendingFieldPayloads[pending] = payload;
            }
            else
            {
                int sent = FindFieldKey(sentFieldReceiverIds, sentFieldOwnerIds, sentFieldZoneIds, sentFieldAddresses,
                    sentFieldCount, __lcgSenderReceiverId, ownerId, __lcgSenderZoneId, __lcgSenderAddress);
                if (!__lcgSenderForce && sent >= 0 && ByteArraysEqual((byte[])sentFieldPayloads[sent], payload))
                    return;
                if (pendingFieldCount >= MaxPendingFieldPackets)
                    return;
                pending = pendingFieldCount++;
                pendingFieldReceiverIds[pending] = __lcgSenderReceiverId;
                pendingFieldOwnerIds[pending] = ownerId;
                pendingFieldZoneIds[pending] = __lcgSenderZoneId;
                pendingFieldTypes[pending] = __lcgSenderType;
                pendingFieldAddresses[pending] = __lcgSenderAddress;
                pendingFieldPayloads[pending] = payload;
            }

            __lcgSenderForce = false;
            if (!fieldFlushScheduled)
            {
                fieldFlushScheduled = true;
                SendCustomEventDelayedFrames(nameof(__lcgFlushFields), 1);
            }
        }

        public void __lcgRequestObjectSync()
        {
            GameObject target = __lcgObjectSyncTarget;
            __lcgObjectSyncTarget = null;
            if (target == null)
                return;

            LCGManualObjectSync sync = target.GetComponent<LCGManualObjectSync>();
            if (sync != null)
                sync.RequestObjectSync();
        }

        public void __lcgFlushFields()
        {
            fieldFlushScheduled = false;
            int count = pendingFieldCount > MaxPacketsPerFrame ? MaxPacketsPerFrame : pendingFieldCount;
            for (int i = 0; i < count; i++)
            {
                LCGNetworkZone zone = FindZone(pendingFieldZoneIds[i]);
                int epoch = zone != null ? zone.Epoch : 0;
                byte[] payload = (byte[])pendingFieldPayloads[i];
                byte[] frame = BuildFieldFrameFromPayload(pendingFieldReceiverIds[i], pendingFieldZoneIds[i], epoch,
                    sequence++, pendingFieldTypes[i], pendingFieldAddresses[i], payload);
                if (frame != null && ResolveReceiver(pendingFieldReceiverIds[i], pendingFieldOwnerIds[i]) != null)
                {
                    WriteInt32(frame, 24, pendingFieldOwnerIds[i]);
                    BroadcastFrame(frame, pendingFieldZoneIds[i], false);
                    RememberSentField(pendingFieldReceiverIds[i], pendingFieldOwnerIds[i], pendingFieldZoneIds[i],
                        pendingFieldAddresses[i], payload);
                }
                pendingFieldPayloads[i] = null;
            }
            int remaining = pendingFieldCount - count;
            for (int i = 0; i < remaining; i++)
            {
                int source = i + count;
                pendingFieldReceiverIds[i] = pendingFieldReceiverIds[source];
                pendingFieldOwnerIds[i] = pendingFieldOwnerIds[source];
                pendingFieldZoneIds[i] = pendingFieldZoneIds[source];
                pendingFieldTypes[i] = pendingFieldTypes[source];
                pendingFieldAddresses[i] = pendingFieldAddresses[source];
                pendingFieldPayloads[i] = pendingFieldPayloads[source];
                pendingFieldPayloads[source] = null;
            }
            pendingFieldCount = remaining;
            if (remaining > 0)
            {
                fieldFlushScheduled = true;
                SendCustomEventDelayedFrames(nameof(__lcgFlushFields), 1);
            }
        }

        public void __lcgSendMethod()
        {
            int ownerId = GetReceiverOwnerId(__lcgSenderReceiverId, __lcgSenderReceiver);
            if (ownerId < -1)
                return;
            if (__lcgSenderArgCount < 0 || __lcgSenderArgCount > 8)
                return;

            LCGNetworkZone zone = FindZone(__lcgSenderZoneId);
            __lcgSenderEpoch = zone != null ? zone.Epoch : 0;
            int[] types = new int[8]
            {
                __lcgSenderArgType0, __lcgSenderArgType1, __lcgSenderArgType2, __lcgSenderArgType3,
                __lcgSenderArgType4, __lcgSenderArgType5, __lcgSenderArgType6, __lcgSenderArgType7
            };
            object[] values = new object[8]
            {
                __lcgSenderArg0, __lcgSenderArg1, __lcgSenderArg2, __lcgSenderArg3,
                __lcgSenderArg4, __lcgSenderArg5, __lcgSenderArg6, __lcgSenderArg7
            };
            byte[] frame = BuildMethodFrame(__lcgSenderReceiverId, __lcgSenderZoneId, __lcgSenderEpoch,
                0, __lcgSenderAddress, __lcgSenderArgCount, types, values);
            if (frame == null)
                return;

            WriteInt32(frame, 24, ownerId);
            if (pendingMethodCount >= MaxPendingMethodPackets)
                return;
            int queued = pendingMethodTail;
            pendingMethodFrames[queued] = frame;
            pendingMethodZoneIds[queued] = __lcgSenderZoneId;
            pendingMethodTargets[queued] = __lcgSenderTargetMode;
            pendingMethodPlayers[queued] = __lcgSenderPlayer;
            pendingMethodTail = (pendingMethodTail + 1) % MaxPendingMethodPackets;
            pendingMethodCount++;
            if (!methodFlushScheduled)
            {
                methodFlushScheduled = true;
                SendCustomEventDelayedFrames(nameof(__lcgFlushMethods), 1);
            }
        }

        public void __lcgFlushMethods()
        {
            methodFlushScheduled = false;
            int count = pendingMethodCount > MaxPacketsPerFrame ? MaxPacketsPerFrame : pendingMethodCount;
            for (int i = 0; i < count; i++)
            {
                int queued = pendingMethodHead;
                byte[] frame = (byte[])pendingMethodFrames[queued];
                DispatchMethodFrame(frame, pendingMethodZoneIds[queued], pendingMethodTargets[queued],
                    pendingMethodPlayers[queued]);
                pendingMethodFrames[queued] = null;
                pendingMethodPlayers[queued] = null;
                pendingMethodHead = (pendingMethodHead + 1) % MaxPendingMethodPackets;
                pendingMethodCount--;
            }
            if (pendingMethodCount > 0)
            {
                methodFlushScheduled = true;
                SendCustomEventDelayedFrames(nameof(__lcgFlushMethods), 1);
            }
        }

        private void DispatchMethodFrame(byte[] frame, int zoneId, int targetMode, VRCPlayerApi player)
        {
            if (frame == null)
                return;
            UdonBehaviour receiver = ResolveReceiver(ReadInt32(frame, 12), ReadInt32(frame, 24));
            if (receiver == null)
                return;
            LCGNetworkZone zone = FindZone(zoneId);
            WriteInt32(frame, 8, zone != null ? zone.Epoch : 0);
            WriteInt32(frame, 16, sequence++);
            if (targetMode == -1)
            {
                SendFrame(player, frame);
                return;
            }

            NetworkEventTarget target = (NetworkEventTarget)targetMode;
            if (target == NetworkEventTarget.All)
                BroadcastFrame(frame, zoneId, true);
            else if (target == NetworkEventTarget.Others)
                BroadcastFrame(frame, zoneId, false);
            else if (target == NetworkEventTarget.Self)
                SendFrame(Networking.LocalPlayer, frame);
            else if (target == NetworkEventTarget.Owner)
            {
                SendFrame(Networking.GetOwner(receiver.gameObject), frame);
            }
        }

        public bool SendFrame(VRCPlayerApi player, byte[] frame)
        {
            if (!Utilities.IsValid(player) || !ValidateFrame(frame) || mailboxTemplate == null)
                return false;

            if (player.isLocal)
            {
                ReceiveFrame(frame, player);
                return true;
            }

            Component found = Networking.FindComponentInPlayerObjects(player, mailboxTemplate);
            LCGRuntimePlayer mailbox = (LCGRuntimePlayer)found;
            if (mailbox == null)
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(1, "No mailbox for recipient " + player.playerId);
#endif
                return false;
            }

#if LCG_NETWORK_DIAGNOSTICS
            TraceDelivery(2, "Sending to mailbox for recipient " + player.playerId);
#endif

            mailbox.SendCustomNetworkEvent(NetworkEventTarget.Owner,
                nameof(LCGRuntimePlayer.ReceiveFrame), frame);
            return true;
        }

        public int BroadcastFrame(byte[] frame, int zoneId, bool includeSelf)
        {
            if (!ValidateFrame(frame))
                return 0;

            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            int sent = 0;
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi player = players[i];
                if (!Utilities.IsValid(player) || (!includeSelf && player.isLocal))
                    continue;
                if (zoneId != 0 && !IsZoneMember(zoneId, player))
                    continue;
                if (SendFrame(player, frame))
                    sent++;
            }
#if LCG_NETWORK_DIAGNOSTICS
            TraceDelivery(sent > 0 ? 4 : 8, "Zone broadcast recipients=" + sent + ", zone=" + zoneId);
#endif
            return sent;
        }

        internal void ReceiveFrame(byte[] frame, VRCPlayerApi sender)
        {
            if (!Utilities.IsValid(sender) || !ValidateFrame(frame))
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(16, "Rejected invalid sender or frame");
#endif
                return;
            }

            int zoneId = ReadInt32(frame, 4);
            int frameEpoch = ReadInt32(frame, 8);
            int receiverId = ReadInt32(frame, 12);
            int ownerId = ReadInt32(frame, 24);
            int frameSequence = ReadInt32(frame, 16);
            if (frameEpoch < 0 || frameSequence < 0)
                return;
            if (zoneId != 0 && !IsZoneMember(zoneId, sender))
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(32, "Rejected sender outside zone " + zoneId);
#endif
                return;
            }
            if (zoneId != 0 && !IsZoneMember(zoneId, Networking.LocalPlayer))
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(64, "Rejected receiver outside zone " + zoneId);
#endif
                return;
            }
            if (receiverId < 0 || receiverId >= receivers.Length)
                return;
            UdonBehaviour receiver = ResolveReceiver(receiverId, ownerId);
            if (receiver == null)
                return;
            object registeredZone = receiver.GetProgramVariable("__lcgZoneId");
            if (registeredZone == null || registeredZone.GetType() != typeof(int) || (int)registeredZone != zoneId)
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(128, "Rejected mismatched receiver zone binding");
#endif
                return;
            }

            int nameLength = ReadUInt16(frame, 20);
            int payloadLength = ReadUInt16(frame, 22);
            if (HeaderSize + nameLength + payloadLength != frame.Length)
                return;

            string address = DecodeText(frame, HeaderSize, nameLength);
            if (address == null)
                return;
            int kind = frame[1];
            if (kind == 2)
            {
                if (nameLength != 0 || payloadLength != 0 || !Networking.IsOwner(receiver.gameObject) ||
                    !AcceptSequence(sender.playerId, zoneId, receiverId, ownerId, frameEpoch, frameSequence))
                    return;
                SendFieldSnapshot(receiverId, receiver, zoneId, sender);
                return;
            }

            int registration = FindRegistration(receiverId, address);
            if (registration < 0 || !CheckAuthority(packetAuthorities[registration], sender, receiver.gameObject))
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(256, "Rejected packet registration or sender authority: " + address);
#endif
                return;
            }
            if (registration >= packetKinds.Length || packetKinds[registration] != kind)
                return;
            if (!AcceptSequence(sender.playerId, zoneId, receiverId, ownerId, frameEpoch, frameSequence))
                return;

            int payloadOffset = HeaderSize + nameLength;
            if (kind == 0)
            {
                if (registration >= packetValueTypes.Length || frame[2] != packetValueTypes[registration])
                    return;
                object decoded = DecodeValue(frame[2], frame, payloadOffset, payloadLength);
                bool nullArray = (frame[2] & (byte)LCGPacketType.ArrayFlag) != 0 && payloadLength == 4 &&
                                 ReadInt32(frame, payloadOffset) == -1;
                bool nullString = frame[2] == (byte)LCGPacketType.String && payloadLength == 1 &&
                                  frame[payloadOffset] == 0;
                if (decoded == null && !nullString && !nullArray)
                    return;
                receiver.SetProgramVariable(address, decoded);
                if (registration < packetCallbackEvents.Length && registration < packetCallbackParameters.Length &&
                    !string.IsNullOrEmpty(packetCallbackEvents[registration]))
                {
                    receiver.SetProgramVariable(packetCallbackParameters[registration], sender);
                    receiver.SendCustomEvent(packetCallbackEvents[registration]);
                }
                return;
            }

            if (kind == 1 && ApplyMethodPayload(receiver, registration, frame, payloadOffset, payloadLength))
            {
#if LCG_NETWORK_DIAGNOSTICS
                TraceDelivery(512, "Applying packet method: " + address);
#endif
                receiver.SetProgramVariable("__lcgPacketSender", sender);
                receiver.SendCustomEvent(address);
            }
        }

        public void RequestZoneSnapshot(int zoneId, VRCPlayerApi player)
        {
            LCGNetworkZone zone = FindZone(zoneId);
            if (zone == null || !zone.Contains(player))
                return;

            for (int receiverId = 0; receiverId < receivers.Length; receiverId++)
            {
                UdonBehaviour receiver = receivers[receiverId];
                if (receiver == null || !HasFieldRegistration(receiverId))
                    continue;
                object receiverZone = receiver.GetProgramVariable("__lcgZoneId");
                if (receiverZone == null || receiverZone.GetType() != typeof(int) || (int)receiverZone != zoneId)
                    continue;
                if (Networking.IsOwner(receiver.gameObject))
                    SendFieldSnapshot(receiverId, receiver, zoneId, player);
                else if (player.isLocal)
                {
                    byte[] request = BuildSnapshotRequestFrame(receiverId, zoneId, zone.Epoch, sequence++);
                    SendFrame(Networking.GetOwner(receiver.gameObject), request);
                }
            }
            zone.SendSnapshot(player);
        }

        public bool IsZoneMember(int zoneId, VRCPlayerApi player)
        {
            LCGNetworkZone zone = FindZone(zoneId);
            return zone != null && zone.Contains(player);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !Utilities.IsValid(Networking.LocalPlayer))
                return;
            for (int receiverId = 0; receiverId < receivers.Length; receiverId++)
            {
                if (!IsPlayerObjectReceiver(receiverId) || !HasFieldRegistration(receiverId))
                    continue;
                if (!player.isLocal)
                {
                    UdonBehaviour localClone = ResolveReceiver(receiverId, Networking.LocalPlayer.playerId);
                    if (localClone != null)
                        SendFieldSnapshot(receiverId, localClone, 0, player);
                }

                // Ask existing owners after our clones are restored. For a newly
                // restored remote player, request only that player's clone.
                VRCPlayerApi[] owners = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
                VRCPlayerApi.GetPlayers(owners);
                for (int i = 0; i < owners.Length; i++)
                {
                    VRCPlayerApi owner = owners[i];
                    if (!Utilities.IsValid(owner) || owner.isLocal || (!player.isLocal && owner != player))
                        continue;
                    byte[] request = BuildSnapshotRequestFrame(receiverId, 0, 0, sequence++);
                    WriteInt32(request, 24, owner.playerId);
                    SendFrame(owner, request);
                }
            }
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            int removedPlayerId = player.playerId;
            // Clear clone-specific suppression and queued work before a player ID
            // can be reused. Null method slots are safely consumed by the FIFO.
            for (int i = sentFieldCount - 1; i >= 0; i--)
            {
                if (sentFieldOwnerIds[i] != removedPlayerId)
                    continue;
                int last = --sentFieldCount;
                sentFieldReceiverIds[i] = sentFieldReceiverIds[last];
                sentFieldOwnerIds[i] = sentFieldOwnerIds[last];
                sentFieldZoneIds[i] = sentFieldZoneIds[last];
                sentFieldAddresses[i] = sentFieldAddresses[last];
                sentFieldPayloads[i] = sentFieldPayloads[last];
                sentFieldPayloads[last] = null;
            }
            for (int i = pendingFieldCount - 1; i >= 0; i--)
            {
                if (pendingFieldOwnerIds[i] != removedPlayerId)
                    continue;
                int last = --pendingFieldCount;
                pendingFieldReceiverIds[i] = pendingFieldReceiverIds[last];
                pendingFieldOwnerIds[i] = pendingFieldOwnerIds[last];
                pendingFieldZoneIds[i] = pendingFieldZoneIds[last];
                pendingFieldTypes[i] = pendingFieldTypes[last];
                pendingFieldAddresses[i] = pendingFieldAddresses[last];
                pendingFieldPayloads[i] = pendingFieldPayloads[last];
                pendingFieldPayloads[last] = null;
            }
            for (int i = 0; i < pendingMethodCount; i++)
            {
                int slot = (pendingMethodHead + i) % MaxPendingMethodPackets;
                byte[] frame = (byte[])pendingMethodFrames[slot];
                if ((frame != null && ReadInt32(frame, 24) == removedPlayerId) ||
                    pendingMethodPlayers[slot] == player)
                {
                    pendingMethodFrames[slot] = null;
                    pendingMethodPlayers[slot] = null;
                }
            }
            int keepCount = 0;
            for (int i = 0; i < receivePlayerIds.Length; i++)
            {
                if (receivePlayerIds[i] != removedPlayerId && receiveOwnerIds[i] != removedPlayerId)
                    keepCount++;
            }
            if (keepCount == receivePlayerIds.Length)
                return;

            int[] nextPlayers = new int[keepCount];
            int[] nextZones = new int[keepCount];
            int[] nextReceivers = new int[keepCount];
            int[] nextOwners = new int[keepCount];
            int[] nextEpochs = new int[keepCount];
            int[] nextSequences = new int[keepCount];
            int write = 0;
            for (int i = 0; i < receivePlayerIds.Length; i++)
            {
                if (receivePlayerIds[i] == removedPlayerId || receiveOwnerIds[i] == removedPlayerId)
                    continue;
                nextPlayers[write] = receivePlayerIds[i];
                nextZones[write] = receiveZoneIds[i];
                nextReceivers[write] = receiveReceiverIds[i];
                nextOwners[write] = receiveOwnerIds[i];
                nextEpochs[write] = receiveEpochs[i];
                nextSequences[write] = receiveSequences[i];
                write++;
            }
            receivePlayerIds = nextPlayers;
            receiveZoneIds = nextZones;
            receiveReceiverIds = nextReceivers;
            receiveOwnerIds = nextOwners;
            receiveEpochs = nextEpochs;
            receiveSequences = nextSequences;
        }

        private LCGNetworkZone FindZone(int zoneId)
        {
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null && zones[i].ZoneId == zoneId)
                    return zones[i];
            }

            return null;
        }

        private static bool ValidateFrame(byte[] frame)
        {
            return frame != null && frame.Length >= HeaderSize && frame.Length <= MaxFrameBytes &&
                   frame[0] == ProtocolVersion && frame[1] <= 2;
        }

        internal static int ReadInt32(byte[] data, int offset)
        {
            return data[offset] |
                   data[offset + 1] << 8 |
                   data[offset + 2] << 16 |
                   data[offset + 3] << 24;
        }

        private int FindRegistration(int receiverId, string address)
        {
            int count = packetAddresses.Length;
            if (packetReceiverIds.Length < count || packetAuthorities.Length < count)
                return -1;
            for (int i = 0; i < count; i++)
            {
                if (packetReceiverIds[i] == receiverId && packetAddresses[i] == address)
                    return i;
            }

            return -1;
        }

        private bool HasFieldRegistration(int receiverId)
        {
            for (int i = 0; i < packetAddresses.Length && i < packetKinds.Length; i++)
            {
                if (packetReceiverIds[i] == receiverId && packetKinds[i] == 0)
                    return true;
            }
            return false;
        }

        private void SendFieldSnapshot(int receiverId, UdonBehaviour receiver, int zoneId, VRCPlayerApi player)
        {
            int ownerId = GetReceiverOwnerId(receiverId, receiver);
            if (ownerId < -1)
                return;
            LCGNetworkZone zone = FindZone(zoneId);
            int epoch = zone != null ? zone.Epoch : 0;
            for (int i = 0; i < packetAddresses.Length; i++)
            {
                if (i >= packetReceiverIds.Length || i >= packetKinds.Length || i >= packetValueTypes.Length ||
                    packetReceiverIds[i] != receiverId || packetKinds[i] != 0)
                    continue;
                object value = receiver.GetProgramVariable(packetAddresses[i]);
                byte[] frame = BuildFieldFrame(receiverId, zoneId, epoch, sequence++, packetValueTypes[i],
                    packetAddresses[i], value);
                if (frame != null)
                {
                    WriteInt32(frame, 24, ownerId);
                    SendFrame(player, frame);
                }
            }
        }

        private bool AcceptSequence(int playerId, int zoneId, int receiverId, int ownerId, int frameEpoch, int frameSequence)
        {
            for (int i = 0; i < receivePlayerIds.Length; i++)
            {
                if (receivePlayerIds[i] != playerId || receiveZoneIds[i] != zoneId ||
                    receiveReceiverIds[i] != receiverId || receiveOwnerIds[i] != ownerId)
                    continue;
                if (frameEpoch < receiveEpochs[i] ||
                    (frameEpoch == receiveEpochs[i] && frameSequence <= receiveSequences[i]))
                    return false;
                receiveEpochs[i] = frameEpoch;
                receiveSequences[i] = frameSequence;
                return true;
            }

            int length = receivePlayerIds.Length;
            int[] nextPlayers = new int[length + 1];
            int[] nextZones = new int[length + 1];
            int[] nextReceivers = new int[length + 1];
            int[] nextOwners = new int[length + 1];
            int[] nextEpochs = new int[length + 1];
            int[] nextSequences = new int[length + 1];
            for (int i = 0; i < length; i++)
            {
                nextPlayers[i] = receivePlayerIds[i];
                nextZones[i] = receiveZoneIds[i];
                nextReceivers[i] = receiveReceiverIds[i];
                nextOwners[i] = receiveOwnerIds[i];
                nextEpochs[i] = receiveEpochs[i];
                nextSequences[i] = receiveSequences[i];
            }

            nextPlayers[length] = playerId;
            nextZones[length] = zoneId;
            nextReceivers[length] = receiverId;
            nextOwners[length] = ownerId;
            nextEpochs[length] = frameEpoch;
            nextSequences[length] = frameSequence;
            receivePlayerIds = nextPlayers;
            receiveZoneIds = nextZones;
            receiveReceiverIds = nextReceivers;
            receiveOwnerIds = nextOwners;
            receiveEpochs = nextEpochs;
            receiveSequences = nextSequences;
            return true;
        }

        private static bool CheckAuthority(int authority, VRCPlayerApi sender, GameObject receiver)
        {
            if (authority == (int)LCGPacketAuthority.Any)
                return true;
            if (authority == (int)LCGPacketAuthority.Master)
                return sender.isMaster;
            return Networking.GetOwner(receiver) == sender;
        }

        private static byte[] BuildFieldFrame(int receiverId, int zoneId, int epoch, int packetSequence,
            int typeTag, string address, object value)
        {
            byte[] payload = EncodeValue(typeTag, value);
            return BuildFieldFrameFromPayload(receiverId, zoneId, epoch, packetSequence, typeTag, address, payload);
        }

        private static byte[] BuildSnapshotRequestFrame(int receiverId, int zoneId, int epoch, int packetSequence)
        {
            byte[] frame = new byte[HeaderSize];
            frame[0] = ProtocolVersion;
            frame[1] = 2;
            WriteInt32(frame, 24, -1);
            WriteInt32(frame, 4, zoneId);
            WriteInt32(frame, 8, epoch);
            WriteInt32(frame, 12, receiverId);
            WriteInt32(frame, 16, packetSequence);
            return frame;
        }

        private static byte[] BuildFieldFrameFromPayload(int receiverId, int zoneId, int epoch,
            int packetSequence, int typeTag, string address, byte[] payload)
        {
            byte[] addressBytes = EncodeText(address ?? string.Empty);
            if (payload == null || addressBytes.Length > ushort.MaxValue || payload.Length > ushort.MaxValue)
                return null;

            int frameLength = HeaderSize + addressBytes.Length + payload.Length;
            if (frameLength > MaxFrameBytes)
                return null;

            byte[] frame = new byte[frameLength];
            frame[0] = ProtocolVersion;
            frame[1] = 0; // field state
            WriteInt32(frame, 24, -1);
            frame[2] = (byte)typeTag;
            WriteInt32(frame, 4, zoneId);
            WriteInt32(frame, 8, epoch);
            WriteInt32(frame, 12, receiverId);
            WriteInt32(frame, 16, packetSequence);
            WriteUInt16(frame, 20, addressBytes.Length);
            WriteUInt16(frame, 22, payload.Length);
            Buffer.BlockCopy(addressBytes, 0, frame, HeaderSize, addressBytes.Length);
            Buffer.BlockCopy(payload, 0, frame, HeaderSize + addressBytes.Length, payload.Length);
            return frame;
        }

        private static int FindFieldKey(int[] receiverIds, int[] ownerIds, int[] zoneIds, string[] addresses, int count,
            int receiverId, int ownerId, int zoneId, string address)
        {
            for (int i = 0; i < count; i++)
            {
                if (receiverIds[i] == receiverId && ownerIds[i] == ownerId && zoneIds[i] == zoneId && addresses[i] == address)
                    return i;
            }
            return -1;
        }

        private void RememberSentField(int receiverId, int ownerId, int zoneId, string address, byte[] payload)
        {
            int index = FindFieldKey(sentFieldReceiverIds, sentFieldOwnerIds, sentFieldZoneIds, sentFieldAddresses, sentFieldCount,
                receiverId, ownerId, zoneId, address);
            if (index < 0)
            {
                if (sentFieldCount >= MaxPendingFieldPackets)
                    index = sequence % MaxPendingFieldPackets;
                else
                    index = sentFieldCount++;
                sentFieldReceiverIds[index] = receiverId;
                sentFieldOwnerIds[index] = ownerId;
                sentFieldZoneIds[index] = zoneId;
                sentFieldAddresses[index] = address;
            }
            sentFieldPayloads[index] = payload;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }
            return true;
        }

        private static byte[] BuildMethodFrame(int receiverId, int zoneId, int epoch, int packetSequence,
            string address, int argumentCount, int[] types, object[] values)
        {
            byte[] addressBytes = EncodeText(address ?? string.Empty);
            byte[][] encoded = new byte[argumentCount][];
            int payloadLength = 0;
            for (int i = 0; i < argumentCount; i++)
            {
                encoded[i] = EncodeValue(types[i], values[i]);
                if (encoded[i] == null || encoded[i].Length > ushort.MaxValue)
                    return null;
                payloadLength += 3 + encoded[i].Length;
            }

            int frameLength = HeaderSize + addressBytes.Length + payloadLength;
            if (addressBytes.Length > ushort.MaxValue || payloadLength > ushort.MaxValue || frameLength > MaxFrameBytes)
                return null;

            byte[] frame = new byte[frameLength];
            frame[0] = ProtocolVersion;
            frame[1] = 1;
            WriteInt32(frame, 24, -1);
            frame[2] = (byte)argumentCount;
            WriteInt32(frame, 4, zoneId);
            WriteInt32(frame, 8, epoch);
            WriteInt32(frame, 12, receiverId);
            WriteInt32(frame, 16, packetSequence);
            WriteUInt16(frame, 20, addressBytes.Length);
            WriteUInt16(frame, 22, payloadLength);
            Buffer.BlockCopy(addressBytes, 0, frame, HeaderSize, addressBytes.Length);
            int cursor = HeaderSize + addressBytes.Length;
            for (int i = 0; i < argumentCount; i++)
            {
                frame[cursor++] = (byte)types[i];
                WriteUInt16(frame, cursor, encoded[i].Length);
                cursor += 2;
                Buffer.BlockCopy(encoded[i], 0, frame, cursor, encoded[i].Length);
                cursor += encoded[i].Length;
            }
            return frame;
        }

        private bool ApplyMethodPayload(UdonBehaviour receiver, int registration, byte[] frame, int offset, int length)
        {
            if (registration >= packetParameterOffsets.Length || registration >= packetParameterCounts.Length)
                return false;
            int count = packetParameterCounts[registration];
            int parameterOffset = packetParameterOffsets[registration];
            if (frame[2] != count || count < 0 || count > 8 || parameterOffset < 0 ||
                parameterOffset + count > packetParameterNames.Length ||
                parameterOffset + count > packetParameterTypes.Length)
                return false;

            int cursor = offset;
            int end = offset + length;
            object[] decoded = new object[count];
            for (int i = 0; i < count; i++)
            {
                if (cursor + 3 > end)
                    return false;
                int type = frame[cursor++];
                int valueLength = ReadUInt16(frame, cursor);
                cursor += 2;
                if (type != packetParameterTypes[parameterOffset + i] || cursor + valueLength > end)
                    return false;
                object value = DecodeValue(type, frame, cursor, valueLength);
                bool nullArray = (type & (int)LCGPacketType.ArrayFlag) != 0 && valueLength == 4 &&
                                 ReadInt32(frame, cursor) == -1;
                bool nullString = type == (int)LCGPacketType.String && valueLength == 1 && frame[cursor] == 0;
                if (value == null && !nullString && !nullArray)
                    return false;
                decoded[i] = value;
                cursor += valueLength;
            }
            if (cursor != end)
                return false;
            for (int i = 0; i < count; i++)
                receiver.SetProgramVariable(packetParameterNames[parameterOffset + i], decoded[i]);
            return true;
        }

        private static byte[] EncodeValue(int typeTag, object value)
        {
            if ((typeTag & (int)LCGPacketType.ArrayFlag) != 0)
                return EncodeArray(typeTag & ~(int)LCGPacketType.ArrayFlag, value != null && value.GetType().IsArray ? (Array)value : null);

            switch ((LCGPacketType)typeTag)
            {
                case LCGPacketType.Boolean: return new[] { (bool)value ? (byte)1 : (byte)0 };
                case LCGPacketType.SByte: return new[] { (byte)((sbyte)value & 255) };
                case LCGPacketType.Byte: return new[] { (byte)value };
                case LCGPacketType.Int16: return BitConverter.GetBytes((short)value);
                case LCGPacketType.UInt16: return BitConverter.GetBytes((ushort)value);
                case LCGPacketType.Int32: return BitConverter.GetBytes((int)value);
                case LCGPacketType.UInt32: return BitConverter.GetBytes((uint)value);
                case LCGPacketType.Int64: return BitConverter.GetBytes((long)value);
                case LCGPacketType.UInt64: return BitConverter.GetBytes((ulong)value);
                case LCGPacketType.Single: return BitConverter.GetBytes((float)value);
                case LCGPacketType.Double: return BitConverter.GetBytes((double)value);
                case LCGPacketType.Char: return BitConverter.GetBytes((char)value);
                case LCGPacketType.String:
                    if (value == null)
                        return new byte[] { 0 };
                    byte[] text = EncodeText((string)value);
                    byte[] encodedText = new byte[text.Length + 1];
                    encodedText[0] = 1;
                    Buffer.BlockCopy(text, 0, encodedText, 1, text.Length);
                    return encodedText;
                case LCGPacketType.Vector2: return EncodeFloats(new[] { ((Vector2)value).x, ((Vector2)value).y });
                case LCGPacketType.Vector3: return EncodeFloats(new[] { ((Vector3)value).x, ((Vector3)value).y, ((Vector3)value).z });
                case LCGPacketType.Vector4: return EncodeFloats(new[] { ((Vector4)value).x, ((Vector4)value).y, ((Vector4)value).z, ((Vector4)value).w });
                case LCGPacketType.Quaternion: return EncodeFloats(new[] { ((Quaternion)value).x, ((Quaternion)value).y, ((Quaternion)value).z, ((Quaternion)value).w });
                case LCGPacketType.Color: return EncodeFloats(new[] { ((Color)value).r, ((Color)value).g, ((Color)value).b, ((Color)value).a });
                case LCGPacketType.Color32:
                    Color32 color = (Color32)value;
                    return new[] { color.r, color.g, color.b, color.a };
                default: return null;
            }
        }

        private static object DecodeValue(int typeTag, byte[] data, int offset, int length)
        {
            if ((typeTag & (int)LCGPacketType.ArrayFlag) != 0)
                return DecodeArray(typeTag & ~(int)LCGPacketType.ArrayFlag, data, offset, length);

            switch ((LCGPacketType)typeTag)
            {
                case LCGPacketType.Boolean:
                    return length == 1 && data[offset] <= 1 ? data[offset] != 0 : (object)null;
                case LCGPacketType.SByte: return length == 1
                    ? (sbyte)(data[offset] < 128 ? (int)data[offset] : data[offset] - 256)
                    : (object)null;
                case LCGPacketType.Byte: return length == 1 ? data[offset] : (object)null;
                case LCGPacketType.Int16: return length == 2 ? BitConverter.ToInt16(data, offset) : (object)null;
                case LCGPacketType.UInt16: return length == 2 ? BitConverter.ToUInt16(data, offset) : (object)null;
                case LCGPacketType.Int32: return length == 4 ? BitConverter.ToInt32(data, offset) : (object)null;
                case LCGPacketType.UInt32: return length == 4 ? BitConverter.ToUInt32(data, offset) : (object)null;
                case LCGPacketType.Int64: return length == 8 ? BitConverter.ToInt64(data, offset) : (object)null;
                case LCGPacketType.UInt64: return length == 8 ? BitConverter.ToUInt64(data, offset) : (object)null;
                case LCGPacketType.Single: return length == 4 ? BitConverter.ToSingle(data, offset) : (object)null;
                case LCGPacketType.Double: return length == 8 ? BitConverter.ToDouble(data, offset) : (object)null;
                case LCGPacketType.Char: return length == 2 ? BitConverter.ToChar(data, offset) : (object)null;
                case LCGPacketType.String:
                    if (length < 1 || data[offset] > 1)
                        return null;
                    return data[offset] == 0 ? null : DecodeText(data, offset + 1, length - 1);
                case LCGPacketType.Vector2: return length == 8 ? new Vector2(ReadSingle(data, offset), ReadSingle(data, offset + 4)) : (object)null;
                case LCGPacketType.Vector3: return length == 12 ? new Vector3(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8)) : (object)null;
                case LCGPacketType.Vector4: return length == 16 ? new Vector4(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8), ReadSingle(data, offset + 12)) : (object)null;
                case LCGPacketType.Quaternion: return length == 16 ? new Quaternion(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8), ReadSingle(data, offset + 12)) : (object)null;
                case LCGPacketType.Color: return length == 16 ? new Color(ReadSingle(data, offset), ReadSingle(data, offset + 4), ReadSingle(data, offset + 8), ReadSingle(data, offset + 12)) : (object)null;
                case LCGPacketType.Color32: return length == 4 ? new Color32(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]) : (object)null;
                default: return null;
            }
        }

        private static byte[] EncodeArray(int elementTypeTag, Array values)
        {
            if (values == null)
                return BitConverter.GetBytes(-1);

            int totalLength = 4;
            for (int i = 0; i < values.Length; i++)
            {
                byte[] element = EncodeValue(elementTypeTag, values.GetValue(i));
                if (element == null)
                    return null;
                totalLength += 4 + element.Length;
                if (totalLength > MaxFrameBytes)
                    return null;
            }

            byte[] result = new byte[totalLength];
            WriteInt32(result, 0, values.Length);
            int offset = 4;
            for (int i = 0; i < values.Length; i++)
            {
                byte[] element = EncodeValue(elementTypeTag, values.GetValue(i));
                WriteInt32(result, offset, element.Length);
                offset += 4;
                Buffer.BlockCopy(element, 0, result, offset, element.Length);
                offset += element.Length;
            }

            return result;
        }

        private static object DecodeArray(int elementTypeTag, byte[] data, int offset, int length)
        {
            if (length < 4)
                return null;
            int count = ReadInt32(data, offset);
            if (count == -1)
                return null;
            if (count < 0 || count > MaxFrameBytes / 4)
                return null;

            Array result = CreateArray(elementTypeTag, count);
            if (result == null)
                return null;

            int cursor = offset + 4;
            int end = offset + length;
            for (int i = 0; i < count; i++)
            {
                if (cursor + 4 > end)
                    return null;
                int elementLength = ReadInt32(data, cursor);
                cursor += 4;
                if (elementLength < 0 || cursor + elementLength > end)
                    return null;
                object element = DecodeValue(elementTypeTag, data, cursor, elementLength);
                bool nullString = elementTypeTag == (int)LCGPacketType.String && elementLength == 1 &&
                                  data[cursor] == 0;
                if (element == null && !nullString)
                    return null;
                result.SetValue(element, i);
                cursor += elementLength;
            }

            return cursor == end ? result : null;
        }

        private static Array CreateArray(int elementTypeTag, int length)
        {
            switch ((LCGPacketType)elementTypeTag)
            {
                case LCGPacketType.Boolean: return new bool[length];
                case LCGPacketType.SByte: return new sbyte[length];
                case LCGPacketType.Byte: return new byte[length];
                case LCGPacketType.Int16: return new short[length];
                case LCGPacketType.UInt16: return new ushort[length];
                case LCGPacketType.Int32: return new int[length];
                case LCGPacketType.UInt32: return new uint[length];
                case LCGPacketType.Int64: return new long[length];
                case LCGPacketType.UInt64: return new ulong[length];
                case LCGPacketType.Single: return new float[length];
                case LCGPacketType.Double: return new double[length];
                case LCGPacketType.Char: return new char[length];
                case LCGPacketType.String: return new string[length];
                case LCGPacketType.Vector2: return new Vector2[length];
                case LCGPacketType.Vector3: return new Vector3[length];
                case LCGPacketType.Vector4: return new Vector4[length];
                case LCGPacketType.Quaternion: return new Quaternion[length];
                case LCGPacketType.Color: return new Color[length];
                case LCGPacketType.Color32: return new Color32[length];
                default: return null;
            }
        }

        private static byte[] EncodeText(string value)
        {
            char[] characters = value.ToCharArray();
            byte[] bytes = new byte[characters.Length * 2];
            for (int i = 0; i < characters.Length; i++)
            {
                int character = characters[i];
                bytes[i * 2] = (byte)(character & 255);
                bytes[i * 2 + 1] = (byte)((character >> 8) & 255);
            }
            return bytes;
        }

        private static string DecodeText(byte[] bytes, int offset, int length)
        {
            if ((length & 1) != 0)
                return null;
            char[] characters = new char[length / 2];
            for (int i = 0; i < characters.Length; i++)
                characters[i] = (char)(bytes[offset + i * 2] | bytes[offset + i * 2 + 1] << 8);
            return new string(characters);
        }

        private static byte[] EncodeFloats(float[] values)
        {
            byte[] result = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                byte[] bytes = BitConverter.GetBytes(values[i]);
                Buffer.BlockCopy(bytes, 0, result, i * 4, 4);
            }

            return result;
        }

        private static float ReadSingle(byte[] data, int offset)
        {
            return BitConverter.ToSingle(data, offset);
        }

        private static int ReadUInt16(byte[] data, int offset)
        {
            return data[offset] | data[offset + 1] << 8;
        }

        private static void WriteUInt16(byte[] data, int offset, int value)
        {
            data[offset] = (byte)(value & 255);
            data[offset + 1] = (byte)((value >> 8) & 255);
        }

        private static void WriteInt32(byte[] data, int offset, int value)
        {
            // Udon lowers casts to checked Convert.ToByte externs. Mask each
            // lane first so negative markers and values above 255 cannot throw.
            data[offset] = (byte)(value & 255);
            data[offset + 1] = (byte)((value >> 8) & 255);
            data[offset + 2] = (byte)((value >> 16) & 255);
            data[offset + 3] = (byte)((value >> 24) & 255);
        }
    }

}
