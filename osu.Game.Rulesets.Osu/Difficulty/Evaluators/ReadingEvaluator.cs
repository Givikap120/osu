// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class ReadingEvaluator
    {
        private const double reading_window_size = 3000;

        private const double overlap_multiplier = 0.8;

        public static double CalculateDenstityOf(OsuDifficultyHitObject currObj)
        {
            double pastObjectDifficultyInfluence = 0;

            foreach (var loopObj in retrievePastVisibleObjects(currObj))
            {
                double loopDifficulty = currObj.OpacityAt(loopObj.BaseObject.StartTime, false);

                // Small distances means objects may be cheesed, so it doesn't matter whether they are arranged confusingly.
                loopDifficulty *= logistic((loopObj.MinimumJumpDistance - 90) / 15);

                //double timeBetweenCurrAndLoopObj = (currObj.BaseObject.StartTime - loopObj.BaseObject.StartTime) / clockRateEstimate;
                double timeBetweenCurrAndLoopObj = currObj.StartTime - loopObj.StartTime;
                loopDifficulty *= getTimeNerfFactor(timeBetweenCurrAndLoopObj);

                pastObjectDifficultyInfluence += loopDifficulty;
            }

            return pastObjectDifficultyInfluence;
        }

        public static double CalculateOverlapDifficultyOf(OsuDifficultyHitObject currObj)
        {
            double screenOverlapDifficulty = 0;

            foreach (var loopObj in retrievePastVisibleObjects(currObj))
            {
                double lastOverlapness = 0;
                foreach (var overlapObj in loopObj.OverlapObjects)
                {
                    if (overlapObj.HitObject.StartTime + overlapObj.HitObject.Preempt > currObj.StartTime) break;
                    lastOverlapness = overlapObj.Overlapness;
                }
                screenOverlapDifficulty += lastOverlapness;
            }

            return screenOverlapDifficulty;
        }
        public static double EvaluateDensityDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner || current.Index == 0)
                return 0;

            var currObj = (OsuDifficultyHitObject)current;

            double pastObjectDifficultyInfluence = CalculateDenstityOf(currObj);
            double screenOverlapDifficulty = CalculateOverlapDifficultyOf(currObj);

            double difficulty = Math.Pow(4 * Math.Log(Math.Max(1, pastObjectDifficultyInfluence)), 2.3);

            screenOverlapDifficulty = Math.Max(0, screenOverlapDifficulty - 0.5); // make overlap value =1 cost significantly less

            double overlapBonus = overlap_multiplier * screenOverlapDifficulty * difficulty;

            difficulty *= getConstantAngleNerfFactor(currObj);
            difficulty += overlapBonus;

            //difficulty *= 1 + overlap_multiplier * screenOverlapDifficulty;

            return difficulty;
        }

        public static double EvaluateHighARDifficultyOf(DifficultyHitObject current, bool applyAdjust = false)
        {
            var currObj = (OsuDifficultyHitObject)current;

            double result = highArCurve(currObj.Preempt);

            if (applyAdjust)
            {
                double inpredictability = EvaluateInpredictabilityOf(current);

                // follow lines make high AR easier, so apply nerf if object isn't new combo
                inpredictability *= 1 + 0.1 * (800 - currObj.FollowLineTime) / 800;

                result *= 0.85 + 0.75 * inpredictability;
            }

            return result;
        }

        public static double EvaluateHiddenDifficultyOf(DifficultyHitObject current)
        {
            var currObj = (OsuDifficultyHitObject)current;

            double aimDifficulty = AimEvaluator.EvaluateDifficultyOf(current, false);

            double hdDifficulty = 0;

            double timeSpentInvisible = getDurationSpentInvisible(currObj) / currObj.ClockRate;

            double density = 1 + Math.Max(0, CalculateDenstityOf(currObj) - 1);
            density *= getConstantAngleNerfFactor(currObj);

            double timeDifficultyFactor = density / 1000;

            double visibleObjectFactor = Math.Clamp(retrieveCurrentVisibleObjects(currObj).Count - 2, 0, 15);

            hdDifficulty += Math.Pow(visibleObjectFactor * timeSpentInvisible * timeDifficultyFactor, 1) +
                            (6 + visibleObjectFactor) * aimDifficulty;

            hdDifficulty *= 0.95 + 0.15 * EvaluateInpredictabilityOf(current); // Max multiplier is 1.1

            return hdDifficulty;
        }

        // Returns value from 0 to 1, where 0 is very predictable and 1 is very unpredictable
        public static double EvaluateInpredictabilityOf(DifficultyHitObject current)
        {
            // make the sum equal to 1
            const double velocity_change_part = 0.3;
            const double angle_change_part = 0.6;
            const double rhythm_change_part = 0.1;

            if (current.BaseObject is Spinner || current.Index == 0 || current.Previous(0).BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);

            double velocityChangeBonus = 0;

            double currVelocity = osuCurrObj.LazyJumpDistance / osuCurrObj.StrainTime;
            double prevVelocity = osuLastObj.LazyJumpDistance / osuLastObj.StrainTime;

            // https://www.desmos.com/calculator/kqxmqc8pkg
            if (currVelocity > 0 || prevVelocity > 0)
            {
                double velocityChange = Math.Max(0,
                Math.Min(
                    Math.Abs(prevVelocity - currVelocity) - 0.5 * Math.Min(currVelocity, prevVelocity),
                    Math.Max(((OsuHitObject)osuCurrObj.BaseObject).Radius / Math.Max(osuCurrObj.StrainTime, osuLastObj.StrainTime), Math.Min(currVelocity, prevVelocity))
                    )); // Stealed from xexxar
                velocityChangeBonus = velocityChange / Math.Max(currVelocity, prevVelocity); // maxiumum is 0.4
                velocityChangeBonus /= 0.4;
            }

            double angleChangeBonus = 0;

            if (osuCurrObj.Angle != null && osuLastObj.Angle != null && currVelocity > 0 && prevVelocity > 0)
            {
                angleChangeBonus = Math.Pow(Math.Sin((double)((osuCurrObj.Angle - osuLastObj.Angle) / 2)), 2); // Also stealed from xexxar
                angleChangeBonus *= Math.Min(currVelocity, prevVelocity) / Math.Max(currVelocity, prevVelocity); // Prevent cheesing
            }

            double rhythmChangeBonus = 0;

            if (current.Index > 1)
            {
                var osuLastLastObj = (OsuDifficultyHitObject)current.Previous(1);

                double currDelta = osuCurrObj.StrainTime;
                double lastDelta = osuLastObj.StrainTime;

                if (osuLastObj.BaseObject is Slider sliderCurr)
                {
                    currDelta -= sliderCurr.Duration / osuCurrObj.ClockRate;
                    currDelta = Math.Max(0, currDelta);
                }

                if (osuLastLastObj.BaseObject is Slider sliderLast)
                {
                    lastDelta -= sliderLast.Duration / osuLastObj.ClockRate;
                    lastDelta = Math.Max(0, lastDelta);
                }

                rhythmChangeBonus = getRhythmDifference(currDelta, lastDelta);
            }

            double result = velocity_change_part * velocityChangeBonus + angle_change_part * angleChangeBonus + rhythm_change_part * rhythmChangeBonus;
            return result;
        }

        public static double EvaluateLowDensityBonusOf(DifficultyHitObject current)
        {
            //var currObj = (OsuDifficultyHitObject)current;

            //// Density = 2 in general means 3 notes on screen (it's not including current note)
            //double density = CalculateDenstityOf(currObj);

            //// We are considering density = 1.5 as starting point, 1.0 is noticably uncomfy and 0.5 is severely uncomfy
            //double bonus = 1.5 - density;
            //if (bonus <= 0) return 0;

            //return Math.Pow(bonus, 2);
            return 0;
        }

        // Returns a list of objects that are visible on screen at
        // the point in time at which the current object becomes visible.
        private static IEnumerable<OsuDifficultyHitObject> retrievePastVisibleObjects(OsuDifficultyHitObject current)
        {
            for (int i = 0; i < current.Index; i++)
            {
                OsuDifficultyHitObject hitObject = (OsuDifficultyHitObject)current.Previous(i);

                if (hitObject.IsNull() ||
                    current.StartTime - hitObject.StartTime > reading_window_size ||
                    hitObject.StartTime < current.StartTime - current.Preempt)
                    break;

                yield return hitObject;
            }
        }

        private static List<OsuDifficultyHitObject> retrieveCurrentVisibleObjects(OsuDifficultyHitObject current)
        {
            List<OsuDifficultyHitObject> objects = new List<OsuDifficultyHitObject>();

            for (int i = 0; i < current.Count; i++)
            {
                OsuDifficultyHitObject hitObject = (OsuDifficultyHitObject)current.Next(i);

                if (hitObject.IsNull() ||
                    (hitObject.StartTime - current.StartTime) > reading_window_size ||
                    current.StartTime < hitObject.StartTime - hitObject.Preempt)
                    break;

                objects.Add(hitObject);
            }

            return objects;
        }

        private static double getDurationSpentInvisible(OsuDifficultyHitObject current)
        {
            var baseObject = (OsuHitObject)current.BaseObject;

            double fadeOutStartTime = baseObject.StartTime - baseObject.TimePreempt + baseObject.TimeFadeIn;
            double fadeOutDuration = baseObject.TimePreempt * OsuModHidden.FADE_OUT_DURATION_MULTIPLIER;

            return (fadeOutStartTime + fadeOutDuration) - (baseObject.StartTime - baseObject.TimePreempt);
        }

        private static double getConstantAngleNerfFactor(OsuDifficultyHitObject current)
        {
            const double time_limit = 2000;
            const double time_limit_low = 200;

            double constantAngleCount = 0;
            int index = 0;
            double currentTimeGap = 0;

            OsuDifficultyHitObject prevLoopObj = current;

            OsuDifficultyHitObject? prevLoopObj1 = null;
            OsuDifficultyHitObject? prevLoopObj2 = null;

            double prevConstantAngle = 0;

            while (currentTimeGap < time_limit)
            {
                var loopObj = (OsuDifficultyHitObject)current.Previous(index);

                if (loopObj.IsNull())
                    break;

                double longIntervalFactor = Math.Clamp(1 - (loopObj.StrainTime - time_limit_low) / (time_limit - time_limit_low), 0, 1);

                if (loopObj.Angle.IsNotNull() && prevLoopObj.Angle.IsNotNull())
                {
                    double angleDifference = Math.Abs(prevLoopObj.Angle.Value - loopObj.Angle.Value);

                    // Nerf alternating angles case
                    if (prevLoopObj1.IsNotNull() && prevLoopObj2.IsNotNull() && prevLoopObj1.Angle.IsNotNull() && prevLoopObj2.Angle.IsNotNull())
                    {
                        // Normalized difference
                        double angleDifference1 = Math.Abs(prevLoopObj1.Angle.Value - loopObj.Angle.Value) / Math.PI;
                        double angleDifference2 = Math.Abs(prevLoopObj2.Angle.Value - prevLoopObj.Angle.Value) / Math.PI;

                        // Will be close to 1 if angleDifference1 and angleDifference2 was both close to 0
                        double alternatingFactor = Math.Pow((1 - angleDifference1) * (1 - angleDifference2), 2);

                        // Be sure to nerf only same rhythms
                        double rhythmFactor = 1 - getRhythmDifference(loopObj.StrainTime, prevLoopObj.StrainTime); // 0 on different rhythm, 1 on same rhythm
                        rhythmFactor *= 1 - getRhythmDifference(prevLoopObj.StrainTime, prevLoopObj1.StrainTime);
                        rhythmFactor *= 1 - getRhythmDifference(prevLoopObj1.StrainTime, prevLoopObj2.StrainTime);

                        double acuteAngleFactor = 1 - Math.Min(loopObj.Angle.Value, prevLoopObj.Angle.Value) / Math.PI;

                        double prevAngleAdjust = Math.Max(angleDifference - angleDifference1, 0);

                        prevAngleAdjust *= alternatingFactor; // Nerf if alternating
                        prevAngleAdjust *= rhythmFactor; // Nerf if same rhythms
                        prevAngleAdjust *= acuteAngleFactor;

                        angleDifference -= prevAngleAdjust;
                    }

                    double currConstantAngle = Math.Cos(4 * Math.Min(Math.PI / 8, angleDifference)) * longIntervalFactor;
                    constantAngleCount += Math.Min(currConstantAngle, prevConstantAngle);
                    prevConstantAngle = currConstantAngle;
                }

                currentTimeGap = current.StartTime - loopObj.StartTime;
                index++;

                prevLoopObj2 = prevLoopObj1;
                prevLoopObj1 = prevLoopObj;
                prevLoopObj = loopObj;
            }

            return Math.Pow(Math.Min(1, 2 / constantAngleCount), 2);
        }

        private static double getTimeNerfFactor(double deltaTime)
        {
            return Math.Clamp(2 - deltaTime / (reading_window_size / 2), 0, 1);
        }

        // https://www.desmos.com/calculator/hbj7swzlth
        private static double highArCurve(double preempt)
        {
            double value = Math.Pow(3, 3 - 0.01 * preempt); // 1 for 300ms, 0.25 for 400ms, 0.0625 for 500ms
            value = softmin(value, 2, 1.7); // use softmin to achieve full-memory cap, 2 times more than AR11 (300ms)
            return value;
        }

        private static double getRhythmDifference(double t1, double t2) => 1 - Math.Min(t1, t2) / Math.Max(t1, t2);
        private static double logistic(double x) => 1 / (1 + Math.Exp(-x));

        // We are using mutiply and divide instead of add and subtract, so values won't be negative
        // https://www.desmos.com/calculator/fv5xerwpd2
        private static double softmin(double a, double b, double power = Math.E) => a * b / Math.Log(Math.Pow(power, a) + Math.Pow(power, b), power);
    }
}
