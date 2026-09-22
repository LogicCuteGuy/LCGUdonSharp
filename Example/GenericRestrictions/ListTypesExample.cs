using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;

namespace LogicCuteGuy.LCGUdonSharp.Examples.GenericRestrictions
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class ListTypesExample : UdonSharpBehaviour
    {
        // Both normal C# collections and legacy VRC data containers can coexist in one project.
        [UdonSynced, NonSerialized] private List<int> syncedValues = new List<int>();

        private readonly Dictionary<string, int> scores = new Dictionary<string, int>
        {
            { "alpha", 10 },
            { "beta", 20 },
        };

        [HideInInspector] public int jsonRoundTripCount;
        [HideInInspector] public int binaryRoundTripValue;

        public bool TryAdd(int value)
        {
            if (syncedValues == null)
                syncedValues = new List<int>();

            syncedValues.Add(value);
            return syncedValues.Contains(value);
        }

        public int GetValue(int index)
        {
            if (syncedValues == null || index < 0 || index >= syncedValues.Count)
                return 0;
            return syncedValues[index];
        }

        public override void Interact()
        {
            TryAdd(syncedValues == null ? 1 : syncedValues.Count + 1);
            scores["count"] = syncedValues.Count;

            RunCollectionValidation();

            RequestSerialization();
            Debug.Log("[Collections] JSON entries: " + jsonRoundTripCount +
                      ", binary result: " + binaryRoundTripValue);
        }

        public void RunCollectionValidation()
        {
            var tagged = new Dictionary<int, string>
            {
                { 7, "seven" },
                { 9, "nine" },
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(tagged, options);
            Dictionary<int, string> decoded = JsonSerializer.Deserialize<Dictionary<int, string>>(json);
            jsonRoundTripCount = decoded.Count;

            List<int> nullList = null;
            string nullJson = JsonSerializer.Serialize(nullList);
            if (JsonSerializer.Deserialize<List<int>>(nullJson) == null)
                jsonRoundTripCount++;

            Dictionary<string, int> invalidNumbers;
            string numericError;
            if (!JsonSerializer.TryDeserialize("{\"value\":1.5}", out invalidNumbers, out numericError))
                jsonRoundTripCount++;

            byte[] utf8 = Encoding.UTF8.GetBytes(json);
            int firstByte = utf8.Length == 0 ? 0 : utf8[0];
            int bitMask = (firstByte << 1) | 1;
            byte[] countBytes = BitConverter.GetBytes(decoded.Count);
            byte[] copy = new byte[countBytes.Length];
            Buffer.BlockCopy(countBytes, 0, copy, 0, countBytes.Length);
            int floatBits = (int)new DataToken(1f).Bitcast(TokenType.Int);
            binaryRoundTripValue = BitConverter.ToInt32(copy, 0) + bitMask + floatBits;
        }
    }
}
