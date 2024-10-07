// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using JetBrains.Annotations;
using Newtonsoft.Json;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyAttributes : DifficultyAttributes
    {
        /// <summary>
        /// The difficulty corresponding to the aim skill.
        /// </summary>
        [JsonProperty("aim_difficulty")]
        public double AimDifficulty { get; set; }

        /// <summary>
        /// The difficulty corresponding to the speed skill.
        /// </summary>
        [JsonProperty("speed_difficulty")]
        public double SpeedDifficulty { get; set; }

        /// <summary>
        /// The number of clickable objects weighted by difficulty.
        /// Related to <see cref="SpeedDifficulty"/>
        /// </summary>
        [JsonProperty("speed_note_count")]
        public double SpeedNoteCount { get; set; }

        /// <summary>
        /// The difficulty corresponding to the flashlight skill.
        /// </summary>
        [JsonProperty("flashlight_difficulty")]
        public double FlashlightDifficulty { get; set; }

        /// <summary>
        /// Describes how much of <see cref="AimDifficulty"/> is contributed to by hitcircles or sliders.
        /// A value closer to 1.0 indicates most of <see cref="AimDifficulty"/> is contributed by hitcircles.
        /// A value closer to 0.0 indicates most of <see cref="AimDifficulty"/> is contributed by sliders.
        /// </summary>
        [JsonProperty("slider_factor")]
        public double SliderFactor { get; set; }

        [JsonProperty("aim_difficult_strain_count")]
        public double AimDifficultStrainCount { get; set; }

        [JsonProperty("speed_difficult_strain_count")]
        public double SpeedDifficultStrainCount { get; set; }

        /// <summary>
        /// The perceived approach rate inclusive of rate-adjusting mods (DT/HT/etc).
        /// </summary>
        /// <remarks>
        /// Rate-adjusting mods don't directly affect the approach rate difficulty value, but have a perceived effect as a result of adjusting audio timing.
        /// </remarks>
        [JsonProperty("approach_rate")]
        public double ApproachRate { get; set; }

        /// <summary>
        /// The perceived overall difficulty inclusive of rate-adjusting mods (DT/HT/etc).
        /// </summary>
        /// <remarks>
        /// Rate-adjusting mods don't directly affect the overall difficulty value, but have a perceived effect as a result of adjusting audio timing.
        /// </remarks>
        [JsonProperty("overall_difficulty")]
        public double OverallDifficulty { get; set; }

        /// <summary>
        /// The beatmap's drain rate. This doesn't scale with rate-adjusting mods.
        /// </summary>
        public double DrainRate { get; set; }

        /// <summary>
        /// The number of hitcircles in the beatmap.
        /// </summary>
        public int HitCircleCount { get; set; }

        /// <summary>
        /// The number of sliders in the beatmap.
        /// </summary>
        public int SliderCount { get; set; }

        /// <summary>
        /// The number of spinners in the beatmap.
        /// </summary>
        public int SpinnerCount { get; set; }

        public override IEnumerable<(int attributeId, object value)> ToDatabaseAttributes()
        {
            foreach (var v in base.ToDatabaseAttributes())
                yield return v;

            yield return (ATTRIB_ID_AIM, AimDifficulty);
            yield return (ATTRIB_ID_SPEED, SpeedDifficulty);
            yield return (ATTRIB_ID_OVERALL_DIFFICULTY, OverallDifficulty);
            yield return (ATTRIB_ID_APPROACH_RATE, ApproachRate);
            yield return (ATTRIB_ID_DIFFICULTY, StarRating);

            if (ShouldSerializeFlashlightDifficulty())
                yield return (ATTRIB_ID_FLASHLIGHT, FlashlightDifficulty);

            yield return (ATTRIB_ID_SLIDER_FACTOR, SliderFactor);

            yield return (ATTRIB_ID_AIM_DIFFICULT_STRAIN_COUNT, AimDifficultStrainCount);
            yield return (ATTRIB_ID_SPEED_DIFFICULT_STRAIN_COUNT, SpeedDifficultStrainCount);
            yield return (ATTRIB_ID_SPEED_NOTE_COUNT, SpeedNoteCount);
        }

        public override void FromDatabaseAttributes(IReadOnlyDictionary<int, double> values, IBeatmapOnlineInfo onlineInfo)
        {
            base.FromDatabaseAttributes(values, onlineInfo);

            AimDifficulty = values[ATTRIB_ID_AIM];
            SpeedDifficulty = values[ATTRIB_ID_SPEED];
            OverallDifficulty = values[ATTRIB_ID_OVERALL_DIFFICULTY];
            ApproachRate = values[ATTRIB_ID_APPROACH_RATE];
            StarRating = values[ATTRIB_ID_DIFFICULTY];
            FlashlightDifficulty = values.GetValueOrDefault(ATTRIB_ID_FLASHLIGHT);
            SliderFactor = values[ATTRIB_ID_SLIDER_FACTOR];
            AimDifficultStrainCount = values[ATTRIB_ID_AIM_DIFFICULT_STRAIN_COUNT];
            SpeedDifficultStrainCount = values[ATTRIB_ID_SPEED_DIFFICULT_STRAIN_COUNT];
            SpeedNoteCount = values[ATTRIB_ID_SPEED_NOTE_COUNT];
            DrainRate = onlineInfo.DrainRate;
            HitCircleCount = onlineInfo.CircleCount;
            SliderCount = onlineInfo.SliderCount;
            SpinnerCount = onlineInfo.SpinnerCount;
        }

        public override SkillValue[] GetSkillValues()
        {
            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(AimDifficulty);
            double aimPerformanceWithoutSliders = OsuStrainSkill.DifficultyToPerformance(AimDifficulty * SliderFactor);

            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(SpeedDifficulty);
            double flashlightPerformance = Flashlight.DifficultyToPerformance(FlashlightDifficulty);

            double totalHits = HitCircleCount + SliderCount;
            double lengthBonus = 0.95 + 0.4 * Math.Min(1.0, totalHits / 2000.0) +
                                 (totalHits > 2000 ? Math.Log10(totalHits / 2000.0) * 0.5 : 0.0);

            double highARbonus = Mods.Any(h => h is OsuModRelax) ? 0 : Math.Max(0, 0.3 * (ApproachRate - 10.33)) * lengthBonus;
            double lowARbonus = Mods.Any(h => h is OsuModRelax) ? 0 : Math.Max(0, 0.05 * (8.0 - ApproachRate)) * lengthBonus;

            bool isBlinds = Mods.Any(m => m is OsuModBlinds);
            double blindsBonusAim = isBlinds ? 0.3 + (totalHits * 0.0016 * (1 - 0.003 * DrainRate * DrainRate)) : 0;
            double blindsBonusSpeed = isBlinds ? 0.16 : 0;

            bool isTraceable = Mods.Any(m => m is OsuModTraceable);
            double HDBonus = (Mods.Any(m => m is ModHidden || m is OsuModTraceable) && !isBlinds) ? 0.04 * (12.0 - ApproachRate) : 0;

            double readingHighARPerformance = (aimPerformance + speedPerformance) * highARbonus;
            double readingLowARPerformance = aimPerformance * lowARbonus;
            double readingHiddenPerformance = (aimPerformance + speedPerformance) * HDBonus;
            double memoryPerformance = Math.Max(flashlightPerformance, aimPerformance * blindsBonusAim + speedPerformance * blindsBonusSpeed);

            // Rescale mechanical performance values to make values more clear
            double mechanicalPerformance = aimPerformance + speedPerformance;
            double mechanicalPerformanceSqr = sqr(aimPerformance) + sqr(speedPerformance);

            aimPerformance = mechanicalPerformance * sqr(aimPerformance) / mechanicalPerformanceSqr;
            aimPerformanceWithoutSliders = mechanicalPerformance * sqr(aimPerformanceWithoutSliders) / mechanicalPerformanceSqr;
            speedPerformance = mechanicalPerformance * sqr(speedPerformance) / mechanicalPerformanceSqr;

            return [
                new SkillValue { Value = aimPerformance - aimPerformanceWithoutSliders, SkillName = "Slider Aim" },
                new SkillValue { Value = aimPerformanceWithoutSliders, SkillName = "Aim" },
                new SkillValue { Value = speedPerformance, SkillName = "Speed" },
                new SkillValue { Value = readingHighARPerformance, SkillName = "High AR" },
                new SkillValue { Value = readingLowARPerformance, SkillName = "Low AR" },
                new SkillValue { Value = readingHiddenPerformance, SkillName = isTraceable ? "Traceable" : "Hidden" },
                new SkillValue { Value = memoryPerformance, SkillName = isBlinds ? "Blinds" : "Flashlight" }
            ];
        }

        private static double sqr(double a) => a * a;

        #region Newtonsoft.Json implicit ShouldSerialize() methods

        // The properties in this region are used implicitly by Newtonsoft.Json to not serialise certain fields in some cases.
        // They rely on being named exactly the same as the corresponding fields (casing included) and as such should NOT be renamed
        // unless the fields are also renamed.

        [UsedImplicitly]
        public bool ShouldSerializeFlashlightDifficulty() => Mods.Any(m => m is ModFlashlight);

        #endregion
    }
}
