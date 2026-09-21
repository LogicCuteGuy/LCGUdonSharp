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
    // Exercise the real packet router and Udon heaps; substitute only the SDK's
    // player/clone lookup delegates, which are normally supplied by the client.
    public sealed class LCGPlayerObjectTests
    {
        private Scene scene;
        private LCGRuntime runtime;
        private UdonBehaviour template, firstClone, secondClone, sceneReceiver;
        private VRCPlayerApi first, second;
        private readonly Dictionary<FieldInfo, object> savedDelegates = new Dictionary<FieldInfo, object>();
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            scene = EditorSceneManager.NewPreviewScene();
            runtime = CreateObject("router").AddComponent<LCGRuntime>();
            template = CreateReceiver("template");
            firstClone = CreateReceiver("first clone");
            secondClone = CreateReceiver("second clone");
            sceneReceiver = CreateReceiver("scene receiver");
            first = new VRCPlayerApi { isLocal = true };
            second = new VRCPlayerApi { isLocal = false };
            first.AddToList();
            second.AddToList();
            Replace(typeof(VRCPlayerApi), "_GetPlayerId", (Func<VRCPlayerApi, int>)(p => p == first ? 10 : 20));
            Replace(typeof(VRCPlayerApi), "_GetPlayerById", (Func<int, VRCPlayerApi>)(id => id == 10 ? first : id == 20 ? second : null));
            Replace(typeof(Networking), "_LocalPlayer", (Func<VRCPlayerApi>)(() => first));
            Replace(typeof(Networking), "_GetOwner", (Func<GameObject, VRCPlayerApi>)(go => go == secondClone.gameObject ? second : first));
            Replace(typeof(Networking), "_IsOwner", (Func<VRCPlayerApi, GameObject, bool>)((p, go) => Networking.GetOwner(go) == p));
            Replace(typeof(Networking), "_FindComponentInPlayerObjects", (Func<VRCPlayerApi, Component, Component>)((p, reference) =>
                reference == template ? (p == first ? firstClone : p == second ? secondClone : null) : null));
            Set("receivers", new[] { template, sceneReceiver });
            Set("playerObjectReceivers", new[] { true, false });
            Set("packetAddresses", new[] { "value", "Apply", "value" });
            Set("packetReceiverIds", new[] { 0, 0, 1 });
            Set("packetAuthorities", new[] { (int)LCGPacketAuthority.Any, (int)LCGPacketAuthority.Any, (int)LCGPacketAuthority.Any });
            Set("packetKinds", new[] { 0, 1, 0 });
            Set("packetValueTypes", new[] { (int)LCGPacketType.Int32, 0, (int)LCGPacketType.Int32 });
            Set("packetParameterOffsets", new[] { -1, 0, -1 });
            Set("packetParameterCounts", new[] { 0, 1, 0 });
            Set("packetParameterNames", new[] { "argument" });
            Set("packetParameterTypes", new[] { (int)LCGPacketType.Int32 });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var entry in savedDelegates) entry.Key.SetValue(null, entry.Value);
            savedDelegates.Clear();
            first?.RemoveFromList();
            second?.RemoveFromList();
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }

        [Test]
        public void Fields_RouteByCloneOwner_WithoutChangingTemplateOrOtherClone()
        {
            Receive(FieldFrame(0, 20, 1, 42), first);
            Assert.That(secondClone.GetProgramVariable("value"), Is.EqualTo(42));
            Assert.That(firstClone.GetProgramVariable("value"), Is.EqualTo(0));
            Assert.That(template.GetProgramVariable("value"), Is.EqualTo(0));
            // Same sender/sequence is independent on another clone.
            Receive(FieldFrame(0, 10, 1, 7), first);
            Assert.That(firstClone.GetProgramVariable("value"), Is.EqualTo(7));
            Receive(FieldFrame(0, 20, 1, 99), first);
            Assert.That(secondClone.GetProgramVariable("value"), Is.EqualTo(42), "Duplicate must be rejected.");
        }

        [Test]
        public void OwnerAuthority_UsesCloneOwner()
        {
            Set("packetAuthorities", new[] { (int)LCGPacketAuthority.ObjectOwner, 0, 0 });
            Receive(FieldFrame(0, 20, 1, 42), first);
            Assert.That(secondClone.GetProgramVariable("value"), Is.EqualTo(0));
            Receive(FieldFrame(0, 20, 1, 42), second);
            Assert.That(secondClone.GetProgramVariable("value"), Is.EqualTo(42));
        }

        [Test]
        public void Methods_DecodeIntoReferencedClone_AndPreserveCaller()
        {
            var frame = (byte[])Static("BuildMethodFrame", 0, 0, 0, 1, "Apply", 1,
                new[] { (int)LCGPacketType.Int32 }, new object[] { 123 });
            Static("WriteInt32", frame, 24, 20);
            Receive(frame, first);
            Assert.That(secondClone.GetProgramVariable("argument"), Is.EqualTo(123));
            Assert.That(secondClone.GetProgramVariable("__lcgPacketSender"), Is.SameAs(first));
            Assert.That(firstClone.GetProgramVariable("argument"), Is.EqualTo(0));
            Assert.That(template.GetProgramVariable("argument"), Is.EqualTo(0));
        }

        [Test]
        public void InvalidCloneIdsAndOldProtocol_AreRejected_ScenePacketsStillWork()
        {
            foreach (int id in new[] { -2, -1, 999 }) Receive(FieldFrame(0, id, 1, 42), first);
            Receive(FieldFrame(1, 10, 1, 42), first);
            byte[] oldFrame = FieldFrame(0, 20, 2, 42);
            oldFrame[0] = 1;
            Receive(oldFrame, first);
            Assert.That(secondClone.GetProgramVariable("value"), Is.EqualTo(0));
            Assert.That(template.GetProgramVariable("value"), Is.EqualTo(0));
            Assert.That(sceneReceiver.GetProgramVariable("value"), Is.EqualTo(0));
            Receive(FieldFrame(1, -1, 1, 77), first);
            Assert.That(sceneReceiver.GetProgramVariable("value"), Is.EqualTo(77));
        }

        [Test]
        public void QueueCoalescingAndSuppression_AreIndependentPerClone()
        {
            QueueField(firstClone, 5);
            QueueField(secondClone, 6);
            QueueField(firstClone, 9);
            Assert.That(Get<int>("pendingFieldCount"), Is.EqualTo(2));
            var payloads = Get<object[]>("pendingFieldPayloads");
            Assert.That(BitConverter.ToInt32((byte[])payloads[0], 0), Is.EqualTo(9));
            Assert.That(BitConverter.ToInt32((byte[])payloads[1], 0), Is.EqualTo(6));
            Set("pendingFieldCount", 0);
            Call("RememberSentField", 0, 10, 0, "value", BitConverter.GetBytes(5));
            QueueField(firstClone, 5);
            Assert.That(Get<int>("pendingFieldCount"), Is.Zero);
            QueueField(secondClone, 5);
            Assert.That(Get<int>("pendingFieldCount"), Is.EqualTo(1));
        }

        [Test]
        public void MethodQueue_CapturesReferencedCloneInsteadOfSenderOrRecipient()
        {
            runtime.__lcgSenderReceiverId = 0;
            runtime.__lcgSenderReceiver = secondClone;
            runtime.__lcgSenderAddress = "Apply";
            runtime.__lcgSenderTargetMode = -1;
            runtime.__lcgSenderPlayer = first;
            runtime.__lcgSendMethod();
            var frame = (byte[])Get<object[]>("pendingMethodFrames")[0];
            Assert.That(BitConverter.ToInt32(frame, 24), Is.EqualTo(20));
            Assert.That(Get<VRCPlayerApi[]>("pendingMethodPlayers")[0], Is.SameAs(first));
            runtime.__lcgSenderReceiver = template;
            runtime.__lcgSendMethod();
            Assert.That(Get<int>("pendingMethodCount"), Is.EqualTo(1), "Sending on inactive template must be rejected.");
        }

        [Test]
        public void OwnerTarget_DeliversToCloneOwnerRatherThanTemplateOwner()
        {
            LCGRuntimePlayer mailbox = CreateObject("mailbox").AddComponent<LCGRuntimePlayer>();
            Set("mailboxTemplate", mailbox);
            VRCPlayerApi recipient = null;
            var lookup = Networking._FindComponentInPlayerObjects;
            Networking._FindComponentInPlayerObjects = (p, reference) => {
                if (reference != mailbox) return lookup(p, reference);
                recipient = p;
                return null;
            };
            byte[] frame = (byte[])Static("BuildMethodFrame", 0, 0, 0, 1, "Apply", 0,
                new int[0], new object[0]);
            Static("WriteInt32", frame, 24, 20);
            Call("DispatchMethodFrame", frame, 0, (int)VRC.Udon.Common.Interfaces.NetworkEventTarget.Owner, null);
            Assert.That(recipient, Is.SameAs(second));
        }

        [Test]
        public void PlayerLeft_ClearsCloneQueuesSuppressionAndReplayState()
        {
            QueueField(firstClone, 5);
            QueueField(secondClone, 6);
            Call("RememberSentField", 0, 20, 0, "value", BitConverter.GetBytes(6));
            Receive(FieldFrame(0, 20, 100, 42), first);
            runtime.__lcgSenderReceiver = secondClone;
            runtime.__lcgSenderAddress = "Apply";
            runtime.__lcgSendMethod();
            runtime.OnPlayerLeft(second);
            Assert.That(Get<int>("pendingFieldCount"), Is.EqualTo(1));
            Assert.That(Get<int[]>("pendingFieldOwnerIds")[0], Is.EqualTo(10));
            Assert.That(Get<int>("sentFieldCount"), Is.Zero);
            Assert.That(Get<int[]>("receiveOwnerIds"), Is.Empty);
            Assert.That(Get<object[]>("pendingMethodFrames")[0], Is.Null);
            runtime.__lcgFlushMethods();
            Assert.That(Get<int>("pendingMethodCount"), Is.Zero);
        }

        [Test]
        public void Snapshot_ContainsCloneIdentityAndCurrentValue()
        {
            Set("mailboxTemplate", CreateObject("mailbox").AddComponent<LCGRuntimePlayer>());
            firstClone.SetProgramVariable("value", 81);
            // Local delivery executes the same receiver/authority path as a remote
            // snapshot. It can succeed only if the header identifies this clone.
            Call("SendFieldSnapshot", 0, firstClone, 0, first);
            Assert.That(Get<int[]>("receiveOwnerIds"), Is.EqualTo(new[] { 10 }));
            Assert.That(firstClone.GetProgramVariable("value"), Is.EqualTo(81));
            Assert.That(template.GetProgramVariable("value"), Is.EqualTo(0));
        }

        [Test]
        public void SnapshotRequest_IsAnsweredOnlyByTheRequestedClonesOwner()
        {
            LCGRuntimePlayer mailbox = CreateObject("mailbox").AddComponent<LCGRuntimePlayer>();
            Set("mailboxTemplate", mailbox);
            var lookup = Networking._FindComponentInPlayerObjects;
            Networking._FindComponentInPlayerObjects = (p, reference) => reference == mailbox ? mailbox : lookup(p, reference);
            byte[] request = (byte[])Static("BuildSnapshotRequestFrame", 0, 0, 0, 1);
            Static("WriteInt32", request, 24, 20);
            Receive(request, second);
            Assert.That(Get<int>("sequence"), Is.Zero);
            Static("WriteInt32", request, 24, 10);
            Receive(request, second);
            Assert.That(Get<int>("sequence"), Is.EqualTo(1), "Owner should send one field snapshot.");
            Receive(request, second);
            Assert.That(Get<int>("sequence"), Is.EqualTo(1), "Duplicate snapshot request must be ignored.");
        }

        [Test]
        public void RestoredPeer_ReceivesCurrentFieldsFromTheOwnersClone()
        {
            LCGRuntime peer = CreateObject("peer router").AddComponent<LCGRuntime>();
            UdonBehaviour peerTemplate = CreateReceiver("peer template");
            UdonBehaviour peerFirstClone = CreateReceiver("peer first clone");
            foreach (string field in new[] { "packetAddresses", "packetReceiverIds", "packetAuthorities", "packetKinds", "packetValueTypes" })
                typeof(LCGRuntime).GetField(field, Private).SetValue(peer, Get<object>(field));
            typeof(LCGRuntime).GetField("receivers", Private).SetValue(peer, new[] { peerTemplate, sceneReceiver });
            typeof(LCGRuntime).GetField("playerObjectReceivers", Private).SetValue(peer, new[] { true, false });
            LCGRuntimePlayer mailbox = CreateObject("peer mailbox").AddComponent<LCGRuntimePlayer>();
            typeof(LCGRuntimePlayer).GetField("runtime", Private).SetValue(mailbox, peer);
            Set("mailboxTemplate", mailbox);
            Func<VRCPlayerApi, Component, Component> previousLookup = Networking._FindComponentInPlayerObjects;
            Networking._FindComponentInPlayerObjects = (player, reference) => reference == mailbox ? mailbox :
                reference == peerTemplate ? (player == first ? peerFirstClone : secondClone) : previousLookup(player, reference);
            PropertyInfo callingPlayer = typeof(NetworkCalling).GetProperty(nameof(NetworkCalling.CallingPlayer));
            object previousCaller = callingPlayer.GetValue(null);
            try
            {
                callingPlayer.SetValue(null, first);
                firstClone.SetProgramVariable("value", 81);
                runtime.OnPlayerRestored(second);
                Assert.That(peerFirstClone.GetProgramVariable("value"), Is.EqualTo(81));
                Assert.That(peerTemplate.GetProgramVariable("value"), Is.EqualTo(0));
                Assert.That(secondClone.GetProgramVariable("value"), Is.EqualTo(0));
            }
            finally
            {
                callingPlayer.SetValue(null, previousCaller);
                Networking._FindComponentInPlayerObjects = previousLookup;
            }
        }

        private void QueueField(UdonBehaviour clone, int value)
        {
            runtime.__lcgSenderReceiver = clone;
            runtime.__lcgSenderReceiverId = 0;
            runtime.__lcgSenderAddress = "value";
            runtime.__lcgSenderType = (int)LCGPacketType.Int32;
            runtime.__lcgSenderValue = value;
            runtime.__lcgSendField();
        }

        private byte[] FieldFrame(int receiverId, int ownerId, int sequence, int value)
        {
            var frame = (byte[])Static("BuildFieldFrame", receiverId, 0, 0, sequence, (int)LCGPacketType.Int32, "value", value);
            Static("WriteInt32", frame, 24, ownerId);
            return frame;
        }

        private void Receive(byte[] frame, VRCPlayerApi sender) => Call("ReceiveFrame", frame, sender);
        private void Set(string name, object value) => typeof(LCGRuntime).GetField(name, Private).SetValue(runtime, value);
        private T Get<T>(string name) => (T)typeof(LCGRuntime).GetField(name, Private).GetValue(runtime);
        private object Call(string name, params object[] args) => typeof(LCGRuntime).GetMethod(name, Private).Invoke(runtime, args);
        private static object Static(string name, params object[] args) => typeof(LCGRuntime)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, args);

        private void Replace(Type type, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            savedDelegates.Add(field, field.GetValue(null));
            field.SetValue(null, value);
        }

        private GameObject CreateObject(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private UdonBehaviour CreateReceiver(string name)
        {
            var receiver = CreateObject(name).AddComponent<UdonBehaviour>();
            var program = UdonEditorManager.Instance.Assemble(@".data_start
__lcgZoneId: %SystemInt32, null
value: %SystemInt32, null
argument: %SystemInt32, null
__lcgPacketSender: %VRCSDKBaseVRCPlayerApi, null
.data_end
.code_start
.code_end");
            Assert.That(program, Is.Not.Null);
            typeof(UdonBehaviour).GetField("_program", Private).SetValue(receiver, program);
            receiver.SetProgramVariable("__lcgZoneId", 0);
            receiver.SetProgramVariable("value", 0);
            receiver.SetProgramVariable("argument", 0);
            return receiver;
        }
    }
}
