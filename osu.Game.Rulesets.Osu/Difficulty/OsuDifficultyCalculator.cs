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
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Difficulty.Utils;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyCalculator : DifficultyCalculator
    {
        private const double performance_base_multiplier = 1.14; // This is being adjusted to keep the final pp value scaled around what it used to be when changing things.
        private const double difficulty_multiplier = 0.0675;
        private const double star_rating_multiplier = 0.0265;

        public override int Version => 20231104;

        // CONFIG
        // default pp+ is all values are set to false

        // Fix category
        public const bool ENABLE_FIRST_SPEED_NOTE_FIX = true; // Makes low SR maps to be calculated correctly
        public const bool ENABLE_LAZER_SUPPORT = true; // Slideracc and no misscount estimation on lazer scores if Effective Miss Count enabled
        public const bool ENABLE_EFFECTIVE_MISS_COUNT = true; // Punishing sliderbreaks as misses. Warning - always set this to true when using CSR
        public const bool DISABLE_300BPM_SPEED_LIMIT = true; // Buffs Ivaxa Deceit DT in 2 times

        // Balance category
        public const bool ENABLE_CSR = true; // Combo Scaling Removal
        public const bool ENABLE_LENGTH_BONUS = true; // Make the value more live-like, indirectly buffing all stream maps

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

            double aimRating = calculateDifficultyRating(skills[0].DifficultyValue());
            double jumpAimRating = calculateDifficultyRating(skills[2].DifficultyValue());
            double flowAimRating = calculateDifficultyRating(skills[3].DifficultyValue());
            double precisionRating = calculateDifficultyRating(Math.Max(0, skills[0].DifficultyValue() - skills[1].DifficultyValue()));

            double speedRating = calculateDifficultyRating(skills[4].DifficultyValue());
            double staminaRating = calculateDifficultyRating(skills[5].DifficultyValue());

            double accuracyRating = skills[6].DifficultyValue();

            double starRating = Math.Pow(Math.Pow(aimRating, 3) + Math.Pow(Math.Max(speedRating, staminaRating), 3), 1 / 3.0) * 1.6;

            if (mods.Any(m => m is OsuModTouchDevice))
            {
                aimRating = Math.Pow(aimRating, 0.8);
            }

            if (mods.Any(h => h is OsuModRelax))
            {
                aimRating *= 0.9;
                speedRating = 0.0;
            }
            int maxCombo = beatmap.GetMaxCombo();

            int hitCircleCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);
            int totalHits = beatmap.HitObjects.Count;

            double sliderNestedScorePerObject = LegacyScoreUtils.CalculateNestedScorePerObject(beatmap, totalHits);
            double legacyScoreBaseMultiplier = LegacyScoreUtils.CalculateDifficultyPeppyStars(beatmap);

            var simulator = new OsuLegacyScoreSimulator();
            var scoreAttributes = simulator.Simulate(WorkingBeatmap, beatmap);

            OsuDifficultyAttributes attributes = new OsuDifficultyAttributes
            {
                StarRating = starRating,
                Mods = mods,
                AimDifficulty = aimRating,
                AimDifficultyStrainsCount = ((OsuStrainSkill)skills[0]).CountDifficultStrains(),
                JumpAimDifficulty = jumpAimRating,
                JumpAimDifficultyStrainsCount = ((OsuStrainSkill)skills[2]).CountDifficultStrains(),
                FlowAimDifficulty = flowAimRating,
                FlowAimDifficultyStrainsCount = ((OsuStrainSkill)skills[3]).CountDifficultStrains(),
                PrecisionDifficulty = precisionRating,
                SpeedDifficulty = speedRating,
                SpeedDifficultyStrainsCount = ((OsuStrainSkill)skills[4]).CountDifficultStrains(),
                StaminaDifficulty = staminaRating,
                StaminaDifficultyStrainsCount = ((OsuStrainSkill)skills[5]).CountDifficultStrains(),
                AccuracyDifficulty = accuracyRating,
                MaxCombo = maxCombo,
                HitCircleCount = hitCircleCount,
                SliderCount = sliderCount,
                SpinnerCount = spinnerCount,
                NestedScorePerObject = sliderNestedScorePerObject,
                LegacyScoreBaseMultiplier = legacyScoreBaseMultiplier,
                MaximumLegacyComboScore = scoreAttributes.ComboScore
            };

            return attributes;
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
                new RawAim(mods),
                new JumpAim(mods),
                new FlowAim(mods),
                new Speed(mods),
                new Stamina(mods),
                new RhythmComplexity(mods)
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
