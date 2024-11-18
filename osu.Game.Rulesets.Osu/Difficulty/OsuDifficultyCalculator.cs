// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

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
using osu.Game.Rulesets.Osu.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Difficulty
{
    public class OsuDifficultyCalculator : DifficultyCalculator
    {
        public const double DIFFICULTY_MULTIPLIER = 0.0675;
        public const double SUM_POWER = 1.1;
        public const double FL_SUM_POWER = 1.5;
        public override int Version => 20241007;

        public OsuDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills, double clockRate)
        {
            if (beatmap.HitObjects.Count == 0)
                return new OsuDifficultyAttributes { Mods = mods };

            double aimRating = Math.Sqrt(skills[0].DifficultyValue()) * DIFFICULTY_MULTIPLIER;
            double aimRatingNoSliders = Math.Sqrt(skills[1].DifficultyValue()) * DIFFICULTY_MULTIPLIER;
            double speedRating = Math.Sqrt(skills.OfType<Speed>().First().DifficultyValue()) * DIFFICULTY_MULTIPLIER;
            double speedNotes = skills.OfType<Speed>().First().RelevantNoteCount();

            double flashlightRating = Math.Sqrt(skills.OfType<Flashlight>().First().DifficultyValue()) * DIFFICULTY_MULTIPLIER;

            double readingLowARRating = Math.Sqrt(skills.OfType<ReadingLowAR>().First().DifficultyValue()) * DIFFICULTY_MULTIPLIER;
            double readingHighARRating = Math.Sqrt(skills.OfType<ReadingHighAR>().First().DifficultyValue()) * DIFFICULTY_MULTIPLIER;

            double hiddenRating = 0;

            double sliderFactor = aimRating > 0 ? aimRatingNoSliders / aimRating : 1;

            double hiddenDifficultyStrainCount = 0;
            double readingHiddenPerformance = 0.0;
            if (mods.Any(h => h is OsuModHidden))
            {
                hiddenRating = Math.Sqrt(skills[6].DifficultyValue()) * DIFFICULTY_MULTIPLIER;
                readingHiddenPerformance = ReadingHidden.DifficultyToPerformance(hiddenRating);
                hiddenDifficultyStrainCount = skills.OfType<ReadingHidden>().First().CountTopWeightedStrains();
            }

            double aimDifficultyStrainCount = skills[0].CountTopWeightedStrains();
            double speedDifficultyStrainCount = skills.OfType<Speed>().First().CountTopWeightedStrains();
            double lowArDifficultyStrainCount = skills.OfType<ReadingLowAR>().First().CountTopWeightedStrains();

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

            double aimPerformance = OsuStrainSkill.DifficultyToPerformance(aimRating);
            double speedPerformance = OsuStrainSkill.DifficultyToPerformance(speedRating);

            // Cognition
            double readingLowARPerformance = ReadingLowAR.DifficultyToPerformance(readingLowARRating);
            double readingHighARPerformance = OsuStrainSkill.DifficultyToPerformance(readingHighARRating);
            double readingARPerformance = Math.Pow(Math.Pow(readingLowARPerformance, SUM_POWER) + Math.Pow(readingHighARPerformance, SUM_POWER), 1.0 / SUM_POWER);

            double potentialFlashlightPerformance = Flashlight.DifficultyToPerformance(flashlightRating);
            double flashlightPerformance = mods.Any(h => h is OsuModFlashlight) ? potentialFlashlightPerformance : 0;

            double baseFlashlightARPerformance = Math.Pow(Math.Pow(flashlightPerformance, FL_SUM_POWER) + Math.Pow(readingARPerformance, FL_SUM_POWER), 1.0 / FL_SUM_POWER);

            double preempt = IBeatmapDifficultyInfo.DifficultyRange(beatmap.Difficulty.ApproachRate, 1800, 1200, 450) / clockRate;

            double drainRate = beatmap.Difficulty.DrainRate;

            int hitCirclesCount = beatmap.HitObjects.Count(h => h is HitCircle);
            int sliderCount = beatmap.HitObjects.Count(h => h is Slider);
            int spinnerCount = beatmap.HitObjects.Count(h => h is Spinner);

            double cognitionPerformance = baseFlashlightARPerformance + readingHiddenPerformance;
            double mechanicalPerformance = Math.Pow(Math.Pow(aimPerformance, SUM_POWER) + Math.Pow(speedPerformance, SUM_POWER), 1.0 / SUM_POWER);

            // Limit cognition by full memorisation difficulty, what is assumed to be mechanicalPerformance + flashlightPerformance
            cognitionPerformance = OsuPerformanceCalculator.AdjustCognitionPerformance(cognitionPerformance, mechanicalPerformance, potentialFlashlightPerformance);

            double basePerformance = mechanicalPerformance + cognitionPerformance;

            double starRating = basePerformance > 0.00001
                ? Math.Cbrt(OsuPerformanceCalculator.PERFORMANCE_BASE_MULTIPLIER) * 0.027 * (Math.Cbrt(100000 / Math.Pow(2, 1 / 1.1) * basePerformance) + 4)
                : 0;

            HitWindows hitWindows = new OsuHitWindows();
            hitWindows.SetDifficulty(beatmap.Difficulty.OverallDifficulty);

            double hitWindowGreat = hitWindows.WindowFor(HitResult.Great) / clockRate;

            OsuDifficultyAttributes attributes = new OsuDifficultyAttributes
            {
                StarRating = starRating,
                Mods = mods,
                AimDifficulty = aimRating,
                SpeedDifficulty = speedRating,
                SpeedNoteCount = speedNotes,
                ReadingDifficultyLowAR = readingLowARRating,
                ReadingDifficultyHighAR = readingHighARRating,
                HiddenDifficulty = hiddenRating,
                FlashlightDifficulty = flashlightRating,
                SliderFactor = sliderFactor,
                AimDifficultStrainCount = aimDifficultyStrainCount,
                SpeedDifficultStrainCount = speedDifficultyStrainCount,
                LowArDifficultStrainCount = lowArDifficultyStrainCount,
                HiddenDifficultStrainCount = hiddenDifficultyStrainCount,
                ApproachRate = IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, 1800, 1200, 450),
                OverallDifficulty = (80 - hitWindowGreat) / 6,
                DrainRate = drainRate,
                MaxCombo = beatmap.GetMaxCombo(),
                HitCircleCount = hitCirclesCount,
                SliderCount = sliderCount,
                SpinnerCount = spinnerCount
            };

            return attributes;
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, double clockRate)
        {
            List<DifficultyHitObject> objects = new List<DifficultyHitObject>();

            // The first jump is formed by the first two hitobjects of the map.
            // If the map has less than two OsuHitObjects, the enumerator will not return anything.
            for (int i = 1; i < beatmap.HitObjects.Count; i++)
            {
                var lastLast = i > 1 ? beatmap.HitObjects[i - 2] : null;
                objects.Add(new OsuDifficultyHitObject(beatmap.HitObjects[i], beatmap.HitObjects[i - 1], lastLast, clockRate, objects, objects.Count));
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
            new MultiMod(new OsuModFlashlight(), new OsuModHidden())
        };
    }
}
