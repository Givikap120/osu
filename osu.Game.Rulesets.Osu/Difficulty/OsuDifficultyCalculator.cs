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
        public const double PERFORMANCE_BASE_MULTIPLIER = 1.104;
        public const double SUM_POWER = 1.1;
        public const double FL_SUM_POWER = 1.5;

        private const double star_rating_multiplier = 0.0265;

        public override int Version => 20250306;

        public OsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

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

            // Skills
            var aim = skills.OfType<Aim>().First(a => a.IncludeSliders);
            var aimWithoutSliders = skills.OfType<Aim>().First(a => !a.IncludeSliders);
            var speed = skills.OfType<Speed>().Single();
            var flashlight = skills.OfType<Flashlight>().Single();
            var readingLowAr = skills.OfType<ReadingLowAR>().First();
            var readingHighAr = skills.OfType<ReadingHighAR>().First();
            var hidden = skills.OfType<ReadingHidden>().FirstOrDefault();

            // Map data
            double overallDifficulty = CalculateRateAdjustedOverallDifficulty(beatmap.Difficulty.OverallDifficulty, clockRate);

            int hitCircleCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);

            int totalHits = beatmap.HitObjects.Count;

            double drainRate = beatmap.Difficulty.DrainRate;

            // Ratings
            var osuRatingCalculator = new OsuRatingCalculator(mods, totalHits, overallDifficulty);

            double aimRating = osuRatingCalculator.ComputeAimRating(aim.DifficultyValue());
            double aimRatingNoSliders = osuRatingCalculator.ComputeAimRating(aimWithoutSliders.DifficultyValue());
            double speedRating = osuRatingCalculator.ComputeSpeedRating(speed.DifficultyValue());
            double flashlightRating = osuRatingCalculator.ComputeFlashlightRating(flashlight.DifficultyValue());
            double readingLowARRating = osuRatingCalculator.ComputeReadingLowArRating(readingLowAr.DifficultyValue());
            double readingHighARRating = osuRatingCalculator.ComputeReadingHighArRating(readingHighAr.DifficultyValue(), aimRating, speedRating);
            double hiddenRating = hidden == null ? 0 : osuRatingCalculator.ComputeReadingHiddenRating(hidden.DifficultyValue());

            // Top weighted strains
            double aimDifficultStrainCount = aim.CountTopWeightedStrains();
            double aimNoSlidersDifficultStrainCount = aimWithoutSliders.CountTopWeightedStrains();
            double speedDifficultStrainCount = speed.CountTopWeightedStrains();
            double lowArDifficultStrainCount = skills.OfType<ReadingLowAR>().First().CountTopWeightedStrains();
            double hiddenDifficultStrainCount = 0;

            // Top weighted slider factors
            double aimNoSlidersTopWeightedSliderCount = aimWithoutSliders.CountTopWeightedSliders();
            double aimTopWeightedSliderFactor = aimNoSlidersTopWeightedSliderCount / Math.Max(1, aimNoSlidersDifficultStrainCount - aimNoSlidersTopWeightedSliderCount);

            double speedTopWeightedSliderCount = speed.CountTopWeightedSliders();
            double speedTopWeightedSliderFactor = speedTopWeightedSliderCount / Math.Max(1, speedDifficultStrainCount - speedTopWeightedSliderCount);

            double lowArTopWeightedSliderCount = readingLowAr.CountTopWeightedSliders();
            double lowArTopWeightedSliderFactor = lowArTopWeightedSliderCount / Math.Max(1, lowArDifficultStrainCount - lowArTopWeightedSliderCount);

            double hiddenTopWeightedSliderFactor = 0;

            // Other
            double sliderFactor = aimRating > 0 ? aimRatingNoSliders / aimRating : 1;
            double difficultSliders = aim.GetDifficultSliders();
            double speedNotes = speed.RelevantNoteCount();

            if (hidden != null)
            {
                hiddenDifficultStrainCount = hidden.CountTopWeightedStrains();
                double hiddenTopWeightedSliderCount = hidden.CountTopWeightedSliders();
                hiddenTopWeightedSliderFactor = hiddenTopWeightedSliderCount / Math.Max(1, hiddenDifficultStrainCount - hiddenTopWeightedSliderCount);
            }

            // Star Rating
            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(aimRating);
            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(speedRating);

            // Cognition
            double readingLowARPerformance = ReadingLowAR.DifficultyToPerformance(readingLowARRating);
            double readingHighARPerformance = OsuStrainSkill.DifficultyToPerformance(readingHighARRating);
            double readingARPerformance = Math.Pow(Math.Pow(readingLowARPerformance, SUM_POWER) + Math.Pow(readingHighARPerformance, SUM_POWER), 1.0 / SUM_POWER);

            double potentialFlashlightPerformance = Flashlight.DifficultyToPerformance(flashlightRating);
            double flashlightPerformance = mods.Any(h => h is OsuModFlashlight) ? potentialFlashlightPerformance : 0;

            double baseFlashlightARPerformance = Math.Pow(Math.Pow(flashlightPerformance, FL_SUM_POWER) + Math.Pow(readingARPerformance, FL_SUM_POWER), 1.0 / FL_SUM_POWER);

            double readingHiddenPerformance = ReadingHidden.DifficultyToPerformance(hiddenRating);

            double cognitionPerformance = baseFlashlightARPerformance + readingHiddenPerformance;
            double mechanicalPerformance = Math.Pow(Math.Pow(aimPerformance, SUM_POWER) + Math.Pow(speedPerformance, SUM_POWER), 1.0 / SUM_POWER);

            // Limit cognition by full memorisation difficulty, what is assumed to be mechanicalPerformance + flashlightPerformance
            cognitionPerformance = OsuPerformanceCalculator.AdjustCognitionPerformance(cognitionPerformance, mechanicalPerformance, potentialFlashlightPerformance);

            double basePerformance = mechanicalPerformance + cognitionPerformance;

            double starRating = calculateStarRating(basePerformance);

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
                ReadingDifficultyLowAR = readingLowARRating,
                ReadingDifficultyHighAR = readingHighARRating,
                HiddenDifficulty = hiddenRating,
                FlashlightDifficulty = flashlightRating,
                SliderFactor = sliderFactor,
                AimDifficultStrainCount = aimDifficultStrainCount,
                SpeedDifficultStrainCount = speedDifficultStrainCount,
                LowArDifficultStrainCount = lowArDifficultStrainCount,
                HiddenDifficultStrainCount = hiddenDifficultStrainCount,
                AimTopWeightedSliderFactor = aimTopWeightedSliderFactor,
                SpeedTopWeightedSliderFactor = speedTopWeightedSliderFactor,
                DrainRate = drainRate,
                MaxCombo = beatmap.GetMaxCombo(),
                HitCircleCount = hitCircleCount,
                SliderCount = sliderCount,
                SpinnerCount = spinnerCount,
                NestedScorePerObject = sliderNestedScorePerObject,
                LegacyScoreBaseMultiplier = legacyScoreBaseMultiplier,
                MaximumLegacyComboScore = scoreAttributes.ComboScore
            };

            return attributes;
        }

        private static double calculateStarRating(double basePerformance)
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
                new Aim(mods, true),
                new Aim(mods, false),
                new Speed(mods),
                new Flashlight(mods),
                new ReadingLowAR(mods),
                new ReadingHighAR(mods),
            };

            if (mods.Any(h => h is OsuModHidden))
                skills.Add(new ReadingHidden(mods));

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
