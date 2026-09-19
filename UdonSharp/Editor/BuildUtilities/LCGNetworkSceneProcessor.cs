using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UdonSharp;
using UdonSharp.Compiler;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.SDKBase.Network;
using VRC.Udon;
using Object = UnityEngine.Object;

namespace UdonSharpEditor
{
    /// <summary>
    /// Adds LCG networking infrastructure to Unity's disposable build-scene copy.
    /// Runs before the SDK Udon scene processor (order 0).
    /// </summary>
    internal sealed class LCGNetworkSceneProcessor : IProcessSceneWithReport
    {
        private const string RuntimeObjectName = "__LCGRuntime";
        private const string MailboxObjectName = "__LCGRuntimePlayer";

        public int callbackOrder => -1000;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!scene.IsValid() || Application.isPlaying)
                return;

            List<LCGNetworkZone> zones = GetSceneComponents<LCGNetworkZone>(scene);
            ValidateZones(zones);

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            SceneManager.MoveGameObjectToScene(runtimeObject, scene);
            LCGRuntime runtime = runtimeObject.AddUdonSharpComponent<LCGRuntime>();

            GameObject mailboxObject = new GameObject(MailboxObjectName);
            SceneManager.MoveGameObjectToScene(mailboxObject, scene);
            mailboxObject.AddComponent<VRCPlayerObject>();
            LCGRuntimePlayer mailbox = mailboxObject.AddUdonSharpComponent<LCGRuntimePlayer>();
            mailbox.Configure(runtime);

            int nextReceiverId = 0;
            for (int zoneIndex = 0; zoneIndex < zones.Count; zoneIndex++)
            {
                LCGNetworkZone zone = zones[zoneIndex];
                List<GameObject> scopedObjects = GetScopedObjects(zone);
                ValidateZoneBehaviours(zone, scopedObjects);

                List<GameObject> protectedObjects = new List<GameObject>();
                for (int i = 0; i < scopedObjects.Count; i++)
                {
                    GameObject target = scopedObjects[i];
                    UdonBehaviour[] targetReceivers = target.GetComponents<UdonBehaviour>();

                    VRCObjectSync objectSync = target.GetComponent<VRCObjectSync>();
                    bool needsOwnership = objectSync != null || targetReceivers.Length > 0 ||
                                          target.GetComponent<VRCPickup>() != null;
                    if (!needsOwnership)
                        continue;

                    protectedObjects.Add(target);
                    LCGZoneOwnershipGuard guard = target.GetComponent<LCGZoneOwnershipGuard>();
                    if (guard == null)
                        guard = target.AddUdonSharpComponent<LCGZoneOwnershipGuard>();
                    guard.Configure(zone);

                    if (objectSync != null)
                    {
                        Object.DestroyImmediate(objectSync);
                        LCGManualObjectSync manualSync = target.GetComponent<LCGManualObjectSync>();
                        if (manualSync == null)
                            manualSync = target.AddUdonSharpComponent<LCGManualObjectSync>();
                        manualSync.Configure(runtime, zone, nextReceiverId++);
                    }
                }

                zone.Configure(zoneIndex + 1, runtime, protectedObjects.ToArray(),
                    scopedObjects.Where(target => target != zone.gameObject).ToArray());
            }

