using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    public enum ShopItemRarity { Common, Rare, Legendary }

    [CreateAssetMenu(menuName = "LCGUdonSharp/Examples/Shop Item", fileName = "ShopItem")]
    public sealed class ShopItemData : ScriptableObject
    {
        public string displayName = "Strawberry Milk";
        public int price = 35;
        public ShopItemRarity rarity = ShopItemRarity.Rare;
        public Color displayColor = new Color(1f, 0.45f, 0.65f);
        public string[] descriptions = { "Fresh strawberry milk", "นมสตรอว์เบอร์รี" };
        public Texture2D icon;
        public AudioClip purchaseSound;
    }
}
