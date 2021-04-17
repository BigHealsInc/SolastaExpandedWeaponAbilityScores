using System;
using System.IO;
using System.Reflection;
using UnityModManagerNet;
using HarmonyLib;
using I2.Loc;
using Newtonsoft.Json.Linq;
using SolastaModApi;

namespace SolastaExpandedWeaponAbilityScores
{
    public class Main
    {
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
            String translation = LocalizationManager.GetTranslation("Tooltip/&TagKnowledgeTitle", overrideLanguage: null);
            Console.WriteLine("Translation is: " + translation);
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
            var staff = DatabaseHelper.ItemDefinitions.Quarterstaff;
            staff.WeaponDescription.WeaponTags.Add("Knowledge");
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
                {
                    __result.AbilityScore = "Intelligence";
                }

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
                    __result.ToHitBonus = abilityScoreModifier;
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
    }
}