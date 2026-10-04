using TMPro;
using UdonSharp;
using UnityEngine;

namespace LogicCuteGuy.LCGUdonSharp.Examples
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public sealed class ScriptableObjectEquipmentExample : UdonSharpBehaviour
    {
        // Both Inspector references contain derived assets through the base type.
        public ItemDefinition item;
        public ItemDefinition[] catalog;
        public TextMeshPro display;
        public Renderer displayRenderer;
        [System.NonSerialized] public int coins;
        [System.NonSerialized] public int purchases;
        [System.NonSerialized] public int lastEffect;
        [System.NonSerialized] public bool featureTestsPassed;
        [System.NonSerialized] public int receiverEvaluations;
        [System.NonSerialized] public string status;

        private void Start()
        {
            coins = 100;
            RefreshDisplay();
        }

        public override void Interact()
        {
            if (item == null || item.economy == null) return;
            if (coins < item.economy.price) status = "Not enough coins";
            else
            {
                coins -= item.economy.price;
                purchases++;
                if (item is WeaponDefinition weapon) lastEffect = weapon.damage;
                else
                {
                    SpellDefinition spell = item as SpellDefinition;
                    if (spell != null) lastEffect = spell.power;
                }
                status = "Bought " + item.displayName + " / effect " + lastEffect;
            }
            RefreshDisplay();
        }

        public void SelectNext()
        {
            if (catalog == null || catalog.Length < 2) return;
            item = item is WeaponDefinition ? catalog[1] : catalog[0];
            RefreshDisplay();
        }

        public void TestDataFeatures()
        {
            if (catalog == null || catalog.Length < 2) return;
            ItemDefinition weaponBase = catalog[0];
            ItemDefinition spellBase = catalog[1];
            WeaponDefinition weapon = (WeaponDefinition)weaponBase;
            SpellDefinition spell = (SpellDefinition)spellBase;
            ItemDefinition absent = null;
            bool invalidCastCaught = false;
            try { WeaponDefinition wrong = (WeaponDefinition)spellBase; }
            catch (System.InvalidCastException) { invalidCastCaught = true; }
            ItemDefinition[] localUpgrades = weaponBase.upgrades;
            bool copyValid = localUpgrades != null && localUpgrades.Length == 2 && localUpgrades[1] == null;
            if (copyValid)
            {
                localUpgrades[0] = null;
                copyValid = weaponBase.upgrades[0] is SpellDefinition;
            }
            receiverEvaluations = 0;
            bool singleEvaluation = GetWeaponOnce() is WeaponDefinition evaluatedWeapon && evaluatedWeapon.damage == 45;
            featureTestsPassed = weaponBase is ItemDefinition && weaponBase is WeaponDefinition &&
                !(weaponBase is SpellDefinition) && spellBase is SpellDefinition &&
                (spellBase as WeaponDefinition) == null && !(absent is WeaponDefinition) &&
                (absent as WeaponDefinition) == null && (WeaponDefinition)absent == null &&
                weapon.damage == 45 && spell.power == 80 && spell.manaCost == 12 &&
                weaponBase.economy.price == 35 && spellBase.economy.price == 20 &&
                weaponBase.upgrades[0].economy.price == 20 && invalidCastCaught && copyValid &&
                spellBase.upgrades.Length == 0 && singleEvaluation && receiverEvaluations == 1;
            status = featureTestsPassed ? "Nested / type / cast / array tests: PASS" : "Data tests: FAIL";
            RefreshDisplay();
        }

        private ItemDefinition GetWeaponOnce()
        {
            receiverEvaluations++;
            return catalog[0];
        }

        public void RefreshDisplay()
        {
            if (item == null || item.economy == null) return;
            string kind = "Item";
            string stats = "";
            if (item is WeaponDefinition weapon)
            {
                kind = "Weapon";
                stats = "Damage: " + weapon.damage;
            }
            else if (item is SpellDefinition spell)
            {
                kind = "Spell";
                stats = "Power: " + spell.power + " / Mana: " + spell.manaCost;
            }
            string upgrade = "";
            ItemDefinition[] upgrades = item.upgrades;
            if (upgrades != null && upgrades.Length > 0 && upgrades[0] != null)
                upgrade = "Upgrade: " + upgrades[0].displayName;
            if (display != null)
                display.text = "NESTED + POLYMORPHIC DATA\n" + item.displayName + " / " + kind +
                    "\n" + stats + "\nEconomy asset: " + item.economy.price + " " + item.economy.currency +
                    "\n" + upgrade + "\nCoins: " + coins + " / Purchases: " + purchases +
                    "\n" + status + "\nClick to buy / SelectNext to switch";
            if (displayRenderer != null) displayRenderer.material.color = item.displayColor;
        }
    }
}
