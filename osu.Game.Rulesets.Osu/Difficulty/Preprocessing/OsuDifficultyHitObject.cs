// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuDifficultyHitObject : DifficultyHitObject
    {
        /// <summary>
        /// A distance by which all distances should be scaled in order to assume a uniform circle size.
        /// </summary>
        public const int NORMALISED_RADIUS = 52; // Change radius to 50 to make 100 the diameter. Easier for mental maths.

        public int MinDeltaTime => OsuDifficultyCalculator.DISABLE_300BPM_SPEED_LIMIT ? 25 : 50;
        
        public const int NORMALISED_DIAMETER = NORMALISED_RADIUS * 2;

        private const float maximum_slider_radius = NORMALISED_RADIUS * 2.4f;
        private const float assumed_slider_radius = NORMALISED_RADIUS * 1.8f;

        protected new OsuHitObject BaseObject => (OsuHitObject)base.BaseObject;
        protected new OsuHitObject LastObject => (OsuHitObject)base.LastObject;

        /// <summary>
        /// Raw distance from the end position of the previous <see cref="OsuDifficultyHitObject"/> to the start position of this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double RawJumpDistance { get; private set; }

        /// <summary>
        /// Normalized distance from the end position of the previous <see cref="OsuDifficultyHitObject"/> to the start position of this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double JumpDistance { get; private set; }

        /// <summary>
        /// Normalized distance from the end position of the previous <see cref="OsuDifficultyHitObject"/> to the start position of this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public readonly double StrainTime;

        /// <summary>
        /// Angle the player has to take to hit this <see cref="OsuDifficultyHitObject"/>.
        /// Calculated as the angle between the circles (current-2, current-1, current).
        /// </summary>
        public double? Angle { get; private set; }

        /// <summary>
        /// Approach rate of <see cref="OsuDifficultyHitObject"/> in milliseconds.
        /// </summary>
        public double Preempt { get; private set; }

        /// <summary>
        /// Measure of angle leniency to be given when calculating the flow values of the next <see cref="OsuDifficultyHitObject"/> (scale of [0, 1]).
        /// </summary>
        public double AngleLeniency { get; private set; }

        /// <summary>
        /// Measure of expected aim flowiness based on time and distance from the previous <see cref="OsuDifficultyHitObject"/> (scale of [0, 1]).
        /// </summary>
        public double BaseFlow { get; private set; }

        /// <summary>
        /// Measure of expected aim flowiness based on <see cref="BaseFlow"/> and pattern context made up of the previous <see cref="OsuDifficultyHitObject"/>s (scale of [0, 1]).
        /// </summary>
        public double Flow { get; private set; }

        /// <summary>
        /// Milliseconds elapsed since the start time of the <see cref="OsuDifficultyHitObject"/> before the previous, with a minimum of 100ms.
        /// </summary>
        public double LastTwoStrainTime { get; private set; }

        /// <summary>
        /// Milliseconds elapsed since the end time of the previous <see cref="OsuDifficultyHitObject"/>, with a minimum of 50ms.
        /// </summary>
        public double GapTime { get; private set; }

        /// <summary>
        /// Retrieves the full hit window for a Great <see cref="HitResult"/>.
        /// </summary>
        public double HitWindowGreat { get; private set; }

        public Vector2? LazyEndPosition { get; private set; }
        public double LazyTravelDistance { get; private set; }
        public double LazyTravelTime { get; private set; }
        public double TravelDistance { get; private set; }

        // Placeholders
        public double LazyJumpDistance { get; }
        public double TravelTime { get; }
        public double MinimumJumpDistance { get; }
        public double MinimumJumpTime { get; }

        private readonly OsuDifficultyHitObject? lastLastDifficultyObject;
        private readonly OsuDifficultyHitObject? lastDifficultyObject;

        public OsuDifficultyHitObject(HitObject hitObject, HitObject lastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
            lastLastDifficultyObject = index > 1 ? (OsuDifficultyHitObject)objects[index - 2] : null;
            lastDifficultyObject = index > 0 ? (OsuDifficultyHitObject)objects[index - 1] : null;

            computeSliderCursorPosition();
            setDistances(clockRate);

            Preempt = ((OsuHitObject)hitObject).TimePreempt / clockRate;

            StrainTime = Math.Max(MinDeltaTime, DeltaTime);

            if (lastLastDifficultyObject == null)
                LastTwoStrainTime = OsuDifficultyCalculator.ENABLE_FIRST_SPEED_NOTE_FIX ? double.PositiveInfinity : 100;
            else
                LastTwoStrainTime = Math.Max(MinDeltaTime * 2, (hitObject.StartTime - lastLastDifficultyObject.BaseObject.StartTime) / clockRate);

            if (lastObject is HitCircle)
                GapTime = StrainTime;
            else if (lastObject is Slider lastSlider)
                GapTime = Math.Max(MinDeltaTime, (hitObject.StartTime - lastSlider.EndTime) / clockRate);
            else if (lastObject is Spinner lastSpinner)
                GapTime = Math.Max(MinDeltaTime, (hitObject.StartTime - lastSpinner.EndTime) / clockRate);

            // WARNING - this is a very stupid bandaid, but without it speed is very broken because of flawed curve
            if (OsuDifficultyCalculator.DISABLE_300BPM_SPEED_LIMIT)
            {
                static double rescaleLowStrainTime(double strainTime, double minStrainTime, double targetMinStrainTime, double lowStrainTimeThreshold)
                    => strainTime < lowStrainTimeThreshold ? double.Lerp(targetMinStrainTime, lowStrainTimeThreshold, (strainTime - minStrainTime) / minStrainTime) : strainTime;

                StrainTime = rescaleLowStrainTime(StrainTime, 25, 30, 50);
                LastTwoStrainTime = rescaleLowStrainTime(LastTwoStrainTime, 50, 60, 100);
                GapTime = rescaleLowStrainTime(GapTime, 25, 30, 50);
            }

            setFlowValues();
        }

        public double OpacityAt(double time, bool hidden)
        {
            if (time > BaseObject.StartTime)
            {
                // Consider a hitobject as being invisible when its start time is passed.
                // In reality the hitobject will be visible beyond its start time up until its hittable window has passed,
                // but this is an approximation and such a case is unlikely to be hit where this function is used.
                return 0.0;
            }

            double fadeInStartTime = BaseObject.StartTime - BaseObject.TimePreempt;
            double fadeInDuration = BaseObject.TimeFadeIn;

            if (hidden)
            {
                // Taken from OsuModHidden.
                double fadeOutStartTime = BaseObject.StartTime - BaseObject.TimePreempt + BaseObject.TimeFadeIn;
                double fadeOutDuration = BaseObject.TimePreempt * OsuModHidden.FADE_OUT_DURATION_MULTIPLIER;

                return Math.Min
                (
                    Math.Clamp((time - fadeInStartTime) / fadeInDuration, 0.0, 1.0),
                    1.0 - Math.Clamp((time - fadeOutStartTime) / fadeOutDuration, 0.0, 1.0)
                );
            }

            return Math.Clamp((time - fadeInStartTime) / fadeInDuration, 0.0, 1.0);
        }

        /// <summary>
        /// Returns how possible is it to doubletap this object together with the next one and get perfect judgement in range from 0 to 1
        /// </summary>
        public double GetDoubletapness(OsuDifficultyHitObject? osuNextObj)
        {
            if (osuNextObj != null)
            {
                double currDeltaTime = Math.Max(1, DeltaTime);
                double nextDeltaTime = Math.Max(1, osuNextObj.DeltaTime);
                double deltaDifference = Math.Abs(nextDeltaTime - currDeltaTime);
                double speedRatio = currDeltaTime / Math.Max(currDeltaTime, deltaDifference);
                double windowRatio = Math.Pow(Math.Min(1, currDeltaTime / HitWindowGreat), 2);
                return 1.0 - Math.Pow(speedRatio, 1 - windowRatio);
            }

            return 0;
        }

        private void setDistances(double clockRate)
        {
            // We will scale distances by this factor, so we can assume a uniform CircleSize among beatmaps.
            float scalingFactor = NORMALISED_RADIUS / (float)BaseObject.Radius;

            if (BaseObject.Radius < 30)
            {
                float smallCircleBonus = Math.Min(30 - (float)BaseObject.Radius, 5) / 50;
                scalingFactor *= 1 + smallCircleBonus;
            }

            if (LastObject is Slider && lastDifficultyObject != null)
            {
                computeSliderCursorPosition();
                TravelDistance = lastDifficultyObject.LazyTravelDistance * scalingFactor;
            }

            Vector2 lastCursorPosition = lastDifficultyObject != null ? getEndCursorPosition(lastDifficultyObject) : LastObject.StackedPosition;

            // Don't need to jump to reach spinners
            if (!(BaseObject is Spinner))
                RawJumpDistance = (BaseObject.StackedPosition - lastCursorPosition).Length;
            JumpDistance = (BaseObject.StackedPosition * scalingFactor - lastCursorPosition * scalingFactor).Length;

            if (lastLastDifficultyObject != null)
            {
                Vector2 lastLastCursorPosition = getEndCursorPosition(lastLastDifficultyObject);

                Vector2 v1 = lastLastCursorPosition - LastObject.StackedPosition;
                Vector2 v2 = BaseObject.StackedPosition - lastCursorPosition;

                float dot = Vector2.Dot(v1, v2);
                float det = v1.X * v2.Y - v1.Y * v2.X;

                Angle = Math.Abs(Math.Atan2(det, dot));
            }
        }

        private void computeSliderCursorPosition()
        {
            if (BaseObject is not Slider slider)
                return;

            if (LazyEndPosition != null)
                return;

            LazyEndPosition = slider.StackedPosition;

            float approxFollowCircleRadius = (float)(slider.Radius * 3);
            var computeVertex = new Action<double>(t =>
            {
                double progress = (t - slider.StartTime) / slider.SpanDuration;
                if (progress % 2 >= 1)
                    progress = 1 - progress % 1;
                else
                    progress %= 1;

                // ReSharper disable once PossibleInvalidOperationException (bugged in current r# version)
                var diff = slider.StackedPosition + slider.Path.PositionAt(progress) - LazyEndPosition.Value;
                float dist = diff.Length;

                if (dist > approxFollowCircleRadius)
                {
                    // The cursor would be outside the follow circle, we need to move it
                    diff.Normalize(); // Obtain direction of diff
                    dist -= approxFollowCircleRadius;
                    LazyEndPosition = LazyEndPosition! + diff * dist;
                    LazyTravelDistance += dist;
                }
            });

            // Skip the head circle
            var scoringTimes = slider.NestedHitObjects.Skip(1).Select(t => t.StartTime);
            foreach (var time in scoringTimes)
                computeVertex(time);
        }

        private Vector2 getEndCursorPosition(OsuDifficultyHitObject difficultyHitObject)
        {
            return difficultyHitObject.LazyEndPosition ?? difficultyHitObject.BaseObject.StackedPosition;
        }

        private void setFlowValues()
        {
            BaseFlow = calculateBaseFlow();
            Flow = calculateFlow();
        }

        private double calculateBaseFlow()
        {
            if (lastDifficultyObject == null || OsuPlusUtils.IsRatioEqualLess(0.667, StrainTime, lastDifficultyObject.StrainTime))
                return calculateSpeedFlow() * calculateDistanceFlow(); // No angle checks for the first actual note of the stream.

            if (OsuPlusUtils.IsRoughlyEqual(StrainTime, lastDifficultyObject.StrainTime))
                return calculateSpeedFlow() * calculateDistanceFlow(calculateAngleScalingFactor(Angle));

            return 0;
        }

        private double calculateSpeedFlow()
        {
            // Sine curve transition from 0 to 1 starting at 90 BPM, reaching 1 at 90 + 30 = 120 BPM.
            return OsuPlusUtils.TransitionToTrue(streamBpm, 90, 30);
        }

        private double calculateDistanceFlow(double angleScalingFactor = 1)
        {
            double distanceOffset = (Math.Tanh((streamBpm - 140) / 20) + 2) * NORMALISED_RADIUS;
            return OsuPlusUtils.TransitionToFalse(JumpDistance, distanceOffset * angleScalingFactor, distanceOffset);
        }

        private double calculateAngleScalingFactor(double? angle)
        {
            if (!OsuPlusUtils.IsNullOrNaN(angle))
            {
                double angleScalingFactor = (-Math.Sin(Math.Cos(angle!.Value) * Math.PI / 2) + 3) / 4;
                return angleScalingFactor + (1 - angleScalingFactor) * lastDifficultyObject.AngleLeniency;
            }
            else
                return 0.5;
        }

        private double calculateFlow()
        {
            if (lastDifficultyObject == null)
                return BaseFlow;

            // No angle check and a larger distance is allowed if the speed matches the previous notes, and those were flowy without a question.
            // (streamjumps, sharp turns)
            double irregularFlow = calculateIrregularFlow();

            // The next note will have lenient angle checks after a note with irregular flow.
            // (the stream section after the streamjump can take any direction too)
            AngleLeniency = (1 - BaseFlow) * irregularFlow;

            return Math.Max(BaseFlow, irregularFlow);
        }

        private double calculateIrregularFlow()
        {
            double irregularFlow = calculateExtendedDistanceFlow();

            if (OsuPlusUtils.IsRoughlyEqual(StrainTime, lastDifficultyObject.StrainTime))
                irregularFlow *= lastDifficultyObject.BaseFlow;
            else
                irregularFlow = 0;

            if (lastLastDifficultyObject != null)
            if (OsuPlusUtils.IsRoughlyEqual(StrainTime, lastLastDifficultyObject.StrainTime))
                irregularFlow *= lastLastDifficultyObject.BaseFlow;
            else
                irregularFlow = 0;

            return irregularFlow;
        }

        private double calculateExtendedDistanceFlow()
        {
            double distanceOffset = (Math.Tanh((streamBpm - 140) / 20) * 1.75 + 2.75) * NORMALISED_RADIUS;
            return OsuPlusUtils.TransitionToFalse(JumpDistance, distanceOffset, distanceOffset);
        }

        private double streamBpm => 15000 / StrainTime;
    }
}
