﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    public class RhythmComplexity : Skill
    {
        private bool isSliderAcc;

        private int hitCircleCount;
        private int accuracyObjectCount;

        private int noteIndex;
        private bool isPreviousOffbeat;
        private readonly List<int> previousDoubles = new List<int>();

        private double difficultyTotal;
        private double difficultyTotalSliderAcc;

        public RhythmComplexity(Mod[] mods) : base(mods)
        {
            isSliderAcc = OsuDifficultyCalculator.ENABLE_LAZER_SUPPORT && mods.OfType<OsuModClassic>().All(m => !m.NoSliderHeadAccuracy.Value);
        }

        public override void Process(DifficultyHitObject current)
        {
            var osuCurrent = (OsuDifficultyHitObject)current;

            if (current.BaseObject is HitCircle)
            {
                double rhythmBonus = calculateRhythmBonus(osuCurrent);

                difficultyTotal += rhythmBonus;
                difficultyTotalSliderAcc += rhythmBonus;

                hitCircleCount++;
                accuracyObjectCount++;
            }
            else if (isSliderAcc && current.BaseObject is Slider)
            {
                difficultyTotalSliderAcc += calculateRhythmBonus(osuCurrent);

                accuracyObjectCount++;
            }
            else
                isPreviousOffbeat = false;

            noteIndex++;
        }

        public override double DifficultyValue()
        {
            return Math.Max(calculateDifficultyValueFor(difficultyTotal, hitCircleCount), calculateDifficultyValueFor(difficultyTotalSliderAcc, accuracyObjectCount));
        }

        private double calculateDifficultyValueFor(double difficulty, int objectCount)
        {
            if (objectCount == 0)
                return 1;

            double lengthRequirement = Math.Tanh(objectCount / 50.0);
            return 1 + difficulty / objectCount * lengthRequirement;
        }

        private double calculateRhythmBonus(OsuDifficultyHitObject current)
        {
            double rhythmBonus = 0.05 * current.Flow;

            if (current.Index == 0)
                return rhythmBonus;

            OsuDifficultyHitObject previous = (OsuDifficultyHitObject)current.Previous(0);

            if (previous.BaseObject is HitCircle)
                rhythmBonus += calculateCircleToCircleRhythmBonus(current);
            else if (previous.BaseObject is Slider)
                rhythmBonus += calculateSliderToCircleRhythmBonus(current);
            else if (previous.BaseObject is Spinner)
                isPreviousOffbeat = false;

            return rhythmBonus;
        }

        private double calculateCircleToCircleRhythmBonus(OsuDifficultyHitObject current)
        {
            var previous = (OsuDifficultyHitObject)current.Previous(0);
            double rhythmBonus = 0;

            if (isPreviousOffbeat && Utils.IsRatioEqualGreater(1.5, current.GapTime, previous.GapTime))
            {
                rhythmBonus = 5; // Doubles, Quads etc.
                foreach (int previousDouble in previousDoubles.Skip(Math.Max(0, previousDoubles.Count - 10)))
                {
                    if (previousDouble > 0) // -1 is used to mark 1/3s
                        rhythmBonus *= 1 - 0.5 * Math.Pow(0.9, noteIndex - previousDouble); // Reduce the value of repeated doubles.
                    else
                        rhythmBonus = 5;
                }

                previousDoubles.Add(noteIndex);
            }
            else if (Utils.IsRatioEqual(0.667, current.GapTime, previous.GapTime))
            {
                rhythmBonus = 4 + 8 * current.Flow; // Transition to 1/3s
                if (current.Flow > 0.8)
                    previousDoubles.Add(-1);
            }
            else if (Utils.IsRatioEqual(0.333, current.GapTime, previous.GapTime))
                rhythmBonus = 0.4 + 0.8 * current.Flow; // Transition to 1/6s
            else if (Utils.IsRatioEqual(0.5, current.GapTime, previous.GapTime) || Utils.IsRatioEqualLess(0.25, current.GapTime, previous.GapTime))
                rhythmBonus = 0.1 + 0.2 * current.Flow; // Transition to triples, streams etc.

            if (Utils.IsRatioEqualLess(0.667, current.GapTime, previous.GapTime) && current.Flow > 0.8)
                isPreviousOffbeat = true;
            else if (Utils.IsRatioEqual(1, current.GapTime, previous.GapTime) && current.Flow > 0.8)
                isPreviousOffbeat = !isPreviousOffbeat;
            else
                isPreviousOffbeat = false;

            return rhythmBonus;
        }

        private double calculateSliderToCircleRhythmBonus(OsuDifficultyHitObject current)
        {
            double rhythmBonus = 0;
            double sliderMS = current.StrainTime - current.GapTime;

            if (Utils.IsRatioEqual(0.5, current.GapTime, sliderMS) || Utils.IsRatioEqual(0.25, current.GapTime, sliderMS))
            {
                double endFlow = calculateSliderEndFlow(current);
                rhythmBonus = 0.3 * endFlow; // Triples, streams etc. starting with a slider end.

                if (endFlow > 0.8)
                    isPreviousOffbeat = true;
                else
                    isPreviousOffbeat = false;
            }
            else
                isPreviousOffbeat = false;

            return rhythmBonus;
        }

        private static double calculateSliderEndFlow(OsuDifficultyHitObject current)
        {
            double streamBpm = 15000 / current.GapTime;
            double isFlowSpeed = Utils.TransitionToTrue(streamBpm, 120, 30);

            double distanceOffset = (Math.Tanh((streamBpm - 140) / 20) + 2) * OsuDifficultyHitObject.NORMALISED_RADIUS;
            double isFlowDistance = Utils.TransitionToFalse(current.JumpDistance, distanceOffset, OsuDifficultyHitObject.NORMALISED_RADIUS);

            return isFlowSpeed * isFlowDistance;
        }
    }
}
