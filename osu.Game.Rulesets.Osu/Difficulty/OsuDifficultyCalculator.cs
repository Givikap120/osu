// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
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
        public const double DIFFICULTY_MULTIPLIER = 0.0675;
        public const double PERFORMANCE_BASE_MULTIPLIER = 1.104;
        public const double SUM_POWER = 1.1;
        public const double FL_SUM_POWER = 1.5;

        private const double star_rating_multiplier = 0.0265;

        public override int Version => 20250306;

        private double mechanicalDifficultyRating;

        public OsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        public static double CalculateDifficultyMultiplier(Mod[] mods, int totalHits, int spinnerCount)
        {
            double multiplier = PERFORMANCE_BASE_MULTIPLIER;

            if (mods.Any(m => m is OsuModSpunOut) && totalHits > 0)
                multiplier *= 1.0 - Math.Pow((double)spinnerCount / totalHits, 0.85);

            return multiplier;
        }

        /// <summary>
        /// Calculates a visibility bonus that is applicable to Hidden and Traceable.
        /// </summary>
        public static double CalculateVisibilityBonus(Mod[] mods, double approachRate, double visibilityFactor = 1)
        {
            // NOTE: TC's effect is only noticeable in performance calculations until lazer mods are accounted for server-side.
            bool isAlwaysPartiallyVisible = mods.OfType<OsuModHidden>().Any(m => !m.OnlyFadeApproachCircles.Value) || mods.OfType<OsuModTraceable>().Any();

            // Start from normal curve, rewarding lower AR up to AR5
            double readingBonus = 0.04 * (12.0 - Math.Max(approachRate, 5));

            readingBonus *= visibilityFactor;

            // For AR up to 0 - reduce reward for very low ARs when object is visible
            if (approachRate < 5)
                readingBonus += (isAlwaysPartiallyVisible ? 0.04 : 0.03) * (5.0 - Math.Max(approachRate, 0));

            // Starting from AR0 - cap values so they won't grow to infinity
            if (approachRate < 0)
                readingBonus += (isAlwaysPartiallyVisible ? 0.1 : 0.075) * (1 - Math.Pow(1.5, approachRate));

            return readingBonus;
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
            HitWindows hitWindows = new OsuHitWindows();
            hitWindows.SetDifficulty(beatmap.Difficulty.OverallDifficulty);

            double hitWindowGreat = hitWindows.WindowFor(HitResult.Great) / clockRate;

            double overallDifficulty = (80 - hitWindowGreat) / 6;

            int hitCircleCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);

            int totalHits = beatmap.HitObjects.Count;

            double drainRate = beatmap.Difficulty.DrainRate;

            // Ratings
            double aimRating = computeAimRating(aim.DifficultyValue(), mods, overallDifficulty);
            double aimRatingNoSliders = computeAimRating(aimWithoutSliders.DifficultyValue(), mods, overallDifficulty);
            double speedRating = computeSpeedRating(speed.DifficultyValue(), mods, overallDifficulty);
            double flashlightRating = computeFlashlightRating(flashlight.DifficultyValue(), mods, totalHits, overallDifficulty);
            double readingLowARRating = computeReadingLowArRating(readingLowAr.DifficultyValue(), overallDifficulty);
            double readingHighARRating = computeReadingHighArRating(readingHighAr.DifficultyValue(), aimRating, speedRating, overallDifficulty);
            double hiddenRating = hidden == null ? 0 : computeReadingHiddenRating(hidden.DifficultyValue(), overallDifficulty);

            if (mods.Any(m => m is OsuModTouchDevice))
            {
                aimRating = Math.Pow(aimRating, 0.8);
                readingLowARRating = Math.Pow(readingLowARRating, 0.8);
                readingHighARRating = Math.Pow(readingHighARRating, 0.9);
                hiddenRating = Math.Pow(hiddenRating, 0.8);
                flashlightRating = Math.Pow(flashlightRating, 0.8);
            }
            if (mods.Any(h => h is OsuModRelax))
            {
                aimRating *= 0.9;
                speedRating = 0.0;
                readingLowARRating *= 0.95;
                readingHighARRating *= 0.7;
                hiddenRating *= 0.7;
                flashlightRating *= 0.7;
            }
            else if (mods.Any(h => h is OsuModAutopilot))
            {
                speedRating *= 0.5;
                aimRating = 0.0;
                flashlightRating *= 0.4;
            }

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

            double multiplier = CalculateDifficultyMultiplier(mods, totalHits, spinnerCount);
            double starRating = calculateStarRating(basePerformance, multiplier);

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

        private double computeAimRating(double aimDifficultyValue, Mod[] mods, double overallDifficulty)
        {
            if (mods.Any(m => m is OsuModAutopilot))
                return 0;

            double aimRating = calculateDifficultyRating(aimDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                aimRating = Math.Pow(aimRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                aimRating *= 0.9;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                aimRating *= 1.0 - magnetisedStrength;
            }

            double ratingMultiplier = 1.0;

            // It is important to consider accuracy difficulty when scaling with accuracy.
            ratingMultiplier *= 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;

            return aimRating * Math.Cbrt(ratingMultiplier);
        }

        private double computeSpeedRating(double speedDifficultyValue, Mod[] mods, double overallDifficulty)
        {
            if (mods.Any(m => m is OsuModRelax))
                return 0;

            double speedRating = calculateDifficultyRating(speedDifficultyValue);

            if (mods.Any(m => m is OsuModAutopilot))
                speedRating *= 0.5;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                // reduce speed rating because of the speed distance scaling, with maximum reduction being 0.7x
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                speedRating *= 1.0 - magnetisedStrength * 0.3;
            }

            double ratingMultiplier = 1.0;

            ratingMultiplier *= 0.95 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 750;

            return speedRating * Math.Cbrt(ratingMultiplier);
        }

        private double computeFlashlightRating(double flashlightDifficultyValue, Mod[] mods, int totalHits, double overallDifficulty)
        {
            double flashlightRating = calculateDifficultyRating(flashlightDifficultyValue);

            if (mods.Any(m => m is OsuModTouchDevice))
                flashlightRating = Math.Pow(flashlightRating, 0.8);

            if (mods.Any(m => m is OsuModRelax))
                flashlightRating *= 0.7;
            else if (mods.Any(m => m is OsuModAutopilot))
                flashlightRating *= 0.4;

            if (mods.Any(m => m is OsuModMagnetised))
            {
                float magnetisedStrength = mods.OfType<OsuModMagnetised>().First().AttractionStrength.Value;
                flashlightRating *= 1.0 - magnetisedStrength;
            }

            double ratingMultiplier = 1.0;

            // Account for shorter maps having a higher ratio of 0 combo/100 combo flashlight radius.
            ratingMultiplier *= 0.7 + 0.1 * Math.Min(1.0, totalHits / 200.0) +
                                (totalHits > 200 ? 0.2 * Math.Min(1.0, (totalHits - 200) / 200.0) : 0.0);

            // It is important to consider accuracy difficulty when scaling with accuracy.
            ratingMultiplier *= 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;

            return flashlightRating * Math.Sqrt(ratingMultiplier);
        }

        private double computeReadingLowArRating(double readingLowArDifficultyValue, double overallDifficulty)
        {
            double lowArRating = calculateDifficultyRating(readingLowArDifficultyValue);

            double ratingMultiplier = Math.Pow(0.98 + Math.Pow(overallDifficulty, 2) / 2500, 2);

            return lowArRating * Math.Pow(ratingMultiplier, 0.25);
        }

        private double computeReadingHighArRating(double readingHighArDifficultyValue, double aimRating, double speedRating, double overallDifficulty)
        {
            double highArRating = calculateDifficultyRating(readingHighArDifficultyValue);

            // Approximate how much of high AR difficulty is aim
            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(aimRating);
            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(speedRating);

            double aimRatio = aimPerformance / (aimPerformance + speedPerformance);
            double aimRatingMultiplier = 0.98 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 2500;
            double speedRatingMultiplier = 0.95 + Math.Pow(Math.Max(0, overallDifficulty), 2) / 750;
            double ratingMultiplier = aimRatingMultiplier * aimRatio + speedRatingMultiplier * (1 - aimRatio);

            return highArRating * Math.Cbrt(ratingMultiplier);
        }

        private double computeReadingHiddenRating(double readingHiddenDifficultyValue, double overallDifficulty)
        {
            double hiddenRating = calculateDifficultyRating(readingHiddenDifficultyValue);

            double ratingMultiplier = 0.98 + Math.Pow(overallDifficulty, 2) / 2500;

            return hiddenRating * Math.Cbrt(ratingMultiplier);
        }

        private static double calculateStarRating(double basePerformance, double multiplier)
        {
            if (basePerformance <= 0.00001)
                return 0;

            return Math.Cbrt(multiplier) * star_rating_multiplier * (Math.Cbrt(100000 / Math.Pow(2, 1 / 1.1) * basePerformance) + 4);
        }

        private static double calculateDifficultyRating(double difficultyValue) => Math.Sqrt(difficultyValue) * DIFFICULTY_MULTIPLIER;
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
            new OsuModSpunOut(),
        };
    }
}
