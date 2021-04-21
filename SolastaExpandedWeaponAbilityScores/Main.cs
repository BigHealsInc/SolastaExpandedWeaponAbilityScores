using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json.Linq;
using SolastaModApi;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;

namespace SolastaExpandedWeaponAbilityScores
{
    public class Main
    {
        public static Guid ModGuidNamespace = new Guid("00c6789d-2fc7-4623-a854-b37cd2f3b07f");

        // [System.Diagnostics.Conditional("DEBUG")]
        public static void Log(string msg)
        {
            if (logger != null) logger.Log(msg);
        }

        public static void Error(Exception ex)
        {
            if (logger != null) logger.Error(ex.ToString());
        }

        public static void Error(string msg)
        {
            if (logger != null) logger.Error(msg);
        }

        public static UnityModManager.ModEntry.ModLogger logger;
        public static bool enabled;

        public static void LoadTranslations()
        {
            var languageSourceData = LocalizationManager.Sources[0];
            var translations = JObject.Parse(File.ReadAllText(UnityModManager.modsPath + @"/SolastaExpandedWeaponAbilityScores/Translations.json"));
            foreach (var translationKey in translations)
            {
                foreach (var translationLanguage in (JObject)translationKey.Value)
                {
                    var languageIndex = languageSourceData.GetLanguageIndex(translationLanguage.Key);
                    if (languageIndex >= 0)
                    {
                        languageSourceData.AddTerm(translationKey.Key).Languages[languageIndex] = translationLanguage.Value.ToString();
                    }
                }
            }
        }

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                logger = modEntry.Logger;

                ModBeforeDBReady();

