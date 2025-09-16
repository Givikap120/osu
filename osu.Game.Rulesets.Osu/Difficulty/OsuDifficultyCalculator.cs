// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Skills;
using osu.Game.Rulesets.Osu.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyCalculator : DifficultyCalculator
    {
        private const double star_rating_multiplier = 0.0265;

        private const double relax_multiplier = 0.87;
        private const double touch_device_multiplier = 0.83;

        public override int Version => 20250306;

        public OsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        // Increasing this multiplier buffs versatile aim+flow maps
        public static double AimVersatilityBonus = 0.08;

        public static double CalculateRateAdjustedApproachRate(double approachRate, double clockRate)
        {
            double preempt = IBeatmapDifficultyInfo.DifficultyRange(approachRate, OsuHitObject.PREEMPT_MAX, OsuHitObject.PREEMPT_MID, OsuHitObject.PREEMPT_MIN) / clockRate;
            return IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, OsuHitObject.PREEMPT_MAX, OsuHitObject.PREEMPT_MID, OsuHitObject.PREEMPT_MIN);
        }

        public static double CalculateRateAdjustedOverallDifficulty(double overallDifficulty, double clockRate)
        {
            HitWindows hitWindows = new OsuHitWindows();
            hitWindows.SetDifficulty(overallDifficulty);

            double hitWindowGreat = hitWindows.WindowFor(HitResult.Great) / clockRate;

            return (79.5 - hitWindowGreat) / 6;
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
        {
            if (beatmap.HitObjects.Count == 0)
                return new OsuDifficultyAttributes { Mods = mods };

            var aim = skills.OfType<TotalAim>().Single(a => a.IncludeSliders);
            var aimWithoutSliders = skills.OfType<TotalAim>().Single(a => !a.IncludeSliders);
            var speed = skills.OfType<Speed>().Single();
            var flashlight = skills.OfType<Flashlight>().SingleOrDefault();
            var reading = skills.OfType<Reading>().Single();

            double speedNotes = speed.RelevantNoteCount();

            double aimDifficultStrainCount = aim.CountTopWeightedStrains();
            double speedDifficultStrainCount = speed.CountTopWeightedStrains();
            double readingDifficultNoteCount = reading.CountTopWeightedNotes();

            double aimNoSlidersTopWeightedSliderCount = aimWithoutSliders.CountTopWeightedSliders();
            double aimNoSlidersDifficultStrainCount = aimWithoutSliders.CountTopWeightedStrains();

            double aimTopWeightedSliderFactor = aimNoSlidersTopWeightedSliderCount / Math.Max(1, aimNoSlidersDifficultStrainCount - aimNoSlidersTopWeightedSliderCount);

            double speedTopWeightedSliderCount = speed.CountTopWeightedSliders();
            double speedTopWeightedSliderFactor = speedTopWeightedSliderCount / Math.Max(1, speedDifficultStrainCount - speedTopWeightedSliderCount);

            double difficultSliders = aim.GetDifficultSliders();

            double overallDifficulty = CalculateRateAdjustedOverallDifficulty(beatmap.Difficulty.OverallDifficulty, clockRate);

            int hitCircleCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);

            int totalHits = beatmap.HitObjects.Count;

            double drainRate = beatmap.Difficulty.DrainRate;

            double aimDifficultyValue = aim.DifficultyValue();
            double aimNoSlidersDifficultyValue = aimWithoutSliders.DifficultyValue();
            double snapAimDifficultyValue = skills.OfType<SnapAim>().Single().DifficultyValue();
            double flowAimDifficultyValue = skills.OfType<FlowAim>().Single().DifficultyValue();
            double speedDifficultyValue = speed.DifficultyValue();
            double readingDifficultyValue = reading.DifficultyValue();

            double sliderFactor = aimDifficultyValue > 0 ? OsuRatingCalculator.CalculateDifficultyRating(aimNoSlidersDifficultyValue) / OsuRatingCalculator.CalculateDifficultyRating(aimDifficultyValue) : 1;

            var osuRatingCalculator = new OsuRatingCalculator(mods, totalHits, overallDifficulty);

            double aimRating = osuRatingCalculator.ComputeAimRating(aimDifficultyValue);
            double speedRating = osuRatingCalculator.ComputeSpeedRating(speedDifficultyValue);
            double readingRating = osuRatingCalculator.ComputeReadingRating(readingDifficultyValue);

            double flashlightRating = 0.0;

            if (flashlight is not null)
                flashlightRating = osuRatingCalculator.ComputeFlashlightRating(flashlight.DifficultyValue());

            double sliderNestedScorePerObject = LegacyScoreUtils.CalculateNestedScorePerObject(beatmap, totalHits);
            double legacyScoreBaseMultiplier = LegacyScoreUtils.CalculateDifficultyPeppyStars(beatmap);

            var simulator = new OsuLegacyScoreSimulator();
            var scoreAttributes = simulator.Simulate(WorkingBeatmap, beatmap);

            double baseAimPerformance = OsuStrainSkill.DifficultyToPerformance(aimRating);
            double baseSpeedPerformance = OsuStrainSkill.DifficultyToPerformance(speedRating);
            double baseReadingPerformance = Reading.DifficultyToPerformance(readingRating);
            double baseFlashlightPerformance = Flashlight.DifficultyToPerformance(flashlightRating);

            double basePerformance =
                Math.Pow(
                    Math.Pow(SumMechanicalDifficulty(baseAimPerformance, baseSpeedPerformance), 1.1) +
                    Math.Pow(baseFlashlightPerformance, 1.1) +
                    Math.Pow(baseReadingPerformance, 1.1), 1.0 / 1.1
                );

            double aimRelevantObjectCount = aim.CountRelevantObjects();
            double aimNoSlidersRelevantObjectCount = aimWithoutSliders.CountRelevantObjects();
            double speedRelevantObjectCount = speed.CountRelevantObjects();

            const double aim_length_bonus_multiplier = 0.91257;
            const double speed_length_bonus_multiplier = 0.998;

            double aimLengthBonus = 1.0 + Math.Min(0.8, aimRelevantObjectCount / 300.0) +
                                    (aimRelevantObjectCount > 240.0 ? 1.5 * Math.Log10(aimRelevantObjectCount / 240.0) : 0);
            aimRating *= Math.Cbrt(aimLengthBonus * aim_length_bonus_multiplier);

            double aimNoSlidersLengthBonus = 1.0 + Math.Min(0.8, aimNoSlidersRelevantObjectCount / 300.0) +
                                             (aimNoSlidersRelevantObjectCount > 240.0 ? 1.5 * Math.Log10(aimNoSlidersRelevantObjectCount / 240.0) : 0);
            aimRatingNoSliders *= Math.Cbrt(aimNoSlidersLengthBonus * aim_length_bonus_multiplier);

            double speedLengthBonus = 1.0 + Math.Min(0.2, speedRelevantObjectCount / 750.0) +
                                      (speedRelevantObjectCount > 200 ? 0.4 * Math.Log10(speedRelevantObjectCount / 200.0) : 0.0);
            speedRating *= Math.Cbrt(speedLengthBonus * speed_length_bonus_multiplier);

            double sliderNestedScorePerObject = LegacyScoreUtils.CalculateNestedScorePerObject(beatmap, totalHits);
            double legacyScoreBaseMultiplier = LegacyScoreUtils.CalculateDifficultyPeppyStars(beatmap);

            var simulator = new OsuLegacyScoreSimulator();
            var scoreAttributes = simulator.Simulate(WorkingBeatmap, beatmap);

            OsuDifficultyAttributes attributes = new OsuDifficultyAttributes
            {
                StarRating = starRating,
                Mods = mods,
                AimDifficulty = aimRating,
                AimDifficultSliderCount = difficultSliders,
                SpeedDifficulty = speedRating,
                SpeedNoteCount = speedNotes,
                FlashlightDifficulty = flashlightRating,
                ReadingDifficulty = readingRating,
                SliderFactor = sliderFactor,
                AimDifficultStrainCount = aimDifficultStrainCount,
                SpeedDifficultStrainCount = speedDifficultStrainCount,
                ReadingDifficultNoteCount = readingDifficultNoteCount,
                AimTopWeightedSliderFactor = aimTopWeightedSliderFactor,
                SpeedTopWeightedSliderFactor = speedTopWeightedSliderFactor,
                DrainRate = drainRate,
                MaxCombo = beatmap.GetMaxCombo(),
                HitCircleCount = hitCircleCount,
                SliderCount = sliderCount,
                SpinnerCount = spinnerCount,
                NestedScorePerObject = sliderNestedScorePerObject,
                LegacyScoreBaseMultiplier = legacyScoreBaseMultiplier,
                MaximumLegacyComboScore = scoreAttributes.ComboScore,
                SnapAimDifficulty = snapAimRating,
                FlowAimDifficulty = flowAimRating
            };

            return attributes;
        }

        private double calculateStarRating(double basePerformance)
        {
            if (basePerformance <= 0.00001)
                return 0;

            return Math.Cbrt(OsuPerformanceCalculator.PERFORMANCE_BASE_MULTIPLIER) * star_rating_multiplier * (Math.Cbrt(100000 / Math.Pow(2, 1 / 1.1) * basePerformance) + 4);
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
        {
            List<DifficultyHitObject> objects = new List<DifficultyHitObject>();

            // The first jump is formed by the first two hitobjects of the map.
            // If the map has less than two OsuHitObjects, the enumerator will not return anything.
            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                objects.Add(new OsuDifficultyHitObject(beatmap.HitObjects[i], beatmap.HitObjects[i - 1], clockRate, objects, objects.Count));
            }

            return objects;
        }

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods, double clockRate)
        {
            var skills = new List<Skill>
            {
                new TotalAim(mods, true),
                new TotalAim(mods, false),
                new Speed(mods),
                new SnapAim(mods),
                new FlowAim(mods),
                new Reading(beatmap, mods, clockRate)
            };

            if (mods.Any(h => h is OsuModFlashlight))
                skills.Add(new Flashlight(mods));

            return skills.ToArray();
        }

        protected override Mod[] DifficultyAdjustmentMods => new Mod[]
        {
            new OsuModTouchDevice(),
            new OsuModDoubleTime(),
            new OsuModHalfTime(),
            new OsuModEasy(),
            new OsuModHardRock(),
            new OsuModFlashlight(),
            new OsuModHidden(),
        };
    }
}
