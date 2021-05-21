using System;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using System.Threading.Tasks;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using NPCStatRescaler.Settings;

namespace NPCStatRescaler
{
    public class Program
    {
        private static Lazy<Config> _settings = null!;
        private static ILoadOrder<IModListing<ISkyrimModGetter>> _loadOrder = null!;
        private static ISkyrimMod _patchMod = null!;
        private static ILinkCache<ISkyrimMod, ISkyrimModGetter> _linkCache = null!;
        private static Spell _healthRegenAbility = null!;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings("settings", "settings.json", out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "YourPatcher.esp")
                .Run(args);
        }

        private static void CreateAbility()
        {
            var healthRegenAbility = _patchMod.Spells.AddNew("NPC_stat_rescaler_health_regen_ability");
            healthRegenAbility.EquipmentType = Skyrim.EquipType.EitherHand.AsNullable();
            healthRegenAbility.Name = "Heath Regen";
            healthRegenAbility.BaseCost = 23337;
            healthRegenAbility.Type = SpellType.Ability;
            healthRegenAbility.ChargeTime = 0.5f;
            healthRegenAbility.Effects.Add(new Effect
            {
                BaseEffect = Skyrim.MagicEffect.AbDamageHealRate.AsNullable(),
                Data = new EffectData {Magnitude = 100}
            });
            _healthRegenAbility = healthRegenAbility;
        }

        private static void PatchClasses()
        {
            foreach (IClassGetter classRecord in _loadOrder.PriorityOrder.Class().WinningOverrides())
            {
                var classCopy = _patchMod.Classes.GetOrAddAsOverride(classRecord);
                classCopy.StatWeights[BasicStat.Health] =
                    (byte) Math.Round(classCopy.StatWeights[BasicStat.Health] * _settings.Value.Class.HealthScale);
                classCopy.StatWeights[BasicStat.Stamina] =
                    (byte) Math.Round(classCopy.StatWeights[BasicStat.Stamina] * _settings.Value.Class.StaminaScale);
                classCopy.StatWeights[BasicStat.Magicka] =
                    (byte) Math.Round(classCopy.StatWeights[BasicStat.Magicka] * _settings.Value.Class.MagickaScale);
            }
        }

        private static void PatchNPCs()
        {
            foreach (INpcGetter npc in _loadOrder.PriorityOrder.Npc()
                .WinningOverrides()
                .Where(npc =>
                    (!npc.Equals(Skyrim.Npc.dunBluePalacePelagiusSuspicious) &&
                     !npc.Equals(Skyrim.Npc.dunBluePalacePelagiusNightmare)) &&
                     npc.Configuration.HealthOffset != 0 && Math.Abs(_settings.Value.NpcOffsetMults.HealthOffsetMult - 1) > 0.001 ||
                     npc.Configuration.StaminaOffset != 0 && Math.Abs(_settings.Value.NpcOffsetMults.StaminaOffsetMult - 1) > 0.001 ||
                     npc.Configuration.MagickaOffset != 0 && Math.Abs(_settings.Value.NpcOffsetMults.MagickaOffsetMult - 1) > 0.001))
            {
                var npcCopy = _patchMod.Npcs.GetOrAddAsOverride(npc);
                if (npcCopy.Equals(Skyrim.Npc.Player))
                {
                    npcCopy.Configuration.HealthOffset = 0;
                    npcCopy.Configuration.StaminaOffset = 0;
                    npcCopy.Configuration.MagickaOffset = 0;
                    continue;
                }

                npcCopy.Configuration.HealthOffset = (short) Math.Round(npcCopy.Configuration.HealthOffset *
                                                                        _settings.Value.NpcOffsetMults
                                                                            .HealthOffsetMult);
                npcCopy.Configuration.StaminaOffset = (short) Math.Round(npcCopy.Configuration.StaminaOffset *
                                                                         _settings.Value.NpcOffsetMults
                                                                             .StaminaOffsetMult);
                npcCopy.Configuration.MagickaOffset = (short) Math.Round(npcCopy.Configuration.MagickaOffset *
                                                                         _settings.Value.NpcOffsetMults
                                                                             .MagickaOffsetMult);
            }
        }

        private static void PatchGameSettings()
        {
            GameSettingFloat gmstCopy =
                (GameSettingFloat) _patchMod.GameSettings.GetOrAddAsOverride(Skyrim.GameSetting.fNPCHealthLevelBonus,
                    _linkCache);
            gmstCopy.Data = _settings.Value.NpcHealthBonusPerLevel;

            gmstCopy = (GameSettingFloat) _patchMod.GameSettings.GetOrAddAsOverride(
                Skyrim.GameSetting.fCombatHealthRegenRateMult, _linkCache);
            gmstCopy.Data = _settings.Value.PlayerCombatRegenPenalties.HealthCombatRegenPenalty;

            gmstCopy = (GameSettingFloat) _patchMod.GameSettings.GetOrAddAsOverride(
                Skyrim.GameSetting.fCombatStaminaRegenRateMult, _linkCache);
            gmstCopy.Data = _settings.Value.PlayerCombatRegenPenalties.StaminaCombatRegenPenalty;

            gmstCopy = (GameSettingFloat) _patchMod.GameSettings.GetOrAddAsOverride(
                Skyrim.GameSetting.fCombatMagickaRegenRateMult, _linkCache);
            gmstCopy.Data = _settings.Value.PlayerCombatRegenPenalties.MagickaCombatRegenPenalty;
        }