                var harmony = new Harmony(modEntry.Info.Id);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Error(ex);
                throw;
            }
            return true;
        }

        [HarmonyPatch(typeof(MainMenuScreen), "RuntimeLoaded")]
        static class MainMenuScreen_RuntimeLoaded_Patch
        {
            static void Postfix()
            {
                ModAfterDBReady();
            }
        }

        // ENTRY POINT IF YOU NEED SERVICE LOCATORS ACCESS
        static void ModBeforeDBReady()
        {
            LoadTranslations();
        }

        // ENTRY POINT IF YOU NEED SAFE DATABASE ACCESS
        static void ModAfterDBReady()
        {
            WeaponTypeDefinitionCopyBuilder(DatabaseHelper.WeaponTypeDefinitions.ShortswordType, "WhipType", "SimpleWeaponCategory");
            WeaponTypeDefinitionCopyBuilder(DatabaseHelper.WeaponTypeDefinitions.ShortswordType, "SickleType", "SimpleWeaponCategory");

            foreach (WeaponTypeDefinition wtd in DatabaseRepository.GetDatabase<WeaponTypeDefinition>().GetAllElements())
            {
                Console.WriteLine(wtd.name);
            }

            List<string> whipWeaponTags = new List<string>() { "Finesse" };
            List<string> sickleWeaponTags = new List<string>() { "Light" };

            ItemDefinitionCopyBuilder(DatabaseHelper.ItemDefinitions.Shortsword, "Whip", "WhipType", whipWeaponTags);
            ItemDefinitionCopyBuilder(DatabaseHelper.ItemDefinitions.Shortsword, "Sickle", "SickleType", sickleWeaponTags);

            String weaponType;
            var allWeapons = DatabaseRepository.GetDatabase<ItemDefinition>().GetAllElements().Where<ItemDefinition>(o => o.IsWeapon == true);
            foreach (ItemDefinition weapon in allWeapons)
            {
                weaponType = weapon.WeaponDescription.WeaponType;
                if (weaponType == "QuarterstaffType" || weaponType == "DaggerType" || weaponType == "ShortswordType")
                    weapon.WeaponDescription.WeaponTags.Add("Knowledge");
                if (weaponType == "ClubType" || weaponType == "MaceType" || weaponType == "SpearType")
                    weapon.WeaponDescription.WeaponTags.Add("Intuition");
                if (weaponType == "MorningstarType")
                    weapon.WeaponDescription.WeaponTags.Add("Vigor");
                if (weaponType == "WhipType" || weaponType == "SickleType")
                    Console.WriteLine("!!!Adding Influence Weapon!!!");
                    weapon.WeaponDescription.WeaponTags.Add("Influence");
            }
        }

        [HarmonyPatch(typeof(RulesetCharacterHero), "RefreshAttackMode")]
        class Patch
        {
            static void Postfix(RulesetCharacterHero __instance, ref RulesetAttackMode __result, ref WeaponDescription weaponDescription)
            {
                DamageForm firstDamageForm = __result.EffectDescription.FindFirstDamageForm();
                WeaponTypeDefinition element = DatabaseRepository.GetDatabase<WeaponTypeDefinition>().GetElement(weaponDescription.WeaponType);
                int originalAbilityScoreModifier = AttributeDefinitions.ComputeAbilityScoreModifier(__instance.Attributes[__result.AbilityScore].CurrentValue);

                __result.AbilityScore = element.WeaponProximity == RuleDefinitions.AttackProximity.Melee ? "Strength" : "Dexterity";
                if (weaponDescription.WeaponTags.Contains("Finesse") && __instance.GetAttribute("Dexterity").CurrentValue > __instance.GetAttribute(__result.AbilityScore).CurrentValue)
                    __result.AbilityScore = "Dexterity";
                if (weaponDescription.WeaponTags.Contains("Knowledge") && __instance.GetAttribute("Intelligence").CurrentValue > __instance.GetAttribute(__result.AbilityScore).CurrentValue)
                    __result.AbilityScore = "Intelligence";
                if (weaponDescription.WeaponTags.Contains("Intuition") && __instance.GetAttribute("Wisdom").CurrentValue > __instance.GetAttribute(__result.AbilityScore).CurrentValue)
                    __result.AbilityScore = "Wisdom";
                if (weaponDescription.WeaponTags.Contains("Vigor") && __instance.GetAttribute("Constitution").CurrentValue > __instance.GetAttribute(__result.AbilityScore).CurrentValue)
                    __result.AbilityScore = "Constitution";
                if (weaponDescription.WeaponTags.Contains("Influence") && __instance.GetAttribute("Charisma").CurrentValue > __instance.GetAttribute(__result.AbilityScore).CurrentValue)
                    __result.AbilityScore = "Charisma";

                int abilityScoreModifier = AttributeDefinitions.ComputeAbilityScoreModifier(__instance.Attributes[__result.AbilityScore].CurrentValue);

                Predicate<RuleDefinitions.TrendInfo> predicate = FindAbilityScoreTrend;
                int ability_score_to_hit_trend_index = __result.ToHitBonusTrends.FindIndex(predicate);
                int ability_score_damage_bonus_trend_index = firstDamageForm.DamageBonusTrends.FindIndex(predicate);

                if (ability_score_to_hit_trend_index >= 0)
                {
                    __result.ToHitBonus -= originalAbilityScoreModifier;
                    __result.ToHitBonus += abilityScoreModifier;
                    __result.ToHitBonusTrends.RemoveAt(ability_score_to_hit_trend_index);
                    __result.ToHitBonusTrends.Add(new RuleDefinitions.TrendInfo(abilityScoreModifier, RuleDefinitions.FeatureSourceType.AbilityScore, __result.AbilityScore, (object)null));
                }

                if (ability_score_damage_bonus_trend_index >= 0)
                {
                    firstDamageForm.DamageBonusTrends.RemoveAt(ability_score_damage_bonus_trend_index);
                    firstDamageForm.BonusDamage -= originalAbilityScoreModifier;
                    firstDamageForm.BonusDamage += abilityScoreModifier;
                    firstDamageForm.DamageBonusTrends.Add(new RuleDefinitions.TrendInfo(abilityScoreModifier, RuleDefinitions.FeatureSourceType.AbilityScore, __result.AbilityScore, (object)null));
                }
            }
        }

        private static bool FindAbilityScoreTrend(RuleDefinitions.TrendInfo trend)
        {
            return trend.sourceType == RuleDefinitions.FeatureSourceType.AbilityScore;
        }

        private static void WeaponTypeDefinitionCopyBuilder(WeaponTypeDefinition sourceWeaponTypeDefinition, string name, string weaponCategory)
        {
            Guid newGuid = GuidHelper.Create(ModGuidNamespace, name);
            WeaponTypeDefinition builtWeaponTypeDefinition = ScriptableObject.CreateInstance<WeaponTypeDefinition>();

            Traverse.Create(builtWeaponTypeDefinition).Field("weaponCategory").SetValue(weaponCategory);
            Traverse.Create(builtWeaponTypeDefinition).Field("name").SetValue(name);
            builtWeaponTypeDefinition.name = name;
            Traverse.Create(builtWeaponTypeDefinition).Field("isAttachedToBone").SetValue(sourceWeaponTypeDefinition.IsAttachedToBone);
            Traverse.Create(builtWeaponTypeDefinition).Field("animationTag").SetValue(sourceWeaponTypeDefinition.AnimationTag);
            Traverse.Create(builtWeaponTypeDefinition).Field("soundEffectDescription").SetValue(sourceWeaponTypeDefinition.SoundEffectDescription);
            Traverse.Create(builtWeaponTypeDefinition).Field("soundEffectOnHitDescription").SetValue(sourceWeaponTypeDefinition.SoundEffectOnHitDescription);
            Traverse.Create(builtWeaponTypeDefinition).Field("MeleeAttackerParticle").SetValue(sourceWeaponTypeDefinition.MeleeAttackerParticle);
            Traverse.Create(builtWeaponTypeDefinition).Field("MeleeImpactParticle").SetValue(sourceWeaponTypeDefinition.MeleeImpactParticle);
            Traverse.Create(builtWeaponTypeDefinition).Field("ThrowAttackerParticle").SetValue(sourceWeaponTypeDefinition.ThrowAttackerParticle);
            Traverse.Create(builtWeaponTypeDefinition).Field("ThrowImpactParticle").SetValue(sourceWeaponTypeDefinition.ThrowImpactParticle);
            Traverse.Create(builtWeaponTypeDefinition).Field("guiPresentation").SetValue(sourceWeaponTypeDefinition.GuiPresentation);
            Traverse.Create(builtWeaponTypeDefinition).Field("guid").SetValue(newGuid.ToString());
            DatabaseRepository.GetDatabase<WeaponTypeDefinition>().Add(builtWeaponTypeDefinition);
        }

        private static void ItemDefinitionCopyBuilder(ItemDefinition sourceItemDefinition, string name, string weaponType, List<string> weaponTags)
        {
            Guid newGuid = GuidHelper.Create(ModGuidNamespace, name);
            ItemDefinition builtItemDefinition = ScriptableObject.CreateInstance<ItemDefinition>();

            Traverse.Create(builtItemDefinition).Field("name").SetValue(name);
            WeaponDescription sourceWeaponDescription = (WeaponDescription)Traverse.Create(sourceItemDefinition).Field("weaponDefinition").GetValue();
            Traverse.Create(sourceWeaponDescription).Field("weaponType").SetValue(weaponType);
            Traverse.Create(sourceWeaponDescription).Field("weaponTags").SetValue(weaponTags);

            builtItemDefinition.name = name;
            Traverse.Create(builtItemDefinition).Field("inDungeonEditor").SetValue(sourceItemDefinition.InDungeonEditor);
            Traverse.Create(builtItemDefinition).Field("merchantCategory").SetValue(sourceItemDefinition.MerchantCategory);
            Traverse.Create(builtItemDefinition).Field("weight").SetValue(sourceItemDefinition.Weight);
            Traverse.Create(builtItemDefinition).Field("slotTypes").SetValue(sourceItemDefinition.SlotTypes);
            Traverse.Create(builtItemDefinition).Field("slotsWhereActive").SetValue(sourceItemDefinition.SlotsWhereActive);
            Traverse.Create(builtItemDefinition).Field("forceEquipSlot").SetValue(sourceItemDefinition.ForceEquipSlot);
            Traverse.Create(builtItemDefinition).Field("stackSize").SetValue(sourceItemDefinition.StackSize);
            Traverse.Create(builtItemDefinition).Field("defaultStackCount").SetValue(sourceItemDefinition.DefaultStackCount);
            Traverse.Create(builtItemDefinition).Field("costs").SetValue(sourceItemDefinition.Costs);
            Traverse.Create(builtItemDefinition).Field("itemTags").SetValue(sourceItemDefinition.ItemTags);
            Traverse.Create(builtItemDefinition).Field("activeTags").SetValue(sourceItemDefinition.ActiveTags);
            Traverse.Create(builtItemDefinition).Field("inactiveTags").SetValue(sourceItemDefinition.InactiveTags);
            Traverse.Create(builtItemDefinition).Field("requiredAttunementClasses").SetValue(sourceItemDefinition.RequiredAttunementClasses);
            Traverse.Create(builtItemDefinition).Field("staticProperties").SetValue(sourceItemDefinition.StaticProperties);
            Traverse.Create(builtItemDefinition).Field("armorDefinition").SetValue(sourceItemDefinition.ArmorDescription);
            Traverse.Create(builtItemDefinition).Field("isWeapon").SetValue(sourceItemDefinition.IsWeapon);
            Traverse.Create(builtItemDefinition).Field("ammunitionDefinition").SetValue(sourceItemDefinition.AmmunitionDescription);
            Traverse.Create(builtItemDefinition).Field("usableDeviceDescription").SetValue(sourceItemDefinition.UsableDeviceDescription);
            Traverse.Create(builtItemDefinition).Field("toolDefinition").SetValue(sourceItemDefinition.ToolDescription);
            Traverse.Create(builtItemDefinition).Field("starterPackDefinition").SetValue(sourceItemDefinition.StarterPackDescription);
            Traverse.Create(builtItemDefinition).Field("containerItemDefinition").SetValue(sourceItemDefinition.ContainerItemDescription);
            Traverse.Create(builtItemDefinition).Field("lightSourceItemDefinition").SetValue(sourceItemDefinition.LightSourceItemDescription);
            Traverse.Create(builtItemDefinition).Field("focusItemDefinition").SetValue(sourceItemDefinition.FocusItemDescription);
            Traverse.Create(builtItemDefinition).Field("wealthPileDefinition").SetValue(sourceItemDefinition.WealthPileDescription);
            Traverse.Create(builtItemDefinition).Field("spellbookDefinition").SetValue(sourceItemDefinition.SpellbookDescription);
            Traverse.Create(builtItemDefinition).Field("documentDescription").SetValue(sourceItemDefinition.DocumentDescription);
            Traverse.Create(builtItemDefinition).Field("foodDescription").SetValue(sourceItemDefinition.FoodDescription);
            Traverse.Create(builtItemDefinition).Field("factionRelicDescription").SetValue(sourceItemDefinition.FactionRelicDescription);
            Traverse.Create(builtItemDefinition).Field("personalityFlagOccurences").SetValue(sourceItemDefinition.PersonalityFlagOccurences);
            Traverse.Create(builtItemDefinition).Field("soundEffectDescriptionOverride").SetValue(sourceItemDefinition.SoundEffectDescription);
            Traverse.Create(builtItemDefinition).Field("soundEffectOnHitDescriptionOverride").SetValue(sourceItemDefinition.SoundEffectOnHitDescription);
            Traverse.Create(builtItemDefinition).Field("itemPresentation").SetValue(sourceItemDefinition.ItemPresentation);
            Traverse.Create(builtItemDefinition).Field("guiPresentation").SetValue(sourceItemDefinition.GuiPresentation);
            Traverse.Create(builtItemDefinition).Field("guid").SetValue(newGuid.ToString());
            DatabaseRepository.GetDatabase<ItemDefinition>().Add(builtItemDefinition);
        }
    }
}