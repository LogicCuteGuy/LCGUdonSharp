using TMPro;
using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class ScriptableObjectShopExample : UdonSharpBehaviour
    {
        public ShopItemData item;
        public ShopItemData[] catalog;
        public TextMeshPro display;
        public Renderer displayRenderer;
        public AudioSource audioSource;
        public int startingCoins = 100;
        [System.NonSerialized] public int coins;
        [System.NonSerialized] public int purchases;
        [System.NonSerialized] public string lastStatus;
        [System.NonSerialized] public bool snapshotArrayCopyValid;

        private void Start()
        {
            coins = startingCoins;
            RefreshDisplay();
        }

        public override void Interact()
        {
            if (item == null) return;
            if (coins < item.price)
            {
                lastStatus = "Not enough coins";
                RefreshDisplay();
                return;
            }
            coins -= item.price;
            purchases++;
            lastStatus = "Purchased " + item.displayName;
            if (audioSource != null && item.purchaseSound != null)
                audioSource.PlayOneShot(item.purchaseSound);
            RefreshDisplay();
        }

        public void RefreshDisplay()
        {
            if (item == null)
            {
                if (display != null) display.text = "Assign a ShopItemData asset";
                return;
            }
            string[] descriptions = item.descriptions;
            string description = descriptions != null && descriptions.Length > 0 ? descriptions[0] : "";
            string catalogLine = catalog != null && catalog.Length > 1 && catalog[1] != null
                ? "Next: " + catalog[1].displayName + " / " + catalog[1].price + " coins" : "";
            string rarityName = item.rarity == ShopItemRarity.Legendary ? "Legendary" :
                item.rarity == ShopItemRarity.Rare ? "Rare" : "Common";
            if (display != null)
                display.text = "SCRIPTABLEOBJECT SHOP\n" + item.displayName + "\n" +
                    "Price: " + item.price + " coins / " + rarityName + "\n" +
                    description + "\n" + catalogLine + "\n" + "Coins: " + coins +
                    " / Purchases: " + purchases + "\n" + lastStatus + "\nClick to buy";
            if (displayRenderer != null)
            {
                displayRenderer.material.color = item.displayColor;
                if (item.icon != null) displayRenderer.material.mainTexture = item.icon;
            }
            Debug.Log("[ScriptableObject Shop] " + item.displayName + " price=" + item.price +
                " coins=" + coins + " purchases=" + purchases);
        }

        public void TestArrayCopy()
        {
            if (item == null) return;
            string[] copy = item.descriptions;
            if (copy == null || copy.Length == 0) return;
            string original = copy[0];
            copy[0] = "Changed only in the local copy";
            snapshotArrayCopyValid = item.descriptions[0] == original;
            lastStatus = snapshotArrayCopyValid ? "Array copy test: PASS" : "Array copy test: FAIL";
            RefreshDisplay();
        }
    }
}
