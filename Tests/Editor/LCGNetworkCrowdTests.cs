using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UdonSharp;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon;
using VRC.Udon.Editor;

namespace LogicCuteGuy.LCGUdonSharp.Installer.Tests
{
    public sealed class LCGNetworkCrowdTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;
        private LCGRuntime runtime;
        private LCGNetworkZone zone;
        private UdonBehaviour receiver;
        private LCGRuntimePlayer mailbox;
        private VRCPlayerApi local;
        private int sdkQueuedEvents;
        private readonly List<VRCPlayerApi> players = new List<VRCPlayerApi>();
        private readonly Dictionary<FieldInfo, object> delegates = new Dictionary<FieldInfo, object>();

        [OneTimeSetUp]
        public void CompilePrograms()
        {
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            Assert.That(UdonSharpProgramAsset.AnyUdonSharpScriptHasError(), Is.False);
        }

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            runtime = Object("router").AddComponent<LCGRuntime>();
            var zoneObject = Object("zone");
            zoneObject.AddComponent<BoxCollider>().isTrigger = true;
            zone = zoneObject.AddComponent<LCGNetworkZone>();
            typeof(LCGNetworkZone).GetField("zoneId", Private).SetValue(zone, 1);
            receiver = Object("receiver").AddComponent<UdonBehaviour>();
            var program = UdonEditorManager.Instance.Assemble(@".data_start
__lcgZoneId: %SystemInt32, null
value: %SystemInt32, null
.data_end
.code_start
.code_end");
            typeof(UdonBehaviour).GetField("_program", Private).SetValue(receiver, program);
            receiver.SetProgramVariable("__lcgZoneId", 1);
            receiver.SetProgramVariable("value", 0);
            mailbox = Object("mailbox").AddComponent<LCGRuntimePlayer>();
            local = Player(true);
            Replace(typeof(Networking), "_LocalPlayer", (Func<VRCPlayerApi>)(() => local));
            Replace(typeof(VRCPlayerApi), "_GetPlayerId", (Func<VRCPlayerApi, int>)(p => players.IndexOf(p) + 1));
            Replace(typeof(Networking), "_GetOwner", (Func<GameObject, VRCPlayerApi>)(go => local));
            Replace(typeof(Networking), "_IsOwner", (Func<VRCPlayerApi, GameObject, bool>)((p, go) => p == local));
            Replace(typeof(Networking), "_IsSuffering", (Func<bool>)(() => false));
            var queueField = typeof(NetworkCalling).GetField("<GetAllQueuedEventsProxy>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Static);
            sdkQueuedEvents = 0;
            Replace(typeof(NetworkCalling), queueField.Name, Delegate.CreateDelegate(queueField.FieldType,
                this, GetType().GetMethod(nameof(GetQueuedEvents), Private)));
            Replace(typeof(Networking), "_FindComponentInPlayerObjects", (Func<VRCPlayerApi, Component, Component>)((p, reference) => reference == mailbox ? mailbox : null));
            Set("zones", new[] { zone });
            Set("receivers", new[] { receiver, receiver });
            Set("playerObjectReceivers", new[] { false, false });
            Set("mailboxTemplate", mailbox);
            zone.OnPlayerTriggerEnter(local);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var entry in delegates) entry.Key.SetValue(null, entry.Value);
            delegates.Clear();
            foreach (var player in players) player.RemoveFromList();
            players.Clear();
            EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void CrowdMotion_ReplacesHistoryAndBatchesObjectsForOneRecipient()
        {
            var remotes = new List<VRCPlayerApi>();
            for (int i = 0; i < 19; i++)
            {
                var player = Player(false);
                zone.OnPlayerTriggerEnter(player);
                remotes.Add(player);
            }
            // Ten updates of two owned objects to nineteen remote players:
            // old transport submits 380 events; only 38 latest states may remain.
            for (int sample = 0; sample < 10; sample++)
                foreach (var player in remotes)
                    for (int id = 0; id < 2; id++)
                        Assert.That(Call("QueueMotionRecipient", player, Motion(id, sample, false)), Is.True);
            Assert.That(runtime.PendingMotionCount, Is.EqualTo(38));
            Assert.That(Get<int>("motionSlotCount"), Is.EqualTo(38));
            runtime.__lcgFlushMotion();
            Assert.That(runtime.MotionBatchesSent, Is.EqualTo(1));
            Assert.That(runtime.PendingMotionCount, Is.EqualTo(36), "Both objects should share one event.");
            Assert.That(runtime.LastMotionBatchBytes, Is.LessThanOrEqualTo(LCGRuntime.MaxMotionBatchBytes));
            runtime.__lcgFlushMotion();
            Assert.That(runtime.MotionBatchesSent, Is.EqualTo(1), "Immediate flush must respect the send budget.");
        }

        [Test]
        public void ObjectStateMethod_UsesLatestStateLaneInsteadOfGameplayRpcQueue()
        {
            receiver.gameObject.AddComponent<LCGManualObjectSync>();
            var player = Player(false);
            zone.OnPlayerTriggerEnter(player);
            runtime.__lcgSenderReceiver = receiver;
            runtime.__lcgSenderReceiverId = 0;
            runtime.__lcgSenderZoneId = 1;
            runtime.__lcgSenderAddress = nameof(LCGManualObjectSync.ApplyObjectState);
            runtime.__lcgSenderTargetMode = (int)VRC.Udon.Common.Interfaces.NetworkEventTarget.Others;
            runtime.__lcgSenderArgCount = 7;
            runtime.__lcgSenderArgType0 = 15;
            runtime.__lcgSenderArgType1 = 17;
            runtime.__lcgSenderArgType2 = 15;
            runtime.__lcgSenderArgType3 = 15;
            runtime.__lcgSenderArgType4 = 1;
            runtime.__lcgSenderArgType5 = 1;
            runtime.__lcgSenderArgType6 = 1;
            runtime.__lcgSenderArg0 = Vector3.zero;
            runtime.__lcgSenderArg1 = Quaternion.identity;
            runtime.__lcgSenderArg2 = Vector3.zero;
            runtime.__lcgSenderArg3 = Vector3.zero;
            runtime.__lcgSenderArg4 = false;
            runtime.__lcgSenderArg5 = false;
            runtime.__lcgSenderArg6 = false;
            for (int i = 0; i < 10; i++) runtime.__lcgSendMethod();
            Assert.That(Get<int>("pendingMethodCount"), Is.Zero);
            Assert.That(runtime.PendingMotionCount, Is.EqualTo(1));
        }

        [Test]
        public void MotionReplacement_PreservesTeleportAndDoesNotMutateOtherRecipients()
        {
            var first = Player(false);
            var second = Player(false);
            Call("QueueMotionRecipient", first, Motion(0, 0, true));
            byte[] latest = Motion(0, 10, false);
            Call("QueueMotionRecipient", first, latest);
            Call("QueueMotionRecipient", second, latest);
            var frames = Get<object[]>("pendingMotionFrames");
            Assert.That(((byte[])frames[0])[latest.Length - 1], Is.EqualTo(1));
            Assert.That(((byte[])frames[1])[latest.Length - 1], Is.Zero);
            Assert.That(latest[latest.Length - 1], Is.Zero);
            Assert.That(runtime.PendingMotionCount, Is.EqualTo(2));
        }

        [Test]
        public void Motion_DropsRecipientsWhoExitAndRetainsSamplesWhileMailboxIsMissing()
        {
            var player = Player(false);
            zone.OnPlayerTriggerEnter(player);
            Networking._FindComponentInPlayerObjects = (p, reference) => null;
            Call("QueueMotionRecipient", player, Motion(0, 0, false));
            runtime.__lcgFlushMotion();
            Assert.That(runtime.PendingMotionCount, Is.EqualTo(1));
            zone.OnPlayerTriggerExit(player);
            runtime.__lcgFlushMotion();
            Assert.That(runtime.PendingMotionCount, Is.Zero);
            Assert.That(runtime.MotionBatchesSent, Is.Zero);
        }

        [Test]
        public void Congestion_PausesTransportWithoutAccumulatingMotionHistory()
        {
            var player = Player(false);
            zone.OnPlayerTriggerEnter(player);
            Networking._IsSuffering = () => true;
            for (int i = 0; i < 20; i++)
            {
                Call("QueueMotionRecipient", player, Motion(0, i, false));
                runtime.__lcgFlushMotion();
            }
            Assert.That(runtime.PendingMotionCount, Is.EqualTo(1));
            Assert.That(runtime.MotionBatchesSent, Is.Zero);
            Networking._IsSuffering = () => false;
            sdkQueuedEvents = 20;
            runtime.__lcgFlushMotion();
            Assert.That(runtime.MotionBatchesSent, Is.Zero);
            sdkQueuedEvents = 0;
            runtime.__lcgFlushMotion();
            Assert.That(runtime.PendingMotionCount, Is.Zero);
            Assert.That(runtime.MotionBatchesSent, Is.EqualTo(1));
        }

        [Test]
        public void PlayerLeft_RemovesUnsentMotion()
        {
            var player = Player(false);
            Call("QueueMotionRecipient", player, Motion(0, 0, false));
            runtime.OnPlayerLeft(player);
            Assert.That(runtime.PendingMotionCount, Is.Zero);
        }

        [Test]
        public void Batch_UsesInnerAuthorityAndRejectsMalformedEnvelopeBeforeApplyingAnything()
        {
            Set("packetAddresses", new[] { "value" });
            Set("packetReceiverIds", new[] { 0 });
            Set("packetAuthorities", new[] { (int)LCGPacketAuthority.ObjectOwner });
            Set("packetKinds", new[] { 0 });
            Set("packetValueTypes", new[] { (int)LCGPacketType.Int32 });
            var other = Player(false);
            zone.OnPlayerTriggerEnter(other);
            byte[] frame = (byte[])Static("BuildFieldFrame", 0, 1, 0, 1,
                (int)LCGPacketType.Int32, "value", 42);
            byte[] batch = new byte[LCGRuntime.HeaderSize + 2 + frame.Length];
            batch[0] = LCGRuntime.ProtocolVersion;
            batch[1] = 3;
            Static("WriteUInt16", batch, LCGRuntime.HeaderSize, frame.Length);
            Buffer.BlockCopy(frame, 0, batch, LCGRuntime.HeaderSize + 2, frame.Length);
            Call("ReceiveFrame", batch, other);
            Assert.That(receiver.GetProgramVariable("value"), Is.EqualTo(0), "Batch must not bypass owner authority.");
            byte[] malformed = new byte[batch.Length + 1];
            Buffer.BlockCopy(batch, 0, malformed, 0, batch.Length);
            Call("ReceiveFrame", malformed, local);
            Assert.That(receiver.GetProgramVariable("value"), Is.EqualTo(0));
            Call("ReceiveFrame", batch, local);
            Assert.That(receiver.GetProgramVariable("value"), Is.EqualTo(42));
            Call("ReceiveFrame", batch, local);
            Assert.That(Get<int[]>("receiveSequences")[0], Is.EqualTo(1));
        }

        // Invoke from a Play Mode smoke check. The SDK's Unity-object blacklist
        // lazily calls DontDestroyOnLoad, which cannot initialize in Edit Mode.
        public void VerifyBatchEnvelopeInUdonVm()
        {
            receiver.SetProgramVariable("__lcgZoneId", 0);
            byte[] first = (byte[])Static("BuildFieldFrame", 0, 0, 0, 1, (int)LCGPacketType.Int32, "value", 42);
            byte[] second = (byte[])Static("BuildFieldFrame", 0, 0, 0, 2, (int)LCGPacketType.Int32, "value", 88);
            byte[] batch = new byte[LCGRuntime.HeaderSize + 4 + first.Length + second.Length];
            batch[0] = LCGRuntime.ProtocolVersion;
            batch[1] = 3;
            int cursor = LCGRuntime.HeaderSize;
            foreach (byte[] frame in new[] { first, second })
            {
                Static("WriteUInt16", batch, cursor, frame.Length);
                cursor += 2;
                Buffer.BlockCopy(frame, 0, batch, cursor, frame.Length);
                cursor += frame.Length;
            }
            var program = UdonSharpProgramAsset.GetProgramAssetForClass(typeof(LCGRuntime)).SerializedProgramAsset.RetrieveProgram();
            var heap = program.Heap;
            var symbols = program.SymbolTable;
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("receivers"), new[] { receiver });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("playerObjectReceivers"), new[] { false });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("packetAddresses"), new[] { "value" });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("packetReceiverIds"), new[] { 0 });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("packetAuthorities"), new[] { (int)LCGPacketAuthority.Any });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("packetKinds"), new[] { 0 });
            heap.SetHeapVariable(symbols.GetAddressFromSymbol("packetValueTypes"), new[] { (int)LCGPacketType.Int32 });
            foreach (string symbol in symbols.GetSymbols())
            {
                if (symbol.EndsWith("_frame__param")) heap.SetHeapVariable(symbols.GetAddressFromSymbol(symbol), batch);
                if (symbol.EndsWith("_sender__param")) heap.SetHeapVariable(symbols.GetAddressFromSymbol(symbol), local);
            }
            var vm = UdonEditorManager.Instance.ConstructUdonVM();
            vm.LoadProgram(program);
            vm.SetProgramCounter(program.EntryPoints.GetAddressFromSymbol("__0_ReceiveFrame"));
            Assert.That(vm.Interpret(), Is.Zero);
            Assert.That(receiver.GetProgramVariable("value"), Is.EqualTo(88), "Each inner frame must apply without overwriting the batch cursor.");
        }

        [Test]
        public void OwnershipGuard_AllowsDepartingOwnerHandoffButRejectsOutsiderClaim()
        {
            var guard = receiver.gameObject.AddComponent<LCGZoneOwnershipGuard>();
            typeof(LCGZoneOwnershipGuard).GetField("zone", Private).SetValue(guard, zone);
            var nextOwner = Player(false);
            var outsider = Player(false);
            zone.OnPlayerTriggerEnter(nextOwner);
            zone.OnPlayerTriggerExit(local);
            Assert.That(guard.OnOwnershipRequest(local, nextOwner), Is.True);
            Assert.That(guard.OnOwnershipRequest(outsider, nextOwner), Is.False);
            Assert.That(guard.OnOwnershipRequest(nextOwner, outsider), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemoteMotion_InterpolatesInsteadOfSnappingAndRestoresPhysicsOnTakeover(bool resetBeforeTakeover)
        {
            var player = Player(false);
            zone.OnPlayerTriggerEnter(player);
            var go = Object("moving object");
            var body = go.AddComponent<Rigidbody>();
            var sync = go.AddComponent<LCGManualObjectSync>();
            typeof(LCGManualObjectSync).GetField("zone", Private).SetValue(sync, zone);
            typeof(LCGManualObjectSync).GetField("defaultRotation", Private).SetValue(sync, Quaternion.identity);
            typeof(LCGManualObjectSync).GetField("defaultActive", Private).SetValue(sync, true);
            Networking._IsOwner = (p, target) => false;
            sync.ApplyObjectState(Vector3.zero, Quaternion.identity, Vector3.right, Vector3.zero, true, false, true);
            sync.ApplyObjectState(Vector3.right * 10f, Quaternion.identity, Vector3.right, Vector3.zero, true, false, false);
            Assert.That(go.transform.position.x, Is.Zero, "Regular update must not teleport the remote object.");
            Assert.That(body.isKinematic, Is.True);
            typeof(LCGManualObjectSync).GetField("receivedAt", Private).SetValue(sync, Time.realtimeSinceStartup - 0.025f);
            typeof(LCGManualObjectSync).GetField("interpolationDuration", Private).SetValue(sync, 0.1f);
            typeof(LCGManualObjectSync).GetMethod("LateUpdate", Private).Invoke(sync, null);
            Assert.That(go.transform.position.x, Is.InRange(2.4f, 2.7f));
            if (resetBeforeTakeover)
                typeof(LCGManualObjectSync).GetMethod("RestoreDefaults", Private).Invoke(sync, null);
            Networking._IsOwner = (p, target) => true;
            sync.OnOwnershipTransferred(local);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(body.velocity.x, Is.EqualTo(resetBeforeTakeover ? 0f : 1f));
            Assert.That(go.transform.position.x, Is.EqualTo(resetBeforeTakeover ? 0f : 10f));
        }

        [Test]
        public void OwnerLeft_BeforeAutomaticTransfer_RepairsFromOwnershipCallback()
        {
            var member = Player(false);
            var departed = Player(false);
            zone.OnPlayerTriggerEnter(member);
            zone.OnPlayerTriggerExit(local);
            typeof(LCGNetworkZone).GetField("protectedObjects", Private).SetValue(zone, new[] { receiver.gameObject });
            var guard = receiver.gameObject.AddComponent<LCGZoneOwnershipGuard>();
            typeof(LCGZoneOwnershipGuard).GetField("zone", Private).SetValue(guard, zone);
            VRCPlayerApi owner = departed;
            Networking._GetOwner = go => owner;
            Networking._IsOwner = (p, go) => p == owner;
            Replace(typeof(Networking), "_SetOwner", (Action<VRCPlayerApi, GameObject>)((p, go) => owner = p));
            // OnPlayerLeft arrives before the engine has chosen the outside local player.
            zone.OnPlayerLeft(departed);
            Assert.That(owner, Is.SameAs(departed));
            owner = local;
            guard.OnOwnershipTransferred(local);
            Assert.That(owner, Is.SameAs(member), "The outside fallback owner must hand back to a zone member.");
        }

        [Test]
        public void LocalPlayerRestored_RequestsSceneFieldSnapshotAfterEarlyZoneEntry()
        {
            var owner = Player(false);
            zone.OnPlayerTriggerEnter(owner);
            Set("packetAddresses", new[] { "value" });
            Set("packetReceiverIds", new[] { 0 });
            Set("packetKinds", new[] { 0 });
            Set("packetValueTypes", new[] { (int)LCGPacketType.Int32 });
            Networking._GetOwner = go => owner;
            Networking._IsOwner = (p, go) => p == owner;
            int requests = 0;
            Networking._FindComponentInPlayerObjects = (p, reference) => {
                if (p == owner && reference == mailbox) requests++;
                return null;
            };
            runtime.OnPlayerRestored(local);
            Assert.That(requests, Is.GreaterThan(0), "Scene fields need a new request once the entrant's mailbox is restored.");
        }

        [Test]
        public void OwnershipRecovery_HandlesDelayedAssignmentWithoutCallbackAndStopsOnEmptyZone()
        {
            var member = Player(false);
            var departed = Player(false);
            zone.OnPlayerTriggerEnter(member);
            zone.OnPlayerTriggerExit(local);
            typeof(LCGNetworkZone).GetField("protectedObjects", Private).SetValue(zone, new[] { receiver.gameObject });
            VRCPlayerApi owner = departed;
            Networking._GetOwner = go => owner;
            Networking._IsOwner = (p, go) => p == owner;
            int transfers = 0;
            Replace(typeof(Networking), "_SetOwner", (Action<VRCPlayerApi, GameObject>)((p, go) => { owner = p; transfers++; }));
            zone.OnPlayerLeft(departed);
            owner = local;
            zone.__lcgRepairOwnership();
            Assert.That(owner, Is.SameAs(member));
            Assert.That(transfers, Is.EqualTo(1));
            zone.OnPlayerTriggerExit(member);
            owner = local;
            for (int i = 0; i < 12; i++) zone.__lcgRepairOwnership();
            Assert.That(owner, Is.SameAs(local), "There is no valid zone owner while the zone is empty.");
            Assert.That(transfers, Is.EqualTo(1));
            Assert.That(typeof(LCGNetworkZone).GetField("ownershipRepairScheduled", Private).GetValue(zone), Is.False);
        }

        [Test]
        public void OwnershipRecovery_PreservesExistingMemberOwnerAndCannotClaimOtherOwnersObjects()
        {
            var member = Player(false);
            zone.OnPlayerTriggerEnter(member);
            typeof(LCGNetworkZone).GetField("protectedObjects", Private).SetValue(zone, new[] { receiver.gameObject });
            int transfers = 0;
            Replace(typeof(Networking), "_SetOwner", (Action<VRCPlayerApi, GameObject>)((p, go) => transfers++));
            zone.RequestOwnershipRepair();
            Assert.That(transfers, Is.Zero, "The local member's legitimate ownership must be preserved.");
            zone.OnPlayerTriggerExit(local);
            transfers = 0;
            Networking._GetOwner = go => member;
            Networking._IsOwner = (p, go) => p == member;
            zone.RequestOwnershipRepair();
            zone.__lcgRepairOwnership();
            Assert.That(transfers, Is.Zero, "An outside non-owner must never claim or reassign objects.");
        }

        [Test]
        public void SnapshotRecovery_RetriesLostRequestWithBoundedAttemptsAndCancelsOnExit()
        {
            var owner = Player(false);
            zone.OnPlayerTriggerEnter(owner);
            typeof(LCGNetworkZone).GetField("runtime", Private).SetValue(zone, runtime);
            Set("packetAddresses", new[] { "value" });
            Set("packetReceiverIds", new[] { 0 });
            Set("packetKinds", new[] { 0 });
            Networking._GetOwner = go => owner;
            Networking._IsOwner = (p, go) => p == owner;
            int requests = 0;
            Networking._FindComponentInPlayerObjects = (p, reference) => { requests++; return null; };
            zone.RequestLocalSnapshotRecovery();
            for (int i = 0; i < 12; i++) zone.__lcgRetrySnapshot();
            Assert.That(requests, Is.EqualTo(5));
            Assert.That(typeof(LCGNetworkZone).GetField("snapshotScheduled", Private).GetValue(zone), Is.False);
            zone.RequestLocalSnapshotRecovery();
            zone.OnPlayerTriggerExit(local);
            zone.__lcgRetrySnapshot();
            Assert.That(requests, Is.EqualTo(5), "No snapshot requests may continue outside the zone.");
        }

        [Test]
        public void RestoredZonePeer_ReceivesCurrentSceneFieldsThroughMailbox()
        {
            var entrant = Player(false);
            zone.OnPlayerTriggerEnter(entrant);
            Set("packetAddresses", new[] { "value" });
            Set("packetReceiverIds", new[] { 0 });
            Set("packetKinds", new[] { 0 });
            Set("packetAuthorities", new[] { (int)LCGPacketAuthority.ObjectOwner });
            Set("packetValueTypes", new[] { (int)LCGPacketType.Int32 });
            var peer = Object("peer router").AddComponent<LCGRuntime>();
            var peerReceiver = Object("peer receiver").AddComponent<UdonBehaviour>();
            var peerProgram = UdonEditorManager.Instance.Assemble(@".data_start
__lcgZoneId: %SystemInt32, null
value: %SystemInt32, null
.data_end
.code_start
.code_end");
            typeof(UdonBehaviour).GetField("_program", Private).SetValue(peerReceiver, peerProgram);
            peerReceiver.SetProgramVariable("__lcgZoneId", 1);
            peerReceiver.SetProgramVariable("value", 0);
            foreach (string field in new[] { "packetAddresses", "packetReceiverIds", "packetKinds", "packetAuthorities", "packetValueTypes", "zones" })
                typeof(LCGRuntime).GetField(field, Private).SetValue(peer, Get<object>(field));
            typeof(LCGRuntime).GetField("receivers", Private).SetValue(peer, new[] { peerReceiver });
            typeof(LCGRuntimePlayer).GetField("runtime", Private).SetValue(mailbox, peer);
            var caller = typeof(NetworkCalling).GetProperty(nameof(NetworkCalling.CallingPlayer));
            object previous = caller.GetValue(null);
            try
            {
                caller.SetValue(null, local);
                receiver.SetProgramVariable("value", 81);
                runtime.OnPlayerRestored(entrant);
                Assert.That(peerReceiver.GetProgramVariable("value"), Is.EqualTo(81));
            }
            finally { caller.SetValue(null, previous); }
        }

        [Test]
        public void DepartedPlayer_RemovedByIdentityWhenInvalidIdsCollide()
        {
            var firstInvalid = Player(false);
            var departed = Player(false);
            zone.OnPlayerTriggerEnter(firstInvalid);
            zone.OnPlayerTriggerEnter(departed);
            var getId = VRCPlayerApi._GetPlayerId;
            VRCPlayerApi._GetPlayerId = p => p == firstInvalid || p == departed ? -1 : getId(p);
            zone.OnPlayerLeft(departed);
            Assert.That(zone.OccupantCount, Is.EqualTo(2));
            Assert.That(zone.GetOccupant(1), Is.SameAs(firstInvalid), "Invalid IDs must not remove a different departing player.");
        }

        private byte[] Motion(int receiverId, float position, bool teleport)
        {
            return (byte[])Static("BuildMethodFrame", receiverId, 1, 0, 0, "ApplyObjectState", 7,
                new[] { 15, 17, 15, 15, 1, 1, 1 },
                new object[] { Vector3.right * position, Quaternion.identity, Vector3.zero, Vector3.zero, false, false, teleport });
        }
        private int GetQueuedEvents() => sdkQueuedEvents;

        private VRCPlayerApi Player(bool isLocal)
        {
            var player = new VRCPlayerApi { isLocal = isLocal };
            player.AddToList();
            players.Add(player);
            return player;
        }
        private GameObject Object(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }
        private void Replace(Type type, string name, object value)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            delegates.Add(field, field.GetValue(null));
            field.SetValue(null, value);
        }
        private void Set(string name, object value) => typeof(LCGRuntime).GetField(name, Private).SetValue(runtime, value);
        private T Get<T>(string name) => (T)typeof(LCGRuntime).GetField(name, Private).GetValue(runtime);
        private object Call(string name, params object[] args) => typeof(LCGRuntime).GetMethod(name, Private).Invoke(runtime, args);
        private static object Static(string name, params object[] args) => typeof(LCGRuntime)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);
    }
}