            BuildPacketRegistry(scene, runtime, zones, out UdonBehaviour[] receivers, out string[] addresses,
                out int[] addressReceiverIds, out int[] authorities, out int[] kinds, out int[] parameterOffsets,
                out int[] parameterCounts, out string[] parameterNames, out int[] parameterTypes,
                out string[] callbackEvents, out string[] callbackParameters, out int[] valueTypes,
                out object[] defaultValues);
            runtime.Configure(mailbox, zones.ToArray(), receivers, addresses, addressReceiverIds, authorities,
                kinds, parameterOffsets, parameterCounts, parameterNames, parameterTypes,
                callbackEvents, callbackParameters, valueTypes);
            runtime.SetPacketDefaultValues(defaultValues);
            CopyProxyState(runtime, mailbox, zones);
            ConfigureNetworkIds();
        }

        private static void BuildPacketRegistry(Scene scene, LCGRuntime runtime, List<LCGNetworkZone> zones,
            out UdonBehaviour[] receivers, out string[] addresses, out int[] addressReceiverIds,
            out int[] authorities, out int[] kinds, out int[] parameterOffsets, out int[] parameterCounts,
            out string[] parameterNames, out int[] parameterTypes, out string[] callbackEvents,
            out string[] callbackParameters, out int[] valueTypes, out object[] defaultValues)
        {
            List<UdonBehaviour> receiverList = new List<UdonBehaviour>();
            List<string> addressList = new List<string>();
            List<int> addressReceiverIdList = new List<int>();
            List<int> authorityList = new List<int>();
            List<int> kindList = new List<int>();
            List<int> parameterOffsetList = new List<int>();
            List<int> parameterCountList = new List<int>();
            List<string> parameterNameList = new List<string>();
            List<int> parameterTypeList = new List<int>();
            List<string> callbackEventList = new List<string>();
            List<string> callbackParameterList = new List<string>();
            List<int> valueTypeList = new List<int>();
            List<object> defaultValueList = new List<object>();
            List<UdonBehaviour> allUdonSharpBehaviours = new List<UdonBehaviour>();

            foreach (UdonBehaviour behaviour in GetSceneComponents<UdonBehaviour>(scene))
            {
                if (!(behaviour.programSource is UdonSharpProgramAsset programAsset))
                    continue;
                allUdonSharpBehaviours.Add(behaviour);

                List<FieldDefinition> packetFields = programAsset.fieldDefinitions == null
                    ? new List<FieldDefinition>()
                    : programAsset.fieldDefinitions.Values
                        .Where(field => field.GetAttribute<LCGPacketAttribute>() != null)
                        .ToList();
                Type sourceType = programAsset.sourceCsScript != null ? programAsset.sourceCsScript.GetClass() : null;
                MethodInfo[] packetMethods = sourceType == null
                    ? Array.Empty<MethodInfo>()
                    : sourceType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(method => method.GetCustomAttribute<LCGPacketAttribute>() != null)
                        .ToArray();
                if (packetFields.Count == 0 && packetMethods.Length == 0)
                    continue;

                int receiverId = receiverList.Count;
                receiverList.Add(behaviour);
                LCGNetworkZone nearestZone = behaviour.GetComponentInParent<LCGNetworkZone>(true);
                int zoneId = nearestZone != null ? nearestZone.ZoneId : 0;

                foreach (FieldDefinition field in packetFields)
                {
                    LCGPacketAttribute packet = field.GetAttribute<LCGPacketAttribute>();
                    addressList.Add(field.Name);
                    addressReceiverIdList.Add(receiverId);
                    authorityList.Add((int)packet.Authority);
                    kindList.Add(0);
                    parameterOffsetList.Add(-1);
                    parameterCountList.Add(0);
                    int valueType = GetPacketTypeTag(field.SystemType);
                    if (valueType == 0)
                        throw new BuildFailedException(
                            $"LCG packet field '{sourceType?.FullName}.{field.Name}' has an unsupported wire type '{field.SystemType}'.");
                    valueTypeList.Add(valueType);
                    object defaultValue = null;
                    UdonSharpBehaviour proxy = UdonSharpEditorUtility.GetProxyBehaviour(behaviour);
                    FieldInfo sourceField = sourceType?.GetField(field.Name,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (proxy != null && sourceField != null)
                        defaultValue = sourceField.GetValue(proxy);
                    else
                        behaviour.publicVariables.TryGetVariableValue(field.Name, out defaultValue);
                    if (defaultValue is Array defaultArray)
                        defaultValue = defaultArray.Clone();
                    defaultValueList.Add(defaultValue);
                    if (!string.IsNullOrEmpty(packet.Callback))
                    {
                        int callbackIndex = Array.IndexOf(programAsset.LCGCallbackSourceNames, packet.Callback);
                        if (callbackIndex < 0 || callbackIndex >= programAsset.LCGCallbackEventNames.Length ||
                            callbackIndex >= programAsset.LCGCallbackParameterNames.Length)
                            throw new BuildFailedException(
                                $"LCG callback metadata for '{sourceType?.FullName}.{packet.Callback}' is missing or stale. Recompile UdonSharp scripts before building.");
                        callbackEventList.Add(programAsset.LCGCallbackEventNames[callbackIndex]);
                        callbackParameterList.Add(programAsset.LCGCallbackParameterNames[callbackIndex]);
                    }
                    else
                    {
                        callbackEventList.Add(string.Empty);
                        callbackParameterList.Add(string.Empty);
                    }
                }

                foreach (MethodInfo method in packetMethods)
                {
                    LCGPacketAttribute packet = method.GetCustomAttribute<LCGPacketAttribute>();
                    NetworkCallingEntrypointMetadata metadata = programAsset.NetworkCallingMetadata?
                        .FirstOrDefault(entry => entry.Name == method.Name);
                    ParameterInfo[] sourceParameters = method.GetParameters();
                    if (metadata == null || metadata.Parameters == null ||
                        metadata.Parameters.Length != sourceParameters.Length)
                        throw new BuildFailedException(
                            $"LCG packet metadata for '{sourceType.FullName}.{method.Name}' is missing or stale. Recompile UdonSharp scripts before building.");

                    addressList.Add(method.Name);
                    addressReceiverIdList.Add(receiverId);
                    authorityList.Add((int)packet.Authority);
                    kindList.Add(1);
                    parameterOffsetList.Add(parameterNameList.Count);
                    parameterCountList.Add(sourceParameters.Length);
                    valueTypeList.Add(0);
                    defaultValueList.Add(null);
                    callbackEventList.Add(string.Empty);
                    callbackParameterList.Add(string.Empty);
                    for (int i = 0; i < sourceParameters.Length; i++)
                    {
                        int typeTag = GetPacketTypeTag(sourceParameters[i].ParameterType);
                        if (typeTag == 0)
                            throw new BuildFailedException(
                                $"LCG packet method '{sourceType.FullName}.{method.Name}' has an unsupported parameter type '{sourceParameters[i].ParameterType}'.");
                        parameterNameList.Add(metadata.Parameters[i].Name);
                        parameterTypeList.Add(typeTag);
                    }
                }
            }

            UdonBehaviour runtimeBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(runtime);
            for (int i = 0; i < allUdonSharpBehaviours.Count; i++)
            {
                UdonBehaviour behaviour = allUdonSharpBehaviours[i];
                int receiverId = receiverList.IndexOf(behaviour);
                LCGNetworkZone nearestZone = behaviour.GetComponentInParent<LCGNetworkZone>(true);
                int zoneId = nearestZone != null ? nearestZone.ZoneId : 0;
                behaviour.publicVariables.TrySetVariableValue("__lcgRuntime", runtimeBacking);
                behaviour.publicVariables.TrySetVariableValue("__lcgReceiverId", receiverId);
                behaviour.publicVariables.TrySetVariableValue("__lcgZoneId", zoneId);
            }

            receivers = receiverList.ToArray();
            addresses = addressList.ToArray();
            addressReceiverIds = addressReceiverIdList.ToArray();
            authorities = authorityList.ToArray();
            kinds = kindList.ToArray();
            parameterOffsets = parameterOffsetList.ToArray();
            parameterCounts = parameterCountList.ToArray();
            parameterNames = parameterNameList.ToArray();
            parameterTypes = parameterTypeList.ToArray();
            callbackEvents = callbackEventList.ToArray();
            callbackParameters = callbackParameterList.ToArray();
            valueTypes = valueTypeList.ToArray();
            defaultValues = defaultValueList.ToArray();
        }

        private static int GetPacketTypeTag(Type type)
        {
            bool isArray = type.IsArray;
            Type elementType = isArray ? type.GetElementType() : type;
            int tag;
            if (elementType == typeof(bool)) tag = (int)LCGPacketType.Boolean;
            else if (elementType == typeof(sbyte)) tag = (int)LCGPacketType.SByte;
            else if (elementType == typeof(byte)) tag = (int)LCGPacketType.Byte;
            else if (elementType == typeof(short)) tag = (int)LCGPacketType.Int16;
            else if (elementType == typeof(ushort)) tag = (int)LCGPacketType.UInt16;
            else if (elementType == typeof(int)) tag = (int)LCGPacketType.Int32;
            else if (elementType == typeof(uint)) tag = (int)LCGPacketType.UInt32;
            else if (elementType == typeof(long)) tag = (int)LCGPacketType.Int64;
            else if (elementType == typeof(ulong)) tag = (int)LCGPacketType.UInt64;
            else if (elementType == typeof(float)) tag = (int)LCGPacketType.Single;
            else if (elementType == typeof(double)) tag = (int)LCGPacketType.Double;
            else if (elementType == typeof(char)) tag = (int)LCGPacketType.Char;
            else if (elementType == typeof(string)) tag = (int)LCGPacketType.String;
            else if (elementType == typeof(Vector2)) tag = (int)LCGPacketType.Vector2;
            else if (elementType == typeof(Vector3)) tag = (int)LCGPacketType.Vector3;
            else if (elementType == typeof(Vector4)) tag = (int)LCGPacketType.Vector4;
            else if (elementType == typeof(Quaternion)) tag = (int)LCGPacketType.Quaternion;
            else if (elementType == typeof(Color)) tag = (int)LCGPacketType.Color;
            else if (elementType == typeof(Color32)) tag = (int)LCGPacketType.Color32;
            else return 0;
            return isArray ? tag | (int)LCGPacketType.ArrayFlag : tag;
        }

        private static void ValidateZones(List<LCGNetworkZone> zones)
        {
            for (int i = 0; i < zones.Count; i++)
            {
                Collider collider = zones[i].GetComponent<Collider>();
                if (collider == null || !collider.isTrigger)
                    throw new BuildFailedException($"LCGNetworkZone '{GetPath(zones[i].transform)}' requires a trigger Collider.");

                for (int j = i + 1; j < zones.Count; j++)
                {
                    Transform first = zones[i].transform;
                    Transform second = zones[j].transform;
                    bool hierarchical = first.IsChildOf(second) || second.IsChildOf(first);
                    Collider secondCollider = zones[j].GetComponent<Collider>();
                    if (!hierarchical && secondCollider != null && collider.bounds.Intersects(secondCollider.bounds))
                        throw new BuildFailedException(
                            $"LCG network zones '{GetPath(first)}' and '{GetPath(second)}' overlap but are not parent/child zones.");
                }
            }
        }

        private static void ValidateZoneBehaviours(LCGNetworkZone zone, List<GameObject> scopedObjects)
        {
            foreach (GameObject target in scopedObjects)
            {
                foreach (UdonBehaviour behaviour in target.GetComponents<UdonBehaviour>())
                {
                    if (behaviour.SyncMethod == Networking.SyncType.Continuous)
                        throw new BuildFailedException(
                            $"Continuous Udon networking is not supported inside LCGNetworkZone '{GetPath(zone.transform)}': '{GetPath(target.transform)}'.");

                    if (behaviour.programSource is UdonSharpProgramAsset programAsset &&
                        programAsset.fieldDefinitions != null && programAsset.fieldDefinitions.Values.Any(field =>
                            field.SyncMode.HasValue && field.SyncMode.Value != UdonSyncMode.NotSynced))
                        throw new BuildFailedException(
                            $"UdonSynced fields below LCGNetworkZone '{GetPath(zone.transform)}' require the generated zone program variant, which is not available for '{GetPath(target.transform)}'. Build stopped to prevent native sync from leaking outside the zone.");

                    if (!(behaviour.programSource is UdonSharpProgramAsset))
                        throw new BuildFailedException(
                            $"Udon Graph behaviour cannot be safely inspected or scoped by LCGNetworkZone: '{GetPath(target.transform)}'.");
                }
            }
        }

        private static List<GameObject> GetScopedObjects(LCGNetworkZone zone)
        {
            Transform[] descendants = zone.GetComponentsInChildren<Transform>(true);
            List<GameObject> result = new List<GameObject>(descendants.Length);
            foreach (Transform descendant in descendants)
            {
                LCGNetworkZone nearestZone = descendant.GetComponentInParent<LCGNetworkZone>(true);
                if (nearestZone == zone)
                    result.Add(descendant.gameObject);
            }

            return result;
        }

        private static List<T> GetSceneComponents<T>(Scene scene) where T : Component
        {
            List<T> result = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        private static void CopyProxyState(LCGRuntime runtime, LCGRuntimePlayer mailbox,
            IEnumerable<LCGNetworkZone> zones)
        {
            UdonSharpEditorUtility.CopyProxyToUdon(runtime);
            UdonSharpEditorUtility.CopyProxyToUdon(mailbox);
            foreach (LCGNetworkZone zone in zones)
            {
                UdonSharpEditorUtility.CopyProxyToUdon(zone);
                foreach (LCGZoneOwnershipGuard guard in zone.GetComponentsInChildren<LCGZoneOwnershipGuard>(true))
                    UdonSharpEditorUtility.CopyProxyToUdon(guard);
                foreach (LCGManualObjectSync sync in zone.GetComponentsInChildren<LCGManualObjectSync>(true))
                    UdonSharpEditorUtility.CopyProxyToUdon(sync);
            }
        }

        private static void ConfigureNetworkIds()
        {
            VRC_SceneDescriptor descriptor = VRC_SceneDescriptor.Instance;
            if (descriptor == null)
                return;

            NetworkIDAssignment.ConfigureNetworkIDs(descriptor,
                out List<NetworkIDAssignment.SetErrorLocation> errors,
                NetworkIDAssignment.SetError.InvalidObject);
            if (errors.Count > 0)
                throw new BuildFailedException($"LCG runtime network-ID configuration failed with {errors.Count} error(s).");
        }

        private static string GetPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
