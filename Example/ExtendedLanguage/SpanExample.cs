using System;
using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples.ExtendedLanguage
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
    public class SpanExample : UdonSharpBehaviour
    {
        [SerializeField] private int[] values = { 1, 2, 3, 4, 5 };

        public override void Interact()
        {
            // This span never exists at runtime. The compiler represents it as the
            // source array plus checked offset and length integer locals.
            Span<int> window = values.AsSpan(1, 3);
            window[0] = 20;
            int length = window.Length;
            window.Fill(length);
            int[] snapshot = window.ToArray();
            window.Clear();

            Debug.Log("[Span] copied=" + snapshot.Length + ", backing=" + values[1]);
        }

        // Span fields, returns, boxing/capture, stackalloc, unmanaged memory, and
        // spans crossing await are rejected because Udon has no safe representation.
    }
}
