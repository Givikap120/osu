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
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyCalculator : DifficultyCalculator
    {
        private const double performance_base_multiplier = 1.15; // This is being adjusted to keep the final pp value scaled around what it used to be when changing things.
        private const double difficulty_multiplier = 0.0675;

        public override int Version => 20250306;

        private double mechanicalDifficultyRating;

        public OsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        public static double CalculateDifficultyMultiplier(Mod[] mods, int totalHits, int spinnerCount)
        {
            double multiplier = performance_base_multiplier;

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

            var aim = skills.OfType<Aim>().Single();
            var speed = skills.OfType<Speed>().Single();

            double aimRating = calculateDifficultyRating(aim.DifficultyValue());
            double speedRating = calculateDifficultyRating(speed.DifficultyValue());
            double starRating = aimRating + speedRating + Math.Abs(aimRating - speedRating) / 2;

            double aimDifficultyStrainCount = aim.CountTopWeightedStrains();
            double speedDifficultyStrainCount = speed.CountTopWeightedStrains();

            double drainRate = beatmap.Difficulty.DrainRate;

            int hitCircleCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);

            return new OsuDifficultyAttributes
            {
                StarRating = starRating,
                Mods = mods,
                AimDifficulty = aimRating,
                SpeedDifficulty = speedRating,
                AimDifficultStrainCount = aimDifficultyStrainCount,
                SpeedDifficultStrainCount = speedDifficultyStrainCount,
                DrainRate = drainRate,
                MaxCombo = beatmap.GetMaxCombo(),
                HitCircleCount = hitCircleCount,
                SliderCount = sliderCount,
                SpinnerCount = spinnerCount
            };
        }

        private static double calculateDifficultyRating(double difficultyValue) => Math.Sqrt(difficultyValue) * difficulty_multiplier;

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
                new Aim(mods),
                new Speed(mods)
            };

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