        private static void PatchRaces()
        {
            foreach (IRaceGetter race in _loadOrder.PriorityOrder.Race().WinningOverrides())
            {
                var raceCopy = _patchMod.Races.GetOrAddAsOverride(race);
                raceCopy.Regen[BasicStat.Health] =
                    raceCopy.Regen[BasicStat.Health] * _settings.Value.HealthRegen.Scale +
                    _settings.Value.HealthRegen.Shift;
                raceCopy.Regen[BasicStat.Stamina] =
                    raceCopy.Regen[BasicStat.Stamina] * _settings.Value.StaminaRegen.Scale +
                    _settings.Value.StaminaRegen.Shift;
                raceCopy.Regen[BasicStat.Magicka] =
                    raceCopy.Regen[BasicStat.Magicka] * _settings.Value.MagickaRegen.Scale +
                    _settings.Value.MagickaRegen.Shift;

                if (Enum.TryParse<Races>(
                    race.EditorID?.Replace("Race", string.Empty).Replace("Vampire", string.Empty)
                        .Replace("Child", string.Empty),
                    out var key))
                {
                    var stats = _settings.Value.RacesStats[key];
                    raceCopy.Starting[BasicStat.Health] = stats.Health;
                    raceCopy.Starting[BasicStat.Stamina] = stats.Stamina;
                    raceCopy.Starting[BasicStat.Magicka] = stats.Magicka;
                    raceCopy.BaseCarryWeight = stats.CarryWeight;
                    raceCopy.ActorEffect ??= new ExtendedList<IFormLinkGetter<ISpellRecordGetter>>();
                    if ((raceCopy.EditorID?.Contains("Vampire") ?? false) &&
                        (race.EditorID?.ToLower().Contains("dlc1") ?? false) &&
                        _settings.Value.HealthRegen.DebuffVampires)
                    {
                        raceCopy.ActorEffect.Add(_healthRegenAbility);
                    }
                    else if (!raceCopy.EditorID?.Contains("Vampire") ?? false)
                    {
                        raceCopy.ActorEffect.Add(_healthRegenAbility);
                    }
                }
                else
                {
                    raceCopy.Starting[BasicStat.Health] =
                        raceCopy.Starting[BasicStat.Health] * _settings.Value.CommonStats.HealthScale +
                        _settings.Value.CommonStats.HealthShift;
                    raceCopy.Starting[BasicStat.Stamina] =
                        raceCopy.Starting[BasicStat.Stamina] * _settings.Value.CommonStats.StaminaScale +
                        _settings.Value.CommonStats.StaminaShift;
                    raceCopy.Starting[BasicStat.Magicka] =
                        raceCopy.Starting[BasicStat.Magicka] * _settings.Value.CommonStats.MagickaScale +
                        _settings.Value.CommonStats.MagickaShift;
                }

                if (race.EditorID?.ToLower().Contains("child") ?? false)
                {
                    raceCopy.Starting[BasicStat.Health] *= _settings.Value.StatScales[ChildVampire.Child].HealthScale;
                    raceCopy.Starting[BasicStat.Stamina] *= _settings.Value.StatScales[ChildVampire.Child].StaminaScale;
                    raceCopy.Starting[BasicStat.Magicka] *= _settings.Value.StatScales[ChildVampire.Child].MagickaScale;
                    raceCopy.BaseCarryWeight *= _settings.Value.StatScales[ChildVampire.Child].CarryWeightScale;
                }

                if ((race.EditorID?.ToLower().Contains("vampire") ?? false) &&
                    !(race.EditorID?.ToLower().Contains("dlc1") ?? false))
                {
                    raceCopy.Starting[BasicStat.Health] *= _settings.Value.StatScales[ChildVampire.Vampire].HealthScale;
                    raceCopy.Starting[BasicStat.Stamina] *=
                        _settings.Value.StatScales[ChildVampire.Vampire].StaminaScale;
                    raceCopy.Starting[BasicStat.Magicka] *=
                        _settings.Value.StatScales[ChildVampire.Vampire].MagickaScale;
                    raceCopy.BaseCarryWeight *= _settings.Value.StatScales[ChildVampire.Vampire].CarryWeightScale;
                }
            }
        }

        /*private static void PatchSpells()
        {
            static bool Predicate(IEffectGetter x)
            {
                if (x.Data == null) return false;
                var effect = x.BaseEffect.Resolve(_linkCache);
                return effect.Archetype.ActorValue == ActorValue.MagickaRate;
            }

            List<ISpellGetter> spellsToPatch = _loadOrder.PriorityOrder.Spell().WinningOverrides()
                .AsParallel()
                .Where(spell => spell.Effects.Any(Predicate))
                .ToList();
            foreach (var spellCopy in spellsToPatch.Select(spell => _patchMod.Spells.GetOrAddAsOverride(spell)))
            {
                spellCopy.Effects.Where(Predicate).ForEach(x =>
                {
                    x.Data!.Magnitude *= _settings.Value.MagickaRegen.Scale;
                });
            }
        }*/

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            _linkCache = state.LinkCache;
            _loadOrder = state.LoadOrder;
            _patchMod = state.PatchMod;
            CreateAbility();
            PatchClasses();
            PatchNPCs();
            PatchGameSettings();
            PatchRaces();
        }
    }
}