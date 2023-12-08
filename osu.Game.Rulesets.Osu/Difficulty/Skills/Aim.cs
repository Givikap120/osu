// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty.Evaluators;
using osu.Game.Rulesets.Difficulty.Skills;
using System.Collections.Generic;

namespace osu.Game.Rulesets.Osu.Difficulty.Skills
{
    /// <summary>
    /// Represents the skill required to correctly aim at every object in the map with a uniform CircleSize and normalized distances.
    /// </summary>
    public class Aim : GraphSkill
    {
        public Aim(Mod[] mods, bool withSliders)
            : base(mods)
        {
            totalAim = new TotalAim(mods, withSliders);
            snapAim = new SnapAim(mods, withSliders);
            flowAim = new FlowAim(mods, withSliders);
            sliderAim = new SliderAim(mods, withSliders);
        }

        private TotalAim totalAim;
        private SnapAim snapAim;
        private FlowAim flowAim;
        private SliderAim sliderAim;

        public override void Process(DifficultyHitObject current)
        {
            totalAim.Process(current);
            snapAim.Process(current);
            flowAim.Process(current);
            sliderAim.Process(current);
        }
        public override IEnumerable<double> GetSectionPeaks() => totalAim.GetSectionPeaks();

        private const double mixed_aim_part = 0.32;
        private const double poly_power = 1.4;
        public override double DifficultyValue()
        {
            double totalDifficulty = totalAim.DifficultyValue();
            double snapDifficulty = snapAim.DifficultyValue();
            double flowDifficulty = flowAim.DifficultyValue();
            double sliderDifficulty = sliderAim.DifficultyValue();

            double polySum = Math.Pow(
                Math.Pow(snapDifficulty, poly_power) +
                Math.Pow(flowDifficulty, poly_power) +
                Math.Pow(sliderDifficulty, poly_power)
                , 1 / poly_power);

            double difficulty = totalDifficulty * (1 - mixed_aim_part) + polySum * mixed_aim_part;

            double debugPerc = difficulty / totalDifficulty - 1;
            string sign = debugPerc > 0 ? "+" : "";
            Console.WriteLine($"Snap - {snapDifficulty:0}, Flow - {flowDifficulty:0}, Slider - {sliderDifficulty:0}, Total - {totalDifficulty:0}, " +
                $"Result - {difficulty:0} ({sign}{100 * debugPerc:0.00}%)");

            return difficulty;
        }
    }

    public class TotalAim : BaseAim
    {
        public TotalAim(Mod[] mods, bool WithSliders)
            : base(mods, WithSliders)
        {
        }
        protected override double StrainValueAt(DifficultyHitObject current)
        {
            CurrentStrain *= StrainDecay(current.DeltaTime);

            (double snap, double flow) difficulties = AimEvaluator.EvaluateRawDifficultiesOf(current, WithSliders, StrainDecayBase);
            CurrentStrain += AimEvaluator.EvaluateTotalStrainOf(current, WithSliders, StrainDecayBase, difficulties);

            return CurrentStrain * SkillMultiplier;
        }
    }

    public class SnapAim : BaseAim
    {
        public SnapAim(Mod[] mods, bool WithSliders)
            : base(mods, WithSliders)
        {
        }
        protected override double StrainValueAt(DifficultyHitObject current)
        {
            CurrentStrain *= StrainDecay(current.DeltaTime);

            (double snap, double flow) difficulties = AimEvaluator.EvaluateRawDifficultiesOf(current, WithSliders, StrainDecayBase);
            CurrentStrain += AimEvaluator.EvaluateSnapStrainOf(current, WithSliders, StrainDecayBase, difficulties);

            return CurrentStrain * SkillMultiplier;
        }
    }

    public class FlowAim : BaseAim
    {
        public FlowAim(Mod[] mods, bool WithSliders)
            : base(mods, WithSliders)
        {
        }
        protected override double StrainValueAt(DifficultyHitObject current)
        {
            CurrentStrain *= StrainDecay(current.DeltaTime);

            (double snap, double flow) difficulties = AimEvaluator.EvaluateRawDifficultiesOf(current, WithSliders, StrainDecayBase);
            CurrentStrain += AimEvaluator.EvaluateFlowStrainOf(current, WithSliders, StrainDecayBase, difficulties);

            return CurrentStrain * SkillMultiplier;
        }
    }

    public class SliderAim : BaseAim
    {
        public SliderAim(Mod[] mods, bool WithSliders)
            : base(mods, WithSliders)
        {
        }
        protected override double DecayWeight => 0.8; // decays faster
        protected override double SkillMultiplier => 50; // but more difficulty
        protected override double StrainValueAt(DifficultyHitObject current)
        {
            if (!WithSliders) return 0; // no sliders = no difficulty

            CurrentStrain *= StrainDecay(current.DeltaTime);

            CurrentStrain += AimEvaluator.EvaluateTotalStrainOf(current, WithSliders, StrainDecayBase, (0, 0));

            return CurrentStrain * SkillMultiplier;
        }


    }

    public abstract class BaseAim : OsuStrainSkill
    {
        public BaseAim(Mod[] mods, bool withSliders)
            : base(mods)
        {
            WithSliders = withSliders;
        }

        protected readonly bool WithSliders;

        protected double CurrentStrain;
        protected double StrainDecayBase => 0.15;

        protected virtual double SkillMultiplier => 26.3;

        protected double StrainDecay(double ms) => Math.Pow(StrainDecayBase, ms / 1000);

        protected override double CalculateInitialStrain(double time, DifficultyHitObject current) => CurrentStrain * StrainDecay(time - current.Previous(0).StartTime);
    }

}
